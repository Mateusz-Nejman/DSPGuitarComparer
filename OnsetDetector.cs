namespace DSPGuitarComparer;

internal sealed class OnsetDetector
{
    private const int WindowSize = 2048;
    private const int HopSize = 256;

    public List<Onset> Detect(AudioData audio)
    {
        float[] samples = audio.MonoSamples;
        int frames = Math.Max(1, (samples.Length + HopSize - 1) / HopSize);
        var flux = new double[frames];
        var rms = new double[frames];
        var real = new double[WindowSize];
        var imaginary = new double[WindowSize];
        var previous = new double[WindowSize / 2 + 1];
        var current = new double[WindowSize / 2 + 1];
        double windowSum = 0;
        for (int i = 0; i < WindowSize; i++) windowSum += DspMath.Hann(i, WindowSize);

        for (int frame = 0; frame < frames; frame++)
        {
            Array.Clear(real);
            Array.Clear(imaginary);
            int start = frame * HopSize;
            double energy = 0;
            int valid = Math.Min(WindowSize, samples.Length - start);
            if (valid > 0)
            {
                int rmsCount = Math.Min(Math.Max(1, audio.SampleRate / 50), samples.Length - start);
                rms[frame] = DspMath.Rms(samples.AsSpan(start, rmsCount));
            }
            for (int i = 0; i < valid; i++)
            {
                double value = samples[start + i] * DspMath.Hann(i, WindowSize);
                real[i] = value;
                energy += value * value;
            }
            DspMath.Fft(real, imaginary);
            double positiveFlux = 0, previousEnergy = 0;
            for (int bin = 0; bin < current.Length; bin++)
            {
                double magnitude = Math.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]) / windowSum;
                current[bin] = magnitude;
                double delta = magnitude - previous[bin];
                if (delta > 0) positiveFlux += delta;
                previousEnergy += previous[bin];
            }
            flux[frame] = positiveFlux / (previousEnergy + 1e-8);
            (previous, current) = (current, previous);
        }

        var sorted = flux.Where((value, i) => rms[i] >= audio.GateThresholdRms).OrderBy(x => x).ToArray();
        double globalFloor = sorted.Length == 0 ? 0 : sorted[(int)(sorted.Length * 0.5)];
        var onsets = new List<Onset>();
        int adaptiveRadius = Math.Max(2, (int)Math.Round(0.12 * audio.SampleRate / HopSize));
        int minimumGap = Math.Max(1, (int)Math.Ceiling(0.060 * audio.SampleRate / HopSize));
        int lastAccepted = -minimumGap;

        for (int i = 2; i < frames - 2; i++)
        {
            if (rms[i] < audio.GateThresholdRms) continue;
            double sum = 0, sumSq = 0;
            int count = 0;
            for (int j = Math.Max(0, i - adaptiveRadius); j <= Math.Min(frames - 1, i + adaptiveRadius); j++)
            {
                if (Math.Abs(j - i) <= 2) continue;
                sum += flux[j];
                sumSq += flux[j] * flux[j];
                count++;
            }
            if (count == 0) continue;
            double mean = sum / count;
            double deviation = Math.Sqrt(Math.Max(0, sumSq / count - mean * mean));
            double threshold = Math.Max(mean + 1.5 * deviation, Math.Max(globalFloor * 1.6, 0.025));
            if (flux[i] <= threshold || flux[i] < flux[i - 1] || flux[i] < flux[i + 1]) continue;
            if (i - lastAccepted < minimumGap)
            {
                if (onsets.Count > 0 && flux[i] > onsets[^1].Strength)
                {
                    onsets[^1] = new Onset((double)(i * HopSize) / audio.SampleRate, flux[i]);
                    lastAccepted = i;
                }
                continue;
            }
            onsets.Add(new Onset((double)(i * HopSize) / audio.SampleRate, flux[i]));
            lastAccepted = i;
        }
        return onsets;
    }
}
