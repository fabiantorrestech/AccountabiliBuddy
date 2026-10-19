namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Makes the built-in chimes as WAV files in memory (16-bit mono), so no sound files ship with the app. Each note is
/// a sine with two quiet overtones, a quick fade in and a slow fade out, like a soft kitchen-timer ding.
/// </summary>
public static class ChimeSynth
{
    private const int SampleRate = 44100;

    // Loudest point at full volume, as a share of the 16-bit range; volume scales it on a curve that suits the ear.
    private const double FullVolumePeak = 0.7;

    private static readonly (double Frequency, double Level)[] SoftTone = { (1, 1), (2, 0.25), (3, 0.08) };
    private static readonly (double Frequency, double Level)[] BellTone = { (1, 1), (2, 0.5), (2.76, 0.35), (5.4, 0.15) };

    /// <summary>Two notes going down (A5 → E5): focus is up, ease off.</summary>
    public static byte[] SoftFalling(int volume)
    {
        return Render(volume, 1.3, (880, 0, 0.35, SoftTone), (659.25, 0.28, 0.4, SoftTone));
    }

    /// <summary>Three notes going up (C5 → E5 → G5): the break is up, back to it.</summary>
    public static byte[] SoftRising(int volume)
    {
        return Render(volume, 1.2, (523.25, 0, 0.3, SoftTone), (659.25, 0.18, 0.3, SoftTone), (783.99, 0.36, 0.38, SoftTone));
    }

    /// <summary>One bell-like note (C6) with a long ring.</summary>
    public static byte[] Bell(int volume)
    {
        return Render(volume, 2.0, (1046.5, 0, 0.7, BellTone));
    }

    private static byte[] Render(int volume, double seconds, params (double Frequency, double Start, double Decay, (double Frequency, double Level)[] Tone)[] notes)
    {
        int count = (int)(seconds * SampleRate);
        var mix = new double[count];
        foreach ((double frequency, double start, double decay, (double Frequency, double Level)[] tone) in notes)
        {
            int first = (int)(start * SampleRate);
            for (int i = first; i < count; i++)
            {
                double t = (double)(i - first) / SampleRate;
                double envelope = Math.Min(1, t / 0.008) * Math.Exp(-t / decay);
                double sample = 0;
                foreach ((double multiple, double level) in tone)
                {
                    sample += level * Math.Sin(2 * Math.PI * frequency * multiple * t);
                }

                mix[i] += envelope * sample;
            }
        }

        // Fade the last 50 ms to silence so the sound doesn't end with a click.
        int fade = Math.Min(count, SampleRate / 20);
        for (int i = 0; i < fade; i++)
        {
            mix[count - 1 - i] *= (double)i / fade;
        }

        double loudest = mix.Max(Math.Abs);
        double share = Math.Clamp(volume, 0, 100) / 100.0;
        double gain = loudest <= 0 ? 0 : FullVolumePeak * share * share / loudest;
        return ToWave(mix, gain);
    }

    private static byte[] ToWave(double[] samples, double gain)
    {
        int dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                 // format chunk size
        writer.Write((short)1);           // PCM
        writer.Write((short)1);           // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);     // bytes per second
        writer.Write((short)2);           // bytes per sample
        writer.Write((short)16);          // bits per sample
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (double sample in samples)
        {
            writer.Write((short)Math.Clamp(Math.Round(sample * gain * short.MaxValue), short.MinValue, short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
