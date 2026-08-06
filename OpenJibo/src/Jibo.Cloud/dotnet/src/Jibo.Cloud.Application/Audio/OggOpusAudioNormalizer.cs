using System.Buffers.Binary;
using System.Text;

namespace Jibo.Cloud.Application.Audio;

/// <summary>
/// Produces one browser- and decoder-valid Ogg stream from the individually framed
/// Ogg pages sent by the robot over its WebSocket connection.
/// </summary>
public static class OggOpusAudioNormalizer
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Normalize(IReadOnlyList<byte[]> pages)
    {
        if (pages.Count == 0) return [];

        var parsed = pages.SelectMany(ParsePages).ToArray();
        if (parsed.Length == 0) return [];

        // WebSocket messages are transport frames, not necessarily a single logical Ogg stream.
        // Canonicalize them to one stream because browsers reject interleaved stream serials.
        var streamSerial = parsed[0].StreamSerial;
        var preSkip = ReadOpusPreSkip(parsed);
        var decodedSamples = (ulong)preSkip;
        var pendingPacket = new List<byte>();
        var hasDecodedAudio = false;
        var normalized = new List<byte[]>(parsed.Length);

        for (var index = 0; index < parsed.Length; index += 1)
        {
            var parsedPage = parsed[index];
            var output = parsedPage.Content.ToArray();
            foreach (var packet in ReadCompletedPackets(parsedPage, pendingPacket))
            {
                if (IsOpusMetadata(packet)) continue;
                if (!TryGetOpusPacketSampleCount(packet, out var samples)) continue;
                decodedSamples += samples;
                hasDecodedAudio = true;
            }

            var newGranule = hasDecodedAudio ? decodedSamples : 0UL;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(6, 8), newGranule);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(14, 4), streamSerial);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(18, 4), (uint)index);
            output[5] = index == 0
                ? (byte)((output[5] | 0x02) & ~0x04)
                : index == parsed.Length - 1
                    ? (byte)((output[5] | 0x04) & ~0x02)
                    : (byte)(output[5] & ~0x06);

            output.AsSpan(22, 4).Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(22, 4), ComputeCrc(output));
            normalized.Add(output);
        }

        return normalized.SelectMany(static page => page).ToArray();
    }

    private static IEnumerable<ParsedOggPage> ParsePages(byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            if (buffer.Length - offset < 27)
                throw new InvalidOperationException($"Buffered Ogg page is too short ({buffer.Length - offset} bytes remain).");
            if (!buffer.AsSpan(offset, 4).SequenceEqual("OggS"u8))
                throw new InvalidOperationException("Buffered audio frame did not begin with an OggS capture pattern.");

            var pageSegments = buffer[offset + 26];
            if (buffer.Length - offset < 27 + pageSegments)
                throw new InvalidOperationException("Buffered Ogg page segment table was truncated.");

            var payloadLength = 0;
            for (var index = 0; index < pageSegments; index += 1) payloadLength += buffer[offset + 27 + index];
            var pageLength = 27 + pageSegments + payloadLength;
            if (buffer.Length - offset < pageLength)
                throw new InvalidOperationException("Buffered Ogg page payload was truncated.");

            yield return new ParsedOggPage(
                buffer.AsSpan(offset, pageLength).ToArray(),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 14, 4)),
                pageSegments,
                27);
            offset += pageLength;
        }
    }

    private static ushort ReadOpusPreSkip(IEnumerable<ParsedOggPage> pages)
    {
        foreach (var page in pages)
        {
            var payload = page.Content.AsSpan(page.PayloadOffset);
            if (payload.Length >= 12 && payload[..8].SequenceEqual("OpusHead"u8))
                return BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
        }
        return 0;
    }

    private static IEnumerable<byte[]> ReadCompletedPackets(ParsedOggPage page, List<byte> pendingPacket)
    {
        var payloadOffset = page.PayloadOffset;
        for (var index = 0; index < page.PageSegments; index += 1)
        {
            var length = page.Content[27 + index];
            pendingPacket.AddRange(page.Content.AsSpan(payloadOffset, length).ToArray());
            payloadOffset += length;
            if (length == byte.MaxValue) continue;

            yield return pendingPacket.ToArray();
            pendingPacket.Clear();
        }
    }

    private static bool IsOpusMetadata(byte[] packet) =>
        packet.AsSpan().StartsWith("OpusHead"u8) || packet.AsSpan().StartsWith("OpusTags"u8);

    private static bool TryGetOpusPacketSampleCount(byte[] packet, out ulong samples)
    {
        samples = 0;
        if (packet.Length == 0) return false;

        var toc = packet[0];
        var configuration = toc >> 3;
        var frameCountCode = toc & 0x03;
        var frameCount = frameCountCode == 0 ? 1 : frameCountCode is 1 or 2 ? 2 :
            packet.Length > 1 ? packet[1] & 0x3f : 0;
        if (frameCount == 0) return false;

        var samplesPerFrame = configuration < 12
            ? 480 << (configuration & 0x03)
            : configuration < 16
                ? 480 << (configuration & 0x01)
                : 120 << (configuration & 0x03);
        var totalSamples = samplesPerFrame * frameCount;
        if (totalSamples > 5760) return false;

        samples = (ulong)totalSamples;
        return true;
    }

    private static uint ComputeCrc(byte[] buffer) => buffer.Aggregate<byte, uint>(0,
        (current, value) => (current << 8) ^ CrcTable[((current >> 24) ^ value) & 0xff]);

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index += 1)
        {
            var remainder = index << 24;
            for (var bit = 0; bit < 8; bit += 1)
                remainder = (remainder & 0x80000000) != 0 ? (remainder << 1) ^ 0x04c11db7 : remainder << 1;
            table[index] = remainder;
        }
        return table;
    }

    private sealed record ParsedOggPage(byte[] Content, uint StreamSerial, int PageSegments, int PayloadOffset);
}
