using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CampusTrack.RfidGateway.Protocol;
using CampusTrack.RfidGateway.Readers;
using Microsoft.Extensions.Options;

namespace CampusTrack.RfidGateway;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>Base URL of the CampusTrack API, e.g. https://school.example.com</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Readers this gateway drives.</summary>
    public List<GatewayReaderOptions> Readers { get; set; } = [];

    /// <summary>How often buffered reads are forwarded. Short enough to feel immediate.</summary>
    public int FlushIntervalMs { get; set; } = 500;

    /// <summary>Maximum reads per POST. The API rejects larger batches.</summary>
    public int MaxBatchSize { get; set; } = 400;

    /// <summary>
    /// Reads held while the API is unreachable. At roughly 50 reads/second per busy reader
    /// this covers about half an hour of a full gate before the oldest are dropped.
    /// </summary>
    public int OfflineBufferLimit { get; set; } = 100_000;

    public int HeartbeatSeconds { get; set; } = 60;
}

public sealed class GatewayReaderOptions : ReaderConnectionOptions
{
    /// <summary>Per-device key issued in Admin → RFID → Readers → Issue key.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Runs on the school network, between the readers and the API.
///
/// It exists for two reasons. Readers speak a binary serial protocol that no web API should
/// have to parse, and a school's internet connection is not reliable enough to sit directly
/// in the path of attendance. The gateway translates, and it buffers: if the line drops
/// mid-morning, reads keep accumulating locally and arrive with their original timestamps
/// when it returns, so the register is right even though the network was not.
/// </summary>
public sealed class GatewayWorker : BackgroundService
{
    private readonly GatewayOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GatewayWorker> _logger;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<PendingRead>> _pending = new();
    private readonly ConcurrentDictionary<string, GatewayReaderOptions> _readerConfig = new();
    private readonly ConcurrentDictionary<string, long> _dropped = new();

    public GatewayWorker(
        IOptions<GatewayOptions> options,
        IHttpClientFactory httpFactory,
        ILoggerFactory loggerFactory,
        ILogger<GatewayWorker> logger)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    private sealed record PendingRead(string Epc, int AntennaNumber, DateTime ReadAtUtc, int Rssi);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Readers.Count == 0)
        {
            _logger.LogWarning("No readers are configured. Add them under Gateway:Readers in appsettings.");
            return;
        }

        _logger.LogInformation("Gateway starting with {Count} reader(s), forwarding to {Api}",
            _options.Readers.Count, _options.ApiBaseUrl);

        var tasks = new List<Task>();

        foreach (var config in _options.Readers)
        {
            _readerConfig[config.DeviceId] = config;
            _pending[config.DeviceId] = new ConcurrentQueue<PendingRead>();
            tasks.Add(RunReaderAsync(config, stoppingToken));
        }

        tasks.Add(FlushLoopAsync(stoppingToken));
        tasks.Add(HeartbeatLoopAsync(stoppingToken));

        await Task.WhenAll(tasks);
    }

    private async Task RunReaderAsync(GatewayReaderOptions config, CancellationToken token)
    {
        var reader = new D2184Reader(config, _loggerFactory.CreateLogger<D2184Reader>());

        reader.TagRead += tag =>
        {
            var queue = _pending[config.DeviceId];

            // Bounded: a reader left running against an unreachable API must degrade
            // visibly rather than consume the host's memory.
            if (queue.Count >= _options.OfflineBufferLimit)
            {
                queue.TryDequeue(out _);
                var dropped = _dropped.AddOrUpdate(config.DeviceId, 1, (_, current) => current + 1);

                if (dropped % 1000 == 1)
                {
                    _logger.LogError(
                        "{DeviceId}: offline buffer is full; {Dropped} read(s) discarded. The API has been unreachable for some time.",
                        config.DeviceId, dropped);
                }
            }

            queue.Enqueue(new PendingRead(tag.Epc, tag.AntennaNumber, tag.ObservedAtUtc, tag.RssiDbm));
        };

        reader.ConnectionChanged += (connected, message) =>
        {
            _logger.LogInformation("{DeviceId} is {State}{Detail}",
                config.DeviceId,
                connected ? "online" : "offline",
                string.IsNullOrWhiteSpace(message) ? "" : $" ({message})");
        };

        await using (reader)
        {
            await reader.RunAsync(token);
        }
    }

    private async Task FlushLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.FlushIntervalMs));

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                foreach (var deviceId in _pending.Keys)
                {
                    await FlushDeviceAsync(deviceId, token);
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }

        // Best effort on the way out, so reads captured in the last half-second are not lost.
        using var final = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var deviceId in _pending.Keys)
        {
            try { await FlushDeviceAsync(deviceId, final.Token); } catch { /* shutting down */ }
        }
    }

    private async Task FlushDeviceAsync(string deviceId, CancellationToken token)
    {
        var queue = _pending[deviceId];
        if (queue.IsEmpty) return;

        var config = _readerConfig[deviceId];

        var batch = new List<PendingRead>(_options.MaxBatchSize);
        while (batch.Count < _options.MaxBatchSize && queue.TryDequeue(out var read))
        {
            batch.Add(read);
        }

        if (batch.Count == 0) return;

        // A stable id per batch makes the POST idempotent: if the response is lost after the
        // server accepted it, the retry is recognised and discarded rather than doubling
        // every arrival in the batch.
        var batchId = Guid.NewGuid().ToString("N");

        var payload = new
        {
            deviceId,
            batchId,
            reads = batch.Select(r => new
            {
                epc = r.Epc,
                antennaNumber = r.AntennaNumber,
                readAtUtc = r.ReadAtUtc,
                rssi = r.Rssi,
            }),
        };

        try
        {
            var client = _httpFactory.CreateClient("api");
            client.BaseAddress = new Uri(_options.ApiBaseUrl);
            client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
            client.DefaultRequestHeaders.Add("X-Device-Key", config.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.PostAsJsonAsync("/api/v1/rfid/reads", payload, token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("{DeviceId}: forwarded {Count} read(s)", deviceId, batch.Count);
                return;
            }

            // 4xx will not succeed on retry — a bad key or a device the server does not know
            // — so the batch is dropped with a loud message rather than retried forever.
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                var body = await response.Content.ReadAsStringAsync(token);
                _logger.LogError(
                    "{DeviceId}: server rejected {Count} read(s) with {Status}. Check the device key and that the reader is registered. {Body}",
                    deviceId, batch.Count, response.StatusCode, Truncate(body, 300));
                return;
            }

            RequeueFront(queue, batch);
            _logger.LogWarning("{DeviceId}: server returned {Status}; {Count} read(s) requeued",
                deviceId, response.StatusCode, batch.Count);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            RequeueFront(queue, batch);
            _logger.LogWarning("{DeviceId}: API unreachable; {Count} read(s) buffered ({Total} waiting)",
                deviceId, batch.Count, queue.Count);
        }
    }

    /// <summary>
    /// Puts an undelivered batch back at the head of the queue so chronological order is
    /// preserved. Out-of-order reads would corrupt the antenna sequence the server uses to
    /// decide direction.
    /// </summary>
    private static void RequeueFront(ConcurrentQueue<PendingRead> queue, List<PendingRead> batch)
    {
        var tail = new List<PendingRead>();
        while (queue.TryDequeue(out var item)) tail.Add(item);

        foreach (var item in batch) queue.Enqueue(item);
        foreach (var item in tail) queue.Enqueue(item);
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.HeartbeatSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                foreach (var (deviceId, config) in _readerConfig)
                {
                    try
                    {
                        var client = _httpFactory.CreateClient("api");
                        client.BaseAddress = new Uri(_options.ApiBaseUrl);
                        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
                        client.DefaultRequestHeaders.Add("X-Device-Key", config.ApiKey);

                        await client.PostAsJsonAsync("/api/v1/rfid/heartbeat", new
                        {
                            deviceId,
                            ipAddress = config.Host,
                            telemetry = new { queuedReads = _pending[deviceId].Count },
                        }, token);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        // The flush loop already reports connectivity; no need to repeat it.
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
