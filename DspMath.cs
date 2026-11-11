namespace DSPGuitarComparer;

internal static class DspMath
{
    public static void Fft(Span<double> real, Span<double> imaginary)
    {
        int n = real.Length;
        if (n != imaginary.Length || n == 0 || (n & (n - 1)) != 0)
            throw new ArgumentException("FFT wymaga równych tablic o długości będącej potęgą dwójki.");

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2.0 * Math.PI / length;
            double stepReal = Math.Cos(angle), stepImag = Math.Sin(angle);
            int half = length >> 1;
            for (int offset = 0; offset < n; offset += length)
            {
                double wr = 1, wi = 0;
                for (int k = 0; k < half; k++)
                {
                    int even = offset + k, odd = even + half;
                    double tr = wr * real[odd] - wi * imaginary[odd];
                    double ti = wr * imaginary[odd] + wi * real[odd];
                    real[odd] = real[even] - tr;
                    imaginary[odd] = imaginary[even] - ti;
                    real[even] += tr;
                    imaginary[even] += ti;
                    (wr, wi) = (wr * stepReal - wi * stepImag, wr * stepImag + wi * stepReal);
                }
            }
        }
    }

    public static double Hann(int index, int length) =>
        length <= 1 ? 1 : 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (length - 1));

    public static double BlackmanHarris(int index, int length)
    {
        if (length <= 1) return 1;
        double phase = 2 * Math.PI * index / (length - 1);
        return 0.35875 - 0.48829 * Math.Cos(phase) + 0.14128 * Math.Cos(2 * phase) - 0.01168 * Math.Cos(3 * phase);
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0;
        double sum = 0;
        foreach (float value in samples) sum += (double)value * value;
        return Math.Sqrt(sum / samples.Length);
    }

    public static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) * 0.5 : values[middle];
    }
}
