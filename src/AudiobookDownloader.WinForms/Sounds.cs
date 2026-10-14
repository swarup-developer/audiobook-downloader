using AudiobookDownloader.Core;
using System.Media;

namespace AudiobookDownloader.WinForms;

/// <summary>
/// The progress beeps: a short beep for each percent a download moves on, pitched the
/// way NVDA beeps for a progress bar. (A finished book is marked by Windows' own
/// notification sound, not by a sound of the app's.)
/// </summary>
internal static class Sounds
{
    private const int SampleRate = 44100;

    private static readonly object Gate = new();
    private static readonly Dictionary<int, byte[]> BeepCache = [];

    /// <summary>Kept alive while it plays: Windows reads the sound from its memory.</summary>
    private static SoundPlayer? _player;

    /// <summary>
    /// NVDA's pitch for a progress bar: 110 Hz at 0 percent, one octave higher every
    /// 25 percent, 1760 Hz at 100.
    /// </summary>
    public static int ProgressBeepFrequency(int percent) =>
        (int)Math.Round(110 * Math.Pow(2, Math.Clamp(percent, 0, 100) / 25.0));

    /// <summary>The 40-millisecond beep NVDA plays for a progress bar at this percent.</summary>
    public static void ProgressBeep(int percent)
    {
        byte[] wave;
        lock (Gate)
        {
            var frequency = ProgressBeepFrequency(percent);
            if (!BeepCache.TryGetValue(frequency, out wave!))
                BeepCache[frequency] = wave = BeepWave(frequency);
        }
        Play(wave);
    }

    private static void Play(byte[] wave)
    {
        try
        {
            lock (Gate)
            {
                _player?.Stop();
                _player?.Dispose();
                _player = new SoundPlayer(new MemoryStream(wave, writable: false));
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            // No sound device, or one that is busy: the app works the same without sound.
            Log.Warn($"Could not play a sound: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // the sounds themselves
    // ------------------------------------------------------------------

    internal static byte[] BeepWave(int frequency)
    {
        var samples = new float[SampleRate * 40 / 1000];
        var fade = SampleRate * 4 / 1000;
        for (var i = 0; i < samples.Length; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, samples.Length - 1 - i) / (double)fade);
            samples[i] = (float)(0.35 * envelope * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
        }
        return ToWave(samples);
    }

    /// <summary>16-bit mono PCM in a WAV container, as Windows plays it.</summary>
    private static byte[] ToWave(float[] samples)
    {
        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);              // PCM
        w.Write((short)1);              // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);        // bytes per second
        w.Write((short)2);              // bytes per sample
        w.Write((short)16);             // bits per sample
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (var s in samples)
            w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        w.Flush();
        return stream.ToArray();
    }
}
