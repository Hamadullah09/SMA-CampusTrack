using CampusTrack.RfidGateway.Protocol;
using Xunit;

namespace CampusTrack.UnitTests.Rfid;

/// <summary>
/// Verifies the D2184 codec against the wire format in the vendor specification.
///
/// Every byte here is constructed by hand from the protocol document rather than by
/// round-tripping our own encoder, so a mistake in the encoder cannot hide behind a matching
/// mistake in the decoder.
/// </summary>
public class D2184FrameTests
{
    [Fact]
    public void ChecksumIsTwosComplementOfTheRunningSum()
    {
        // 0xA0 + 0x03 + 0xFF + 0x72 = 0x214, low byte 0x14; two's complement is 0xEC.
        byte[] bytes = [0xA0, 0x03, 0xFF, 0x72];

        Assert.Equal(0xEC, D2184Frame.Checksum(bytes, 0, bytes.Length));
    }

    [Fact]
    public void CommandWithoutDataProducesAFiveByteFrame()
    {
        var frame = D2184Frame.Create(0xFF, D2184Command.GetFirmwareVersion);

        Assert.Equal(5, frame.Raw.Length);
        Assert.Equal(0xA0, frame.Raw[0]);
        Assert.Equal(0x03, frame.Raw[1]);   // address + cmd + checksum
        Assert.Equal(0xFF, frame.Raw[2]);
        Assert.Equal(0x72, frame.Raw[3]);
        Assert.Equal(0xEC, frame.Raw[4]);
    }

    [Fact]
    public void RealTimeInventoryMatchesTheDocumentedRequest()
    {
        // Specification: Head 0xA0 | Len 0x04 | Address | Cmd 0x89 | Repeat | Check
        var frame = D2184Frame.Create(0x01, D2184Command.RealTimeInventory, 0xFF);

        Assert.Equal(6, frame.Raw.Length);
        Assert.Equal(0x04, frame.Raw[1]);
        Assert.Equal(0x89, frame.Raw[3]);
        Assert.Equal(0xFF, frame.Raw[4]);

        // The frame must validate against its own checksum.
        Assert.NotNull(D2184Frame.Parse(frame.Raw));
    }

    [Fact]
    public void SetOutputPowerCarriesFourAntennaValues()
    {
        var frame = D2184Frame.Create(0xFF, D2184Command.SetOutputPower, 30, 30, 30, 30);

        Assert.Equal(0x07, frame.Raw[1]);   // 4 data + address + cmd + checksum
        Assert.Equal(0x76, frame.Raw[3]);
        Assert.Equal([30, 30, 30, 30], frame.Data);
    }

    [Fact]
    public void ParseRejectsAFrameWithABadChecksum()
    {
        var frame = D2184Frame.Create(0xFF, D2184Command.GetFirmwareVersion);
        var corrupted = frame.Raw.ToArray();
        corrupted[^1] ^= 0xFF;

        Assert.Null(D2184Frame.Parse(corrupted));
    }

    [Fact]
    public void ParseRejectsAFrameWithoutTheHeaderByte()
    {
        Assert.Null(D2184Frame.Parse([0xB0, 0x03, 0xFF, 0x72, 0xEC]));
    }

    [Fact]
    public void RoundTripPreservesAddressCommandAndData()
    {
        var original = D2184Frame.Create(0x05, D2184Command.SetWorkAntenna, 0x02);
        var parsed = D2184Frame.Parse(original.Raw);

        Assert.NotNull(parsed);
        Assert.Equal(0x05, parsed.Address);
        Assert.Equal(D2184Command.SetWorkAntenna, parsed.Command);
        Assert.Equal([0x02], parsed.Data);
    }
}

public class D2184InventoryDecoderTests
{
    /// <summary>
    /// Builds a tag report exactly as the reader sends one:
    /// FreqAnt(1) | PC(2) | EPC(N) | RSSI(1)
    /// </summary>
    private static D2184Frame TagFrame(byte freqAnt, ushort pc, byte[] epc, byte rssi)
    {
        var data = new byte[1 + 2 + epc.Length + 1];
        data[0] = freqAnt;
        data[1] = (byte)(pc >> 8);
        data[2] = (byte)(pc & 0xFF);
        epc.CopyTo(data, 3);
        data[^1] = rssi;

        return D2184Frame.Parse(D2184Frame.Create(0x01, D2184Command.RealTimeInventory, data).Raw)!;
    }

    [Fact]
    public void DecodesA96BitEpcTagReport()
    {
        // A 96-bit EPC is six words, so the PC word's top five bits hold 6 → 0x3000.
        var epc = Convert.FromHexString("E28011606000020C3F1A2B3C");
        var frame = TagFrame(freqAnt: 0b000101_10, pc: 0x3000, epc: epc, rssi: 0x50);

        var payload = D2184InventoryDecoder.Decode(frame);

        Assert.True(payload.IsTag);
        Assert.Equal("E28011606000020C3F1A2B3C", payload.Tag!.Epc);

        // Low two bits are the antenna (0b10 = 2), presented to humans as antenna 3.
        Assert.Equal(3, payload.Tag.AntennaNumber);
        Assert.Equal(0b000101, payload.Tag.FrequencyParameter);

        // 0x50 = 80 → 80 - 129 = -49 dBm, matching the vendor's RSSI table.
        Assert.Equal(-49, payload.Tag.RssiDbm);
        Assert.Equal(0x50, payload.Tag.RawRssi);
    }

    [Theory]
    [InlineData(0b000000_00, 1)]
    [InlineData(0b000000_01, 2)]
    [InlineData(0b000000_10, 3)]
    [InlineData(0b000000_11, 4)]
    public void AntennaIdIsTakenFromTheLowTwoBits(byte freqAnt, int expected)
    {
        var frame = TagFrame(freqAnt, 0x3000, Convert.FromHexString("E28011606000020C3F1A2B3C"), 0x50);
        var payload = D2184InventoryDecoder.Decode(frame);

        Assert.Equal(expected, payload.Tag!.AntennaNumber);
    }

    [Theory]
    [InlineData(98, -31)]   // strongest documented value
    [InlineData(80, -49)]
    [InlineData(64, -65)]
    [InlineData(31, -98)]   // weakest documented value
    public void RssiConversionMatchesTheVendorTable(byte raw, int expectedDbm)
    {
        Assert.Equal(expectedDbm, D2184InventoryDecoder.ToDbm(raw));
    }

    [Fact]
    public void DecodesALongerEpc()
    {
        // A 128-bit EPC is eight words → PC 0x4000.
        var epc = Convert.FromHexString("E28011606000020C3F1A2B3C11223344");
        var frame = TagFrame(0x00, 0x4000, epc, 0x55);

        var payload = D2184InventoryDecoder.Decode(frame);

        Assert.True(payload.IsTag);
        Assert.Equal("E28011606000020C3F1A2B3C11223344", payload.Tag!.Epc);
    }

    [Fact]
    public void DecodesTheRoundSummary()
    {
        // AntID(1) | ReadRate(2) | TotalRead(4) — seven data bytes.
        byte[] data = [0x02, 0x01, 0x2C, 0x00, 0x00, 0x03, 0xE8];
        var frame = D2184Frame.Parse(
            D2184Frame.Create(0x01, D2184Command.RealTimeInventory, data).Raw)!;

        var payload = D2184InventoryDecoder.Decode(frame);

        Assert.True(payload.IsSummary);
        Assert.Equal(2, payload.Summary!.AntennaId);
        Assert.Equal(300, payload.Summary.ReadRate);
        Assert.Equal(1000, payload.Summary.TotalRead);
    }

    [Fact]
    public void DecodesAnErrorCode()
    {
        var frame = D2184Frame.Parse(
            D2184Frame.Create(0x01, D2184Command.RealTimeInventory, 0x41).Raw)!;

        var payload = D2184InventoryDecoder.Decode(frame);

        Assert.True(payload.IsError);
        Assert.Equal((byte)0x41, payload.ErrorCode!.Value);
        Assert.Contains("No tag", payload.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SevenDataBytesAreNeverMistakenForATagReport()
    {
        // A tag report is always even (1 + 2 + even EPC + 1), so a seven-byte payload can
        // only be the round summary. This is the one genuine ambiguity in the protocol.
        byte[] data = [0x00, 0x00, 0x64, 0x00, 0x00, 0x00, 0x0A];
        var frame = D2184Frame.Parse(
            D2184Frame.Create(0x01, D2184Command.RealTimeInventory, data).Raw)!;

        var payload = D2184InventoryDecoder.Decode(frame);

        Assert.True(payload.IsSummary);
        Assert.False(payload.IsTag);
    }

    [Fact]
    public void ATruncatedEpcIsRejectedRatherThanTruncatedSilently()
    {
        // The PC word claims six words (12 bytes) but only 8 are present. Accepting this
        // would yield a corrupt EPC that resolves to the wrong student or to nobody.
        var data = new byte[1 + 2 + 8 + 1];
        data[0] = 0x00;
        data[1] = 0x30;   // PC claims 6 words
        data[2] = 0x00;
        data[^1] = 0x50;

        var frame = D2184Frame.Parse(
            D2184Frame.Create(0x01, D2184Command.RealTimeInventory, data).Raw)!;

        var payload = D2184InventoryDecoder.Decode(frame);

        Assert.True(payload.IsError);
        Assert.False(payload.IsTag);
    }
}

public class D2184FrameReaderTests
{
    private static byte[] TagFrameBytes(string epcHex, byte antenna, byte rssi)
    {
        var epc = Convert.FromHexString(epcHex);
        var words = (ushort)((epc.Length / 2) << 11);

        var data = new byte[1 + 2 + epc.Length + 1];
        data[0] = antenna;
        data[1] = (byte)(words >> 8);
        data[2] = (byte)(words & 0xFF);
        epc.CopyTo(data, 3);
        data[^1] = rssi;

        return D2184Frame.Create(0x01, D2184Command.RealTimeInventory, data).Raw;
    }

    [Fact]
    public void ExtractsASingleCompleteFrame()
    {
        var reader = new D2184FrameReader();
        var frames = reader.Append(TagFrameBytes("E28011606000020C3F1A2B3C", 0, 0x50));

        Assert.Single(frames);
        Assert.Equal(0, reader.Buffered);
    }

    [Fact]
    public void ExtractsSeveralFramesDeliveredInOneRead()
    {
        // At a busy gate the socket routinely delivers several tag reports together.
        var reader = new D2184FrameReader();

        var stream = TagFrameBytes("E28011606000020C3F1A2B3C", 0, 0x50)
            .Concat(TagFrameBytes("E28011606000020C3F1A2B3D", 1, 0x4E))
            .Concat(TagFrameBytes("E28011606000020C3F1A2B3E", 2, 0x4C))
            .ToArray();

        var frames = reader.Append(stream);

        Assert.Equal(3, frames.Count);
    }

    [Fact]
    public void ReassemblesAFrameSplitAcrossReads()
    {
        var reader = new D2184FrameReader();
        var complete = TagFrameBytes("E28011606000020C3F1A2B3C", 0, 0x50);

        // Arrives in three pieces, as a real socket delivers it.
        Assert.Empty(reader.Append(complete.AsSpan(0, 4)));
        Assert.Empty(reader.Append(complete.AsSpan(4, 6)));

        var frames = reader.Append(complete.AsSpan(10));

        Assert.Single(frames);
        var payload = D2184InventoryDecoder.Decode(frames[0]);
        Assert.Equal("E28011606000020C3F1A2B3C", payload.Tag!.Epc);
    }

    [Fact]
    public void ResynchronisesAfterLeadingGarbage()
    {
        // A reader powered on mid-stream, or a line that dropped bytes.
        var reader = new D2184FrameReader();

        var stream = new byte[] { 0x11, 0x22, 0x33 }
            .Concat(TagFrameBytes("E28011606000020C3F1A2B3C", 0, 0x50))
            .ToArray();

        var frames = reader.Append(stream);

        Assert.Single(frames);
        Assert.Equal(3, reader.DiscardedBytes);
    }

    [Fact]
    public void AnEpcContainingTheHeaderByteDoesNotBreakFraming()
    {
        // 0xA0 appears inside the EPC. A naive scanner would treat it as a frame start and
        // lose sync for the rest of the session; the checksum check is what prevents that.
        var reader = new D2184FrameReader();

        var stream = TagFrameBytes("E280A0A0A0A0020C3F1A2B3C", 0, 0x50)
            .Concat(TagFrameBytes("E28011606000020C3F1A2B3D", 1, 0x4E))
            .ToArray();

        var frames = reader.Append(stream);

        Assert.Equal(2, frames.Count);
        Assert.Equal("E280A0A0A0A0020C3F1A2B3C",
            D2184InventoryDecoder.Decode(frames[0]).Tag!.Epc);
        Assert.Equal("E28011606000020C3F1A2B3D",
            D2184InventoryDecoder.Decode(frames[1]).Tag!.Epc);
    }

    [Fact]
    public void PartialTrailingFrameIsHeldForTheNextRead()
    {
        var reader = new D2184FrameReader();
        var complete = TagFrameBytes("E28011606000020C3F1A2B3C", 0, 0x50);

        var stream = complete.Concat(complete.Take(6)).ToArray();
        var frames = reader.Append(stream);

        Assert.Single(frames);
        Assert.Equal(6, reader.Buffered);
    }

    [Fact]
    public void SustainedStreamIsDecodedWithoutLoss()
    {
        // Approximates a minute of a busy gate: 3,000 tag reports arriving in ragged chunks.
        var reader = new D2184FrameReader();
        var stream = new List<byte>();

        for (var i = 0; i < 3000; i++)
        {
            stream.AddRange(TagFrameBytes($"E280116060000{i:X7}", (byte)(i % 4), 0x50));
        }

        var all = stream.ToArray();
        var decoded = 0;
        var offset = 0;
        var chunk = 1;

        while (offset < all.Length)
        {
            var size = Math.Min(chunk, all.Length - offset);
            decoded += reader.Append(all.AsSpan(offset, size)).Count;
            offset += size;

            // Vary the chunk size so frames land on every possible boundary.
            chunk = chunk % 97 + 1;
        }

        Assert.Equal(3000, decoded);
        Assert.Equal(0, reader.DiscardedBytes);
    }
}
