namespace CampusTrack.RfidGateway.Protocol;

/// <summary>
/// Command codes for the D2184 (R2000-based) UHF reader, taken from the vendor's
/// "UHF RFID Reader Serial Interface Protocol V3.1" and its reference SDK.
/// </summary>
public static class D2184Command
{
    // ---- reader configuration -------------------------------------------------
    public const byte Reset = 0x70;
    public const byte SetUartBaudrate = 0x71;
    public const byte GetFirmwareVersion = 0x72;
    public const byte SetReaderAddress = 0x73;
    public const byte SetWorkAntenna = 0x74;
    public const byte GetWorkAntenna = 0x75;
    public const byte SetOutputPower = 0x76;
    public const byte GetOutputPower = 0x77;
    public const byte SetFrequencyRegion = 0x78;
    public const byte GetFrequencyRegion = 0x79;
    public const byte SetBeeperMode = 0x7A;
    public const byte GetReaderTemperature = 0x7B;

    // ---- GPIO and antenna detection -------------------------------------------
    public const byte ReadGpioValue = 0x60;
    public const byte WriteGpioValue = 0x61;
    public const byte SetAntDetector = 0x62;
    public const byte GetAntDetector = 0x63;
    public const byte SetRadioProfile = 0x69;
    public const byte GetRadioProfile = 0x6A;
    public const byte GetReaderIdentifier = 0x68;
    public const byte SetReaderIdentifier = 0x67;

    // ---- EPC Gen2 --------------------------------------------------------------
    public const byte Inventory = 0x80;
    public const byte ReadTag = 0x81;
    public const byte WriteTag = 0x82;

    /// <summary>
    /// Real-time inventory. Tags are uploaded as they are seen rather than buffered,
    /// which is what makes doorway detection responsive.
    /// </summary>
    public const byte RealTimeInventory = 0x89;

    /// <summary>Polls several antennas in one command — used on multi-lane gates.</summary>
    public const byte FastSwitchAntInventory = 0x8A;
    public const byte CustomizedSessionTargetInventory = 0x8B;

    // ---- buffered inventory -----------------------------------------------------
    public const byte GetInventoryBuffer = 0x90;
    public const byte GetAndResetInventoryBuffer = 0x91;
    public const byte GetInventoryBufferTagCount = 0x92;
    public const byte ResetInventoryBuffer = 0x93;
}

/// <summary>
/// One D2184 frame.
///
/// Wire format, from the protocol specification:
/// <code>
///   0xA0 | Len | Address | Cmd | Data... | Checksum
/// </code>
/// <list type="bullet">
///   <item><c>Len</c> counts the bytes that follow it, including the checksum, so the
///   complete frame on the wire is <c>Len + 2</c> bytes.</item>
///   <item><c>Checksum</c> is the two's complement of the sum of every preceding byte:
///   <c>((~sum) + 1) &amp; 0xFF</c>.</item>
/// </list>
/// </summary>
public sealed class D2184Frame
{
    public const byte Header = 0xA0;

    private D2184Frame(byte address, byte command, byte[] data, byte[] raw)
    {
        Address = address;
        Command = command;
        Data = data;
        Raw = raw;
    }

    public byte Address { get; }
    public byte Command { get; }
    public byte[] Data { get; }
    public byte[] Raw { get; }

    /// <summary>Builds a frame to send to the reader.</summary>
    public static D2184Frame Create(byte address, byte command, params byte[] data)
    {
        data ??= [];

        // Len covers Address + Cmd + Data + Checksum.
        var length = (byte)(data.Length + 3);
        var raw = new byte[data.Length + 5];

        raw[0] = Header;
        raw[1] = length;
        raw[2] = address;
        raw[3] = command;
        data.CopyTo(raw, 4);
        raw[^1] = Checksum(raw, 0, raw.Length - 1);

        return new D2184Frame(address, command, data, raw);
    }

    /// <summary>
    /// Parses one complete frame. Returns null when the checksum does not match, which is
    /// the normal outcome for a false 0xA0 inside tag data rather than an error worth
    /// escalating.
    /// </summary>
    public static D2184Frame? Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 5 || frame[0] != Header) return null;

        var expected = Checksum(frame, 0, frame.Length - 1);
        if (expected != frame[^1]) return null;

        var data = frame.Length > 5 ? frame[4..^1].ToArray() : [];
        return new D2184Frame(frame[2], frame[3], data, frame.ToArray());
    }

    /// <summary>Two's complement of the running sum, per the protocol specification.</summary>
    public static byte Checksum(ReadOnlySpan<byte> buffer, int start, int length)
    {
        byte sum = 0;
        for (var i = start; i < start + length; i++) sum += buffer[i];
        return (byte)(((~sum) + 1) & 0xFF);
    }
}

/// <summary>A tag seen by the reader during real-time inventory.</summary>
public sealed record D2184TagReport
{
    /// <summary>EPC as upper-case hex, exactly as the tag reports it.</summary>
    public required string Epc { get; init; }

    /// <summary>Antenna port that saw the tag, numbered 1-4 for humans (the wire uses 0-3).</summary>
    public required int AntennaNumber { get; init; }

    /// <summary>Signal strength in dBm, converted from the reader's raw parameter.</summary>
    public required int RssiDbm { get; init; }

    /// <summary>Raw RSSI parameter, kept for diagnostics.</summary>
    public required byte RawRssi { get; init; }

    /// <summary>The frequency channel parameter the tag was read on.</summary>
    public required int FrequencyParameter { get; init; }

    /// <summary>Protocol Control word. Its top five bits encode the EPC length in words.</summary>
    public required ushort ProtocolControl { get; init; }

    public DateTime ObservedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Summary the reader sends when an inventory round finishes.</summary>
public sealed record D2184RoundSummary(int AntennaId, int ReadRate, long TotalRead);

/// <summary>
/// Decodes the payload of a real-time inventory (0x89) frame.
///
/// The same command code carries three different shapes, and the protocol does not tag
/// them. They are told apart by payload length, which is unambiguous:
/// <list type="bullet">
///   <item><b>1 byte</b> — an error code.</item>
///   <item><b>7 bytes</b> — a round summary (AntId + ReadRate + TotalRead).</item>
///   <item><b>even, 6 or more</b> — a tag report. FreqAnt(1) + PC(2) + EPC(even) + RSSI(1)
///   is always even, so it can never be confused with the 7-byte summary.</item>
/// </list>
/// </summary>
public static class D2184InventoryDecoder
{
    /// <summary>
    /// Converts the reader's RSSI parameter to dBm. The vendor's table is exactly linear
    /// (98 → -31 dBm, 31 → -98 dBm), so the lookup reduces to a subtraction.
    /// </summary>
    public const int RssiOffset = 129;

    public static int ToDbm(byte rawRssi) => rawRssi - RssiOffset;

    public static InventoryPayload Decode(D2184Frame frame)
    {
        var data = frame.Data;

        if (data.Length == 1)
            return InventoryPayload.FromError(data[0]);

        if (data.Length == 7)
        {
            // AntID(1) | ReadRate(2, big-endian) | TotalRead(4, big-endian)
            var antennaId = data[0];
            var readRate = (data[1] << 8) | data[2];
            var totalRead = ((long)data[3] << 24) | ((long)data[4] << 16) | ((long)data[5] << 8) | data[6];

            return InventoryPayload.FromSummary(new D2184RoundSummary(antennaId, readRate, totalRead));
        }

        if (data.Length >= 6 && data.Length % 2 == 0)
        {
            var freqAnt = data[0];

            // Low two bits are the antenna id (0-3); the upper six are the frequency channel.
            var antenna = (freqAnt & 0x03) + 1;
            var frequency = freqAnt >> 2;

            var pc = (ushort)((data[1] << 8) | data[2]);

            // The PC word's top five bits give the EPC length in 16-bit words. Trusting it
            // rather than "everything between PC and RSSI" means a truncated frame is
            // rejected instead of yielding a corrupt EPC.
            var epcWords = (pc >> 11) & 0x1F;
            var epcLength = epcWords * 2;

            if (epcLength <= 0 || 4 + epcLength != data.Length)
                return InventoryPayload.FromError(null, "EPC length does not match the PC word.");

            var epc = Convert.ToHexString(data.AsSpan(3, epcLength));
            var rssi = data[^1];

            return InventoryPayload.FromTag(new D2184TagReport
            {
                Epc = epc,
                AntennaNumber = antenna,
                RawRssi = rssi,
                RssiDbm = ToDbm(rssi),
                FrequencyParameter = frequency,
                ProtocolControl = pc,
            });
        }

        return InventoryPayload.FromError(null, $"Unrecognised inventory payload of {data.Length} byte(s).");
    }
}

/// <summary>The three things a 0x89 frame can mean.</summary>
public sealed record InventoryPayload
{
    public D2184TagReport? Tag { get; private init; }
    public D2184RoundSummary? Summary { get; private init; }
    public byte? ErrorCode { get; private init; }
    public string? Problem { get; private init; }

    public bool IsTag => Tag is not null;
    public bool IsSummary => Summary is not null;
    public bool IsError => ErrorCode is not null || Problem is not null;

    public static InventoryPayload FromTag(D2184TagReport tag) => new() { Tag = tag };
    public static InventoryPayload FromSummary(D2184RoundSummary summary) => new() { Summary = summary };

    public static InventoryPayload FromError(byte? code, string? problem = null) =>
        new() { ErrorCode = code, Problem = problem ?? (code is null ? null : DescribeError(code.Value)) };

    /// <summary>Turns a reader error byte into something an installer can act on.</summary>
    public static string DescribeError(byte code) => code switch
    {
        0x10 => "Command succeeded.",
        0x11 => "Command failed.",
        0x20 => "The MCU reset itself.",
        0x21 => "The CW signal could not be turned on.",
        0x22 => "The antenna is missing or not detected.",
        0x23 => "Write failed.",
        0x31 => "The reader is over temperature.",
        0x32 => "The output power is out of range.",
        0x41 => "No tag was found in the field.",
        0x42 => "Tag inventory returned an error.",
        0xFF => "Unspecified reader error.",
        _ => $"Reader error 0x{code:X2}.",
    };
}
