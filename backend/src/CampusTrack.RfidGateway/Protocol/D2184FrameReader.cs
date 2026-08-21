namespace CampusTrack.RfidGateway.Protocol;

/// <summary>
/// Reassembles D2184 frames from a raw byte stream.
///
/// Neither TCP nor a serial port preserves message boundaries: one read can deliver half a
/// frame, three frames, or a frame split across two reads. During real-time inventory the
/// reader emits a frame per tag sighting — dozens per second — so this runs constantly and
/// must never lose sync.
///
/// Resynchronisation matters as much as parsing. EPC payloads legitimately contain 0xA0, so
/// a byte that looks like a header may not be one. A candidate frame is only accepted when
/// its checksum verifies; otherwise the scan advances a single byte and tries again, which
/// means a corrupted stream costs at most one frame rather than every frame after it.
/// </summary>
public sealed class D2184FrameReader
{
    /// <summary>
    /// Ceiling on buffered bytes. If the stream never yields a valid frame — wrong baud
    /// rate, wrong device on the port — this stops the buffer growing without bound.
    /// </summary>
    private const int MaxBufferBytes = 16 * 1024;

    private readonly List<byte> _buffer = new(4096);

    /// <summary>Bytes discarded because they could not begin a valid frame. A rising count
    /// means the link is misconfigured or noisy.</summary>
    public long DiscardedBytes { get; private set; }

    public int Buffered => _buffer.Count;

    public void Reset()
    {
        _buffer.Clear();
    }

    /// <summary>
    /// Adds newly received bytes and returns every complete frame now available. Any partial
    /// frame at the end is retained for the next call.
    /// </summary>
    public IReadOnlyList<D2184Frame> Append(ReadOnlySpan<byte> incoming)
    {
        if (incoming.Length > 0) _buffer.AddRange(incoming);

        // Runaway guard: keep the newest bytes, which are the ones most likely to contain a
        // real frame boundary.
        if (_buffer.Count > MaxBufferBytes)
        {
            var excess = _buffer.Count - MaxBufferBytes;
            _buffer.RemoveRange(0, excess);
            DiscardedBytes += excess;
        }

        var frames = new List<D2184Frame>();
        var position = 0;

        while (position < _buffer.Count)
        {
            // Find the next plausible header.
            if (_buffer[position] != D2184Frame.Header)
            {
                position++;
                DiscardedBytes++;
                continue;
            }

            // Need at least the header and the length byte to know how far the frame runs.
            if (position + 1 >= _buffer.Count) break;

            var length = _buffer[position + 1];

            // Len counts everything after itself, so the frame is Len + 2 bytes. A length
            // below 3 cannot even hold address, command and checksum.
            if (length < 3)
            {
                position++;
                DiscardedBytes++;
                continue;
            }

            var frameLength = length + 2;

            // The rest has not arrived yet; stop and wait for more.
            if (position + frameLength > _buffer.Count) break;

            var candidate = CollectionsMarshalSpan(position, frameLength);
            var frame = D2184Frame.Parse(candidate);

            if (frame is null)
            {
                // A 0xA0 inside payload data, or a corrupted frame. Advance one byte so the
                // real header immediately after it is still found.
                position++;
                DiscardedBytes++;
                continue;
            }

            frames.Add(frame);
            position += frameLength;
        }

        if (position > 0) _buffer.RemoveRange(0, position);

        return frames;
    }

    private ReadOnlySpan<byte> CollectionsMarshalSpan(int start, int length) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer).Slice(start, length);
}
