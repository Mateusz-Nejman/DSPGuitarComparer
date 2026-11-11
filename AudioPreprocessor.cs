using NAudio.Dsp;
using NAudio.Wave;

namespace DSPGuitarComparer;

internal sealed class AudioPreprocessor
{
    private const int PitchRate = 12_000;
    private const double AbsoluteGateRms = 0.00316227766; // -50 dBFS

    public AudioData Load(string path)
    {
        using var reader = new AudioFileReader(path);
        int sampleRate = reader.WaveFormat.SampleRate;
        int channels = reader.WaveFormat.Channels;
        if (sampleRate < 5_000 || channels < 1)
            throw new InvalidDataException($"Nieobsługiwany format WAV: {sampleRate} Hz, {channels} kanałów.");

        float[] interleaved = new float[Math.Max(4096, sampleRate * channels)];
        var mono = new List<float>(sampleRate * 60);
        int read;
        while ((read = reader.Read(interleaved, 0, interleaved.Length)) > 0)
        {
            int frames = read / channels;
            for (int frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                int offset = frame * channels;
                for (int channel = 0; channel < channels; channel++) sum += interleaved[offset + channel];
                mono.Add((float)(sum / channels));
            }
        }
        if (mono.Count == 0) throw new InvalidDataException("Plik audio nie zawiera próbek.");

        double mean = mono.Average(x => (double)x);
        var samples = mono.ToArray();
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)(samples[i] - mean);

        float highPassCutoff = Math.Min(70f, sampleRate * 0.04f);
        float lowPassCutoff = Math.Min(2500f, sampleRate * 0.45f);
        var highPass = BiQuadFilter.HighPassFilter(sampleRate, highPassCutoff, 0.70710678f);
        var lowPass = BiQuadFilter.LowPassFilter(sampleRate, lowPassCutoff, 0.70710678f);
        for (int i = 0; i < samples.Length; i++)
            samples[i] = lowPass.Transform(highPass.Transform(samples[i]));

        double noiseRms = EstimateNoiseRms(samples, sampleRate);
        double gate = Math.Max(AbsoluteGateRms, noiseRms * 2.5);
        float[] pitchSamples = Resample(samples, sampleRate, Math.Min(sampleRate, PitchRate));
        int pitchSampleRate = Math.Min(sampleRate, PitchRate);
        return new AudioData
        {
            MonoSamples = samples,
            PitchSamples = pitchSamples,
            SampleRate = sampleRate,
            PitchSampleRate = pitchSampleRate,
            Channels = channels,
            NoiseRms = noiseRms,
            GateThresholdRms = gate
        };
    }

    private static double EstimateNoiseRms(float[] samples, int sampleRate)
    {
        int frameSize = Math.Max(1, sampleRate / 50); // 20 ms
        var levels = new List<double>((samples.Length + frameSize - 1) / frameSize);
        for (int start = 0; start < samples.Length; start += frameSize)
            levels.Add(DspMath.Rms(samples.AsSpan(start, Math.Min(frameSize, samples.Length - start))));
        levels.Sort();
        if (levels.Count == 0) return 0;
        // The quietest percentile tracks the background floor without mistaking quieter notes for noise.
        int index = (int)Math.Floor((levels.Count - 1) * 0.01);
        return levels[index];
    }

    private static float[] Resample(float[] source, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate) return source;
        int count = Math.Max(1, (int)Math.Floor((double)(source.Length - 1) * targetRate / sourceRate) + 1);
        var result = new float[count];
        double ratio = (double)sourceRate / targetRate;
        for (int i = 0; i < count; i++)
        {
            double position = i * ratio;
            int left = (int)position;
            int right = Math.Min(left + 1, source.Length - 1);
            double fraction = position - left;
            result[i] = (float)(source[left] * (1 - fraction) + source[right] * fraction);
        }
        return result;
    }
}
