namespace Jibo.Cloud.Application.Audio;

/// <summary>
/// Detects end-of-speech from Opus VBR packet sizes in audio time.
/// Silence/comfort-noise packets collapse to tiny payloads; speech stays dense.
/// Uses audio-clock duration so network jitter cannot fake or hide silence.
/// Requires at least one prior speech packet so all-quiet / garbage buffers do not
/// spuriously finalize mid-utterance.
/// </summary>
public static class OpusSpeechActivityDetector
{
    /// <summary>Opus sample rate used for TOC frame duration calculations.</summary>
    public const int OpusSampleRate = 48_000;

    /// <summary>
    /// Packets at or below this many bytes-per-millisecond of audio are treated as silence.
    /// ~1.2 B/ms ≈ 9.6 kbps — below typical speech Opus bitrates, above empty DTX.
    /// Tuned for robot comfort-noise / low-energy trailing frames that were missing
    /// the older 0.8 B/ms cutoff and forced the 1.8s continuous-probe path (~3-4s).
    /// </summary>
    public const double SilenceBytesPerMillisecond = 1.2;

    /// <summary>Absolute packet size below which a packet is always silence/CN.</summary>
    public const int AbsoluteSilencePacketBytes = 24;

    /// <summary>
    /// Trailing packets below this fraction of peak speech density count as quiet even
    /// when they exceed the absolute silence floor (room-noise after the user stops).
    /// </summary>
    public const double RelativeSilenceFractionOfPeak = 0.35;

    public static bool HasTrailingSilence(
        IReadOnlyList<byte[]> pages,
        TimeSpan requiredSilence,
        double silenceBytesPerMillisecond = SilenceBytesPerMillisecond)
    {
        if (requiredSilence <= TimeSpan.Zero) return false;

        var packets = OggOpusAudioNormalizer.EnumerateAudioPackets(pages).ToArray();
        if (packets.Length == 0) return false;

        var peakSpeechBytesPerMs = MeasurePeakSpeechDensity(packets, silenceBytesPerMillisecond);
        var lastSpeechIndex = -1;
        for (var index = 0; index < packets.Length; index += 1)
        {
            if (!IsSilencePacket(packets[index], silenceBytesPerMillisecond, peakSpeechBytesPerMs))
                lastSpeechIndex = index;
        }

        // Never observed speech — do not treat an all-quiet buffer as end-of-speech.
        if (lastSpeechIndex < 0) return false;

        var requiredSamples = (ulong)Math.Ceiling(requiredSilence.TotalSeconds * OpusSampleRate);
        ulong trailingSilenceSamples = 0;
        for (var index = lastSpeechIndex + 1; index < packets.Length; index += 1)
        {
            if (!IsSilencePacket(packets[index], silenceBytesPerMillisecond, peakSpeechBytesPerMs))
                return false;

            trailingSilenceSamples += packets[index].SampleCount;
        }

        return trailingSilenceSamples >= requiredSamples;
    }

    public static TimeSpan MeasureTrailingSilence(
        IReadOnlyList<byte[]> pages,
        double silenceBytesPerMillisecond = SilenceBytesPerMillisecond)
    {
        var packets = OggOpusAudioNormalizer.EnumerateAudioPackets(pages).ToArray();
        if (packets.Length == 0) return TimeSpan.Zero;

        var peakSpeechBytesPerMs = MeasurePeakSpeechDensity(packets, silenceBytesPerMillisecond);
        var lastSpeechIndex = -1;
        for (var index = 0; index < packets.Length; index += 1)
        {
            if (!IsSilencePacket(packets[index], silenceBytesPerMillisecond, peakSpeechBytesPerMs))
                lastSpeechIndex = index;
        }

        if (lastSpeechIndex < 0) return TimeSpan.Zero;

        ulong trailingSilenceSamples = 0;
        for (var index = lastSpeechIndex + 1; index < packets.Length; index += 1)
        {
            if (!IsSilencePacket(packets[index], silenceBytesPerMillisecond, peakSpeechBytesPerMs))
                break;

            trailingSilenceSamples += packets[index].SampleCount;
        }

        return TimeSpan.FromSeconds(trailingSilenceSamples / (double)OpusSampleRate);
    }

    public static bool IsSilencePacket(
        OpusAudioPacket packet,
        double silenceBytesPerMillisecond = SilenceBytesPerMillisecond,
        double peakSpeechBytesPerMs = 0)
    {
        if (packet.SampleCount == 0) return true;
        if (packet.ByteLength <= AbsoluteSilencePacketBytes) return true;

        var density = MeasureBytesPerMillisecond(packet);
        if (density <= silenceBytesPerMillisecond) return true;

        // Relative drop vs peak speech: quiet room noise after the user stops talking.
        if (peakSpeechBytesPerMs > silenceBytesPerMillisecond &&
            density <= peakSpeechBytesPerMs * RelativeSilenceFractionOfPeak)
            return true;

        return false;
    }

    private static double MeasurePeakSpeechDensity(
        IReadOnlyList<OpusAudioPacket> packets,
        double silenceBytesPerMillisecond)
    {
        var peak = 0.0;
        for (var index = 0; index < packets.Count; index += 1)
        {
            var packet = packets[index];
            if (packet.SampleCount == 0 || packet.ByteLength <= AbsoluteSilencePacketBytes)
                continue;

            var density = MeasureBytesPerMillisecond(packet);
            // Ignore absolute-floor quiet packets when estimating peak speech energy.
            if (density <= silenceBytesPerMillisecond) continue;
            if (density > peak) peak = density;
        }

        return peak;
    }

    private static double MeasureBytesPerMillisecond(OpusAudioPacket packet)
    {
        if (packet.SampleCount == 0) return 0;
        var durationMs = packet.SampleCount * 1000.0 / OpusSampleRate;
        return durationMs <= 0 ? 0 : packet.ByteLength / durationMs;
    }
}
