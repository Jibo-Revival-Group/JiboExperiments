using Jibo.Cloud.Application.Audio;

namespace Jibo.Cloud.Tests.WebSockets;

public sealed class OpusSpeechActivityDetectorTests
{
    [Fact]
    public void HasTrailingSilence_ReturnsFalse_WhenNoPackets()
    {
        Assert.False(OpusSpeechActivityDetector.HasTrailingSilence([], TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void IsSilencePacket_TreatsTinyPacketsAsSilence()
    {
        var silence = new OpusAudioPacket(ByteLength: 3, SampleCount: 960);
        var speech = new OpusAudioPacket(ByteLength: 80, SampleCount: 960);

        Assert.True(OpusSpeechActivityDetector.IsSilencePacket(silence));
        Assert.False(OpusSpeechActivityDetector.IsSilencePacket(speech));
    }

    [Fact]
    public void HasTrailingSilence_DetectsTrailingQuietPackets()
    {
        // Build a minimal Ogg/Opus stream: OpusHead + OpusTags + one speech-sized
        // packet page followed by several tiny silence-sized packet pages.
        var pages = new List<byte[]>
        {
            BuildOggPage(0x02, BuildOpusHead()),
            BuildOggPage(0x00, BuildOpusTags()),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 90)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 4)),
            BuildOggPage(0x04, BuildOpusTocPacket(byteLength: 4))
        };

        // 16 silence packets * 20ms = 320ms trailing silence.
        Assert.True(OpusSpeechActivityDetector.HasTrailingSilence(pages, TimeSpan.FromMilliseconds(300)));
        Assert.False(OpusSpeechActivityDetector.HasTrailingSilence(pages, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void HasTrailingSilence_DetectsRelativeDropAfterSpeech()
    {
        // Dense speech (~4 B/ms) followed by quieter room-noise packets (~1.5 B/ms)
        // that sit above the absolute silence floor but well below peak speech.
        var pages = new List<byte[]>
        {
            BuildOggPage(0x02, BuildOpusHead()),
            BuildOggPage(0x00, BuildOpusTags()),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 90)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 90)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x00, BuildOpusTocPacket(byteLength: 28)),
            BuildOggPage(0x04, BuildOpusTocPacket(byteLength: 28))
        };

        // 16 quiet packets * 20ms = 320ms trailing relative silence.
        Assert.True(OpusSpeechActivityDetector.HasTrailingSilence(pages, TimeSpan.FromMilliseconds(300)));
        Assert.False(OpusSpeechActivityDetector.HasTrailingSilence(pages, TimeSpan.FromMilliseconds(500)));
    }

    private static byte[] BuildOpusHead()
    {
        var packet = new byte[19];
        "OpusHead"u8.CopyTo(packet);
        packet[8] = 1; // version
        packet[9] = 1; // channel count
        return packet;
    }

    private static byte[] BuildOpusTags()
    {
        var packet = new byte[16];
        "OpusTags"u8.CopyTo(packet);
        return packet;
    }

    private static byte[] BuildOpusTocPacket(int byteLength)
    {
        // TOC config 13 (20ms @ 48kHz = 960 samples) with 1 frame.
        var packet = new byte[Math.Max(1, byteLength)];
        packet[0] = 0x68; // configuration 13, stereo bit clear, frame count code 0
        for (var index = 1; index < packet.Length; index += 1)
            packet[index] = 0x11;
        return packet;
    }

    private static byte[] BuildOggPage(byte headerType, byte[] payload)
    {
        var page = new byte[27 + 1 + payload.Length];
        "OggS"u8.CopyTo(page);
        page[4] = 0; // version
        page[5] = headerType;
        page[26] = 1; // one segment
        page[27] = (byte)payload.Length;
        payload.CopyTo(page.AsSpan(28));
        return page;
    }
}
