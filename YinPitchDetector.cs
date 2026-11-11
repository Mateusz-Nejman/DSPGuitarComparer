namespace DSPGuitarComparer;

internal sealed class YinPitchDetector
{
    private readonly int _windowSize;
    private readonly double _threshold;

    public YinPitchDetector(int windowSize = 1024, double threshold = 0.14)
    {
        if (windowSize < 256) throw new ArgumentOutOfRangeException(nameof(windowSize));
        _windowSize = windowSize;
        _threshold = threshold;
    }

    public PitchEstimate AnalyzeAtTime(float[] samples, int sampleRate, double startTimeSeconds, double minimumRms = 0)
    {
        int start = (int)Math.Round(startTimeSeconds * sampleRate);
        return Analyze(samples, sampleRate, start, minimumRms);
    }

    public PitchEstimate Analyze(float[] samples, int sampleRate, int start, double minimumRms = 0)
    {
        int length = Math.Min(_windowSize, samples.Length - Math.Max(start, 0));
        if (start < 0 || length < _windowSize * 0.75 || sampleRate <= 0) return new PitchEstimate(0, 0, 0);

        double mean = 0;
        for (int i = 0; i < length; i++) mean += samples[start + i];
        mean /= length;
        double rmsSum = 0;
        var frame = new double[length];
        for (int i = 0; i < length; i++)
        {
            double value = samples[start + i] - mean;
            rmsSum += value * value;
            frame[i] = value * DspMath.Hann(i, length);
        }
        double rms = Math.Sqrt(rmsSum / length);
        if (rms < minimumRms) return new PitchEstimate(0, 0, rms);

        int minTau = Math.Max(2, (int)Math.Floor(sampleRate / 1200.0));
        int maxTau = Math.Min(length / 5, (int)Math.Floor(sampleRate / 70.0));
        if (maxTau <= minTau + 1) return new PitchEstimate(0, 0, rms);
        var difference = new double[maxTau + 1];
        var cmndf = new double[maxTau + 1];
        for (int tau = 1; tau <= maxTau; tau++)
        {
            double sum = 0;
            int count = length - tau;
            for (int i = 0; i < count; i++)
            {
                double delta = frame[i] - frame[i + tau];
                sum += delta * delta;
            }
            difference[tau] = sum;
        }
        cmndf[0] = 1;
        double cumulative = 0;
        for (int tau = 1; tau <= maxTau; tau++)
        {
            cumulative += difference[tau];
            cmndf[tau] = cumulative <= 1e-24 ? 1 : difference[tau] * tau / cumulative;
        }

        int selected = -1;
        for (int tau = minTau; tau <= maxTau; tau++)
        {
            if (cmndf[tau] < _threshold)
            {
                while (tau < maxTau && cmndf[tau + 1] < cmndf[tau]) tau++;
                selected = tau;
                break;
            }
        }
        if (selected < 0)
        {
            double best = double.MaxValue;
            for (int tau = minTau; tau <= maxTau; tau++)
                if (cmndf[tau] < best) { best = cmndf[tau]; selected = tau; }
            if (selected < 0 || cmndf[selected] > 0.45) return new PitchEstimate(0, 0, rms);
        }

        double refined = selected;
        if (selected > minTau && selected < maxTau)
        {
            double left = cmndf[selected - 1], center = cmndf[selected], right = cmndf[selected + 1];
            double denominator = left - 2 * center + right;
            if (Math.Abs(denominator) > 1e-12)
                refined += Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5);
        }
        double frequency = sampleRate / refined;
        double confidence = Math.Clamp(1 - cmndf[selected], 0, 1);
        return new PitchEstimate(frequency, confidence, rms);
    }
}
