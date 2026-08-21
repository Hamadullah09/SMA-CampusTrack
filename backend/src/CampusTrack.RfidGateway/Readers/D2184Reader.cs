using System.IO.Ports;
using System.Net.Sockets;
using CampusTrack.RfidGateway.Protocol;
using Microsoft.Extensions.Logging;

namespace CampusTrack.RfidGateway.Readers;

public enum ReaderTransport { Tcp, Serial }

public class ReaderConnectionOptions
{
    /// <summary>Must match the DeviceId registered in CampusTrack.</summary>
    public string DeviceId { get; set; } = string.Empty;

    public ReaderTransport Transport { get; set; } = ReaderTransport.Tcp;

    // TCP (the D2184B network module, or an IPort-3 serial-to-Ethernet bridge)
    public string Host { get; set; } = "192.168.1.200";
    public int Port { get; set; } = 4001;

    // Serial
    public string SerialPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 115200;

    /// <summary>Reader address on the bus. 0xFF broadcasts to any address.</summary>
    public byte Address { get; set; } = 0xFF;

    /// <summary>Antenna ports in use, 1-4.</summary>
    public int[] Antennas { get; set; } = [1, 2];

    /// <summary>
    /// Repeat count for each inventory round. 0xFF is the shortest round, which gives the
    /// fastest reaction at a doorway.
    /// </summary>
    public byte InventoryRepeat { get; set; } = 0xFF;

    /// <summary>Milliseconds each antenna dwells before the reader switches to the next.</summary>
    public byte AntennaDwellMs { get; set; } = 20;

    /// <summary>Transmit power in dBm per antenna (20-33 on this hardware).</summary>
    public byte OutputPowerDbm { get; set; } = 30;

    /// <summary>Reads weaker than this are dropped at the gateway before they leave the site.</summary>
    public int MinimumRssiDbm { get; set; } = -70;
}

/// <summary>
/// Drives one physical D2184 reader.
///
/// Responsibilities are deliberately narrow: connect, keep the reader in real-time
/// inventory, decode tag reports, and raise them. It does not decide direction, resolve
/// students or judge attendance — those need context the device does not have and belong
/// to the server, which is what keeps this class swappable for another reader model.
/// </summary>
public sealed class D2184Reader : IAsyncDisposable
{
    private readonly ReaderConnectionOptions _options;
    private readonly ILogger<D2184Reader> _logger;
    private readonly D2184FrameReader _frames = new();

    private TcpClient? _tcp;
    private NetworkStream? _tcpStream;
    private SerialPort? _serial;
    private CancellationTokenSource? _sessionCts;

    public D2184Reader(ReaderConnectionOptions options, ILogger<D2184Reader> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string DeviceId => _options.DeviceId;
    public bool IsConnected { get; private set; }

    /// <summary>Raised for every tag sighting that passes the RSSI floor.</summary>
    public event Action<D2184TagReport>? TagRead;

    /// <summary>Raised when the link comes up or goes down, so the host can report status.</summary>
    public event Action<bool, string?>? ConnectionChanged;

    /// <summary>
    /// Connects and streams until cancelled, reconnecting with backoff.
    ///
    /// A school reader must survive a switch reboot or a cable knocked loose during the day
    /// without anyone restarting a service, so failure here is a normal state to recover
    /// from rather than an exception to propagate.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(cancellationToken);
                attempt = 0;

                SetConnected(true, null);
                await ConfigureAsync(cancellationToken);
                await PumpAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetConnected(false, ex.Message);
                _logger.LogWarning(ex, "Reader {DeviceId} disconnected", _options.DeviceId);
            }
            finally
            {
                CloseTransport();
            }

            if (cancellationToken.IsCancellationRequested) break;

            // Backs off to 30s. Retrying every second against a reader that is switched off
            // for the holidays just fills the log.
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt++, 5))));
            _logger.LogInformation("Reconnecting to {DeviceId} in {Delay}s", _options.DeviceId, delay.TotalSeconds);

            try { await Task.Delay(delay, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }

        SetConnected(false, "Stopped");
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _frames.Reset();

        if (_options.Transport == ReaderTransport.Tcp)
        {
            _tcp = new TcpClient
            {
                // Tag reports are tiny and latency-critical; Nagle would batch them.
                NoDelay = true,
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            await _tcp.ConnectAsync(_options.Host, _options.Port, timeout.Token);
            _tcpStream = _tcp.GetStream();

            _logger.LogInformation("Connected to {DeviceId} at {Host}:{Port}",
                _options.DeviceId, _options.Host, _options.Port);
        }
        else
        {
            _serial = new SerialPort(_options.SerialPort, _options.BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
            };

            _serial.Open();

            _logger.LogInformation("Opened {DeviceId} on {Port} at {Baud} baud",
                _options.DeviceId, _options.SerialPort, _options.BaudRate);
        }
    }

    /// <summary>
    /// Applies the configuration the site needs, then starts inventory. Sent every time the
    /// link is established because a reader that rebooted has reverted to its own defaults.
    /// </summary>
    private async Task ConfigureAsync(CancellationToken cancellationToken)
    {
        await SendAsync(D2184Command.GetFirmwareVersion, cancellationToken);

        var power = Math.Clamp(_options.OutputPowerDbm, (byte)20, (byte)33);
        await SendAsync(D2184Command.SetOutputPower, cancellationToken, power, power, power, power);

        // A short beep on each read is useful during installation and unbearable in a
        // corridor, so it is off.
        await SendAsync(D2184Command.SetBeeperMode, cancellationToken, 0x00);

        await StartInventoryAsync(cancellationToken);
    }

    /// <summary>
    /// Starts inventory. With more than one antenna the fast-switch command polls them in
    /// one round, which is far better at a gate than switching antennas from the host: the
    /// reader sequences them itself with no round-trip between each.
    /// </summary>
    public async Task StartInventoryAsync(CancellationToken cancellationToken)
    {
        var antennas = _options.Antennas.Where(a => a is >= 1 and <= 4).Distinct().ToArray();

        if (antennas.Length <= 1)
        {
            var antenna = antennas.Length == 1 ? (byte)(antennas[0] - 1) : (byte)0;
            await SendAsync(D2184Command.SetWorkAntenna, cancellationToken, antenna);
            await SendAsync(D2184Command.RealTimeInventory, cancellationToken, _options.InventoryRepeat);
            return;
        }

        // A/B/C/D each take an antenna index (0-3) followed by its dwell count; an index
        // above 3 means "skip this slot".
        var payload = new byte[10];
        for (var slot = 0; slot < 4; slot++)
        {
            payload[slot * 2] = slot < antennas.Length ? (byte)(antennas[slot] - 1) : (byte)0xFF;
            payload[slot * 2 + 1] = slot < antennas.Length ? (byte)1 : (byte)0;
        }

        payload[8] = _options.AntennaDwellMs;
        payload[9] = _options.InventoryRepeat;

        await SendAsync(D2184Command.FastSwitchAntInventory, cancellationToken, payload);
    }

    /// <summary>Reads from the transport and dispatches decoded frames until the link drops.</summary>
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _sessionCts.Token;

        var buffer = new byte[4096];

        // The reader stops inventorying once its repeat count is exhausted, so the round is
        // restarted periodically to keep the doorway continuously armed.
        using var restartTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var restartLoop = RestartInventoryLoopAsync(restartTimer, token);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await ReadAsync(buffer, token);

                if (read == 0)
                {
                    // A clean zero-length read on TCP means the peer closed the connection.
                    if (_options.Transport == ReaderTransport.Tcp)
                        throw new IOException("The reader closed the connection.");

                    await Task.Delay(20, token);
                    continue;
                }

                foreach (var frame in _frames.Append(buffer.AsSpan(0, read)))
                {
                    Dispatch(frame);
                }
            }
        }
        finally
        {
            await _sessionCts.CancelAsync();
            try { await restartLoop; } catch { /* already shutting down */ }
        }
    }

    private async Task RestartInventoryLoopAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await StartInventoryAsync(token);
            }
        }
        catch (OperationCanceledException) { /* link closing */ }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not restart inventory on {DeviceId}", _options.DeviceId);
        }
    }

    private async Task<int> ReadAsync(byte[] buffer, CancellationToken token)
    {
        if (_tcpStream is not null)
            return await _tcpStream.ReadAsync(buffer, token);

        if (_serial is { IsOpen: true })
        {
            // SerialPort has no cancellable async read, so poll the buffer instead of
            // blocking on a read that cannot be interrupted at shutdown.
            var available = _serial.BytesToRead;
            if (available == 0) return 0;

            return _serial.Read(buffer, 0, Math.Min(available, buffer.Length));
        }

        throw new InvalidOperationException("No transport is open.");
    }

    private void Dispatch(D2184Frame frame)
    {
        if (frame.Command is not (D2184Command.RealTimeInventory or D2184Command.FastSwitchAntInventory))
        {
            _logger.LogDebug("{DeviceId}: response to command 0x{Cmd:X2} ({Bytes} data byte(s))",
                _options.DeviceId, frame.Command, frame.Data.Length);
            return;
        }

        var payload = D2184InventoryDecoder.Decode(frame);

        if (payload.IsTag)
        {
            var tag = payload.Tag!;

            // Dropped here rather than at the server: a stray read of a bag two rooms away
            // should not consume bandwidth or queue capacity at all.
            if (tag.RssiDbm < _options.MinimumRssiDbm)
            {
                _logger.LogTrace("{DeviceId}: ignoring weak read {Rssi} dBm", _options.DeviceId, tag.RssiDbm);
                return;
            }

            TagRead?.Invoke(tag);
            return;
        }

        if (payload.IsSummary)
        {
            _logger.LogTrace("{DeviceId}: round finished on antenna {Ant}, {Total} read(s)",
                _options.DeviceId, payload.Summary!.AntennaId, payload.Summary.TotalRead);
            return;
        }

        // "No tag found" is the normal state of an empty corridor, not a problem.
        if (payload.ErrorCode == 0x41) return;

        _logger.LogDebug("{DeviceId}: {Problem}", _options.DeviceId, payload.Problem);
    }

    private async Task SendAsync(byte command, CancellationToken token, params byte[] data)
    {
        var frame = D2184Frame.Create(_options.Address, command, data);

        if (_tcpStream is not null)
        {
            await _tcpStream.WriteAsync(frame.Raw, token);
            await _tcpStream.FlushAsync(token);
        }
        else if (_serial is { IsOpen: true })
        {
            _serial.Write(frame.Raw, 0, frame.Raw.Length);
        }
    }

    private void SetConnected(bool connected, string? message)
    {
        if (IsConnected == connected) return;

        IsConnected = connected;
        ConnectionChanged?.Invoke(connected, message);
    }

    private void CloseTransport()
    {
        try { _tcpStream?.Dispose(); } catch { /* closing */ }
        try { _tcp?.Dispose(); } catch { /* closing */ }
        try { if (_serial is { IsOpen: true }) _serial.Close(); _serial?.Dispose(); } catch { /* closing */ }

        _tcpStream = null;
        _tcp = null;
        _serial = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sessionCts is not null) await _sessionCts.CancelAsync();
        _sessionCts?.Dispose();
        CloseTransport();
    }
}
