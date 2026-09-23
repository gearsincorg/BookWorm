using System.IO;
using System.Media;

namespace Bookworm.Windows.Services;

/// <summary>
/// Plays a soft, repeating tone while a request is taking a while, so the user has ongoing audible
/// confirmation that Bookworm is still working rather than silently stuck — a multi-tool-call Claude
/// round trip can run 10-20+ seconds. Deliberately synthesizes plain tones rather than using
/// System.Media's built-in Windows system sounds (Asterisk/Exclamation/Hand are literally the classic
/// Windows warning/error sounds — repeating one every few seconds during completely normal operation
/// would read as "something's wrong", the opposite of what this needs to convey), and rather than
/// bundling external audio files, to avoid any licensing/asset dependency for something this small.
/// </summary>
public sealed class ThinkingSoundPlayer
{
    private const int SampleRate = 44100;
    private const int GapMs = 1400;
    private const int CycleTargetMs = 10_000;

    // A few distinct short chime shapes, cycled across successive ~10-second windows so a long wait
    // doesn't sound like the exact same blip on a loop.
    private static readonly (double FrequencyHz, int DurationMs)[][] Patterns =
    [
        [(440.0, 180)],                    // soft single ping
        [(523.0, 140), (659.0, 140)],      // gentle two-note rising chime
        [(659.0, 140), (523.0, 140)],      // gentle two-note falling chime
    ];

    /// <summary>Plays cycling tones until <paramref name="ct"/> is cancelled — cancel this the moment
    /// the real response is ready, don't wait for the current ~10s cycle to finish.</summary>
    public async Task PlayUntilCancelledAsync(CancellationToken ct)
    {
        var index = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var clip = BuildCycleClip(Patterns[index % Patterns.Length]);
                using var player = new SoundPlayer(new MemoryStream(clip));
                player.Play();
                try
                {
                    await Task.Delay(CycleTargetMs, ct);
                }
                finally
                {
                    player.Stop();
                }
                index++;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected — the response arrived before this cycle finished.
        }
    }

    private static byte[] BuildCycleClip((double FrequencyHz, int DurationMs)[] notes)
    {
        var pcm = new List<short>();
        while (pcm.Count * 1000.0 / SampleRate < CycleTargetMs)
        {
            foreach (var (frequencyHz, durationMs) in notes)
            {
                pcm.AddRange(GenerateTone(frequencyHz, durationMs));
            }
            pcm.AddRange(new short[SampleRate * GapMs / 1000]);
        }
        return WrapAsWav([.. pcm]);
    }

    private static short[] GenerateTone(double frequencyHz, int durationMs)
    {
        var sampleCount = (int)(SampleRate * (durationMs / 1000.0));
        var samples = new short[sampleCount];
        const double amplitude = 0.25 * short.MaxValue; // soft volume — this is a background cue, not an alert
        var fadeSamples = Math.Max(1, Math.Min(sampleCount / 4, SampleRate / 20)); // ~50ms fade in/out, avoids clicks
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)SampleRate;
            var envelope = 1.0;
            if (i < fadeSamples)
            {
                envelope = i / (double)fadeSamples;
            }
            else if (i > sampleCount - fadeSamples)
            {
                envelope = (sampleCount - i) / (double)fadeSamples;
            }
            samples[i] = (short)(amplitude * envelope * Math.Sin(2 * Math.PI * frequencyHz * t));
        }
        return samples;
    }

    private static byte[] WrapAsWav(short[] pcmSamples)
    {
        var dataLength = pcmSamples.Length * 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // fmt chunk size
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2); // byte rate (mono, 16-bit)
        writer.Write((short)2); // block align
        writer.Write((short)16); // bits per sample
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        foreach (var sample in pcmSamples)
        {
            writer.Write(sample);
        }

        return stream.ToArray();
    }
}
