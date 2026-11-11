namespace DSPGuitarComparer;

internal sealed class PolyphonicAnalyzer
{
    private const int WindowSize = 1024;
    private static readonly double[] Window = Enumerable.Range(0, WindowSize).Select(i => DspMath.BlackmanHarris(i, WindowSize)).ToArray();
    private static readonly double WindowSum = Window.Sum();
    private readonly float[] _samples;
    private readonly int _sampleRate;
    private readonly double _gate;

    public PolyphonicAnalyzer(float[] samples, int sampleRate, double gate)
    {
        _samples = samples;
        _sampleRate = sampleRate;
        _gate = gate;
    }

    public PitchEstimate AnalyzeNote(double startTime, double expectedHz, IReadOnlyList<double> lowerNotes)
    {
        int start = (int)Math.Round(startTime * _sampleRate);
        if (start < 0 || start + WindowSize > _samples.Length || expectedHz <= 0)
            return new PitchEstimate(0, 0, 0);

        double rms = DspMath.Rms(_samples.AsSpan(start, WindowSize));
        if (rms < _gate) return new PitchEstimate(0, 0, rms);

        double bestScore = 0, bestFrequency = expectedHz;
        for (int cents = -150; cents <= 150; cents += 10)
        {
            double candidate = expectedHz * Math.Pow(2, cents / 1200.0);
            double score = 0;
            for (int harmonic = 1; harmonic <= 3; harmonic++)
            {
                double frequency = candidate * harmonic;
                if (frequency >= _sampleRate * 0.48 || IsMasked(frequency, lowerNotes)) continue;
                double amplitude = GoertzelAmplitude(start, frequency);
                score += amplitude / harmonic;
            }
            if (score > bestScore) { bestScore = score; bestFrequency = candidate; }
        }
        double confidence = bestScore / Math.Max(rms * 2, 1e-9);
        return bestScore < Math.Max(_gate * 2.0, rms * 0.012)
            ? new PitchEstimate(0, confidence, rms)
            : new PitchEstimate(bestFrequency, Math.Clamp(confidence, 0, 1), rms);
    }

    public (double Fundamental, double SecondHarmonic) MeasureOctaveRatio(double startTime, double fundamentalHz)
    {
        int start = (int)Math.Round(startTime * _sampleRate);
        if (start < 0 || start + WindowSize > _samples.Length || fundamentalHz * 2 >= _sampleRate * 0.48) return (0, 0);
        return (GoertzelAmplitude(start, fundamentalHz), GoertzelAmplitude(start, fundamentalHz * 2));
    }

    private bool IsMasked(double frequency, IReadOnlyList<double> lowerNotes)
    {
        foreach (double lower in lowerNotes)
        {
            for (int harmonic = 1; harmonic <= 5; harmonic++)
            {
                double lowerHarmonic = lower * harmonic;
                if (Math.Abs(frequency - lowerHarmonic) <= Math.Max(3.0, frequency * 0.018)) return true;
            }
        }
        return false;
    }

    private double GoertzelAmplitude(int start, double frequency)
    {
        double omega = 2 * Math.PI * frequency / _sampleRate;
        double coefficient = 2 * Math.Cos(omega);
        double previous = 0, previous2 = 0;
        for (int i = 0; i < WindowSize; i++)
        {
            double value = _samples[start + i] * Window[i];
            double current = value + coefficient * previous - previous2;
            previous2 = previous;
            previous = current;
        }
        double power = Math.Max(0, previous2 * previous2 + previous * previous - coefficient * previous * previous2);
        return 2 * Math.Sqrt(power) / WindowSum;
    }
}
