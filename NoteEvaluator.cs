namespace DSPGuitarComparer;

internal sealed class NoteEvaluator
{
    private static readonly double[] OpenStringHz = [82.4069, 110.0, 146.8324, 195.9977, 246.9417, 329.6276];
    private static readonly int[] OpenStringMidi = [40, 45, 50, 55, 59, 64];
    private static readonly string[] PitchNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private readonly AppOptions _options;
    private readonly AudioData _audio;
    private readonly YinPitchDetector _yin = new(1024, 0.14);
    private readonly PolyphonicAnalyzer _polyphonic;

    public NoteEvaluator(AppOptions options, AudioData audio)
    {
        _options = options;
        _audio = audio;
        _polyphonic = new PolyphonicAnalyzer(audio.PitchSamples, audio.PitchSampleRate, audio.GateThresholdRms);
    }

    public List<NoteEvent> GroupNotes(List<GuitarNote> notes)
    {
        var groups = new List<NoteEvent>();
        foreach (var ordered in notes.OrderBy(n => n.Time))
        {
            if (groups.Count == 0 || ordered.Time - groups[^1].Time > 0.010)
                groups.Add(new NoteEvent { Time = ordered.Time, Notes = [ordered] });
            else
                groups[^1].Notes.Add(ordered);
        }
        return groups;
    }

    public List<NoteResult> Evaluate(List<NoteEvent> events, List<Onset> onsets)
    {
        var results = new List<NoteResult>();
        var consumedOnsets = new HashSet<int>();
        int pitchSize = 1024;
        double pitchWindowSeconds = (double)pitchSize / _audio.PitchSampleRate;

        foreach (NoteEvent noteEvent in events)
        {
            int onsetIndex = FindNearestOnset(noteEvent.Time, onsets, consumedOnsets);
            Onset? onset = onsetIndex >= 0 ? onsets[onsetIndex] : null;
            if (onsetIndex >= 0) consumedOnsets.Add(onsetIndex);
            double analysisStart = Math.Max(0, (onset?.TimeSeconds ?? noteEvent.Time) + 0.020);

            var expected = noteEvent.Notes.Select(n => ExpectedFrequency(n)).ToArray();
            var lower = expected.Where(x => x.HasValue).Select(x => x!.Value).Order().ToArray();
            var eventResults = new List<NoteResult>(noteEvent.Notes.Count);
            for (int i = 0; i < noteEvent.Notes.Count; i++)
            {
                GuitarNote note = noteEvent.Notes[i];
                double? expectedHz = expected[i];
                var result = new NoteResult
                {
                    Note = note,
                    ExpectedHz = expectedHz,
                    ExpectedName = expectedHz.HasValue ? NoteName(expectedHz.Value) : "invalid string",
                    DetectedTime = onset?.TimeSeconds,
                    TimingDeltaMs = onset is null ? null : (onset.TimeSeconds - note.Time) * 1000
                };
                eventResults.Add(result);

                if (expectedHz is null) continue;
                PitchEstimate estimate;
                if (noteEvent.IsChord)
                {
                    var lowerNotes = lower.Where(x => x < expectedHz.Value * 0.995).ToArray();
                    estimate = _polyphonic.AnalyzeNote(analysisStart, expectedHz.Value, lowerNotes);
                }
                else
                {
                    estimate = _yin.AnalyzeAtTime(_audio.PitchSamples, _audio.PitchSampleRate, analysisStart, _audio.GateThresholdRms);
                    if ((!estimate.IsVoiced || estimate.Confidence < 0.45) && onset is not null)
                    {
                        var fallback = _yin.AnalyzeAtTime(_audio.PitchSamples, _audio.PitchSampleRate, Math.Max(0, note.Time + 0.020), _audio.GateThresholdRms);
                        if (fallback.Confidence > estimate.Confidence) estimate = fallback;
                    }

                    if (estimate.IsVoiced)
                    {
                        double cents = Cents(estimate.FrequencyHz, expectedHz.Value);
                        if (Math.Abs(cents - 1200) <= 50)
                        {
                            var (fundamental, second) = _polyphonic.MeasureOctaveRatio(analysisStart, expectedHz.Value);
                            if (fundamental >= second * 0.08) estimate = estimate with { FrequencyHz = expectedHz.Value };
                        }
                        else if (Math.Abs(cents + 1200) <= 50)
                        {
                            var (fundamental, second) = _polyphonic.MeasureOctaveRatio(analysisStart, estimate.FrequencyHz);
                            if (second >= fundamental * 0.08) estimate = estimate with { FrequencyHz = expectedHz.Value };
                        }
                    }
                }

                if (estimate.IsVoiced)
                {
                    result.DetectedHz = estimate.FrequencyHz;
                    result.DetectedName = NoteName(estimate.FrequencyHz);
                    result.PitchErrorCents = Cents(estimate.FrequencyHz, expectedHz.Value);
                    result.HasPitchMatch = Math.Abs(result.PitchErrorCents.Value) <= 50;
                }
            }

            bool eventHasTone = eventResults.Any(r => r.HasPitchMatch || r.DetectedHz.HasValue);
            if (onset is null && eventHasTone)
            {
                // An active, pitch-matched frame can verify a note even when the transient detector misses a legato pick.
                foreach (var result in eventResults.Where(r => r.DetectedHz.HasValue))
                {
                    result.DetectedTime = result.Note.Time;
                    result.TimingDeltaMs = 0;
                }
            }

            foreach (NoteResult result in eventResults)
            {
                Classify(result, onset is not null || result.DetectedHz.HasValue);
                result.TechniqueStatus = AnalyzeTechniques(result, analysisStart, pitchWindowSeconds, noteEvent.IsChord);
                results.Add(result);
            }
        }
        return results;
    }

    private int FindNearestOnset(double time, List<Onset> onsets, HashSet<int> consumed)
    {
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < onsets.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            double distance = Math.Abs(onsets[i].TimeSeconds - time);
            if (distance <= 0.150 && distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return best;
    }

    private void Classify(NoteResult result, bool hasEvidence)
    {
        if (!hasEvidence || result.ExpectedHz is null || (!result.HasPitchMatch && !result.DetectedHz.HasValue))
        {
            result.Status = NoteStatus.Missed;
            return;
        }
        if (!result.HasPitchMatch)
        {
            result.Status = NoteStatus.WrongPitch;
            return;
        }
        double delta = result.TimingDeltaMs ?? 0;
        result.Status = Math.Abs(delta) <= 35 ? NoteStatus.Perfect
            : Math.Abs(delta) <= 100 ? (delta < 0 ? NoteStatus.Early : NoteStatus.Late)
            : NoteStatus.PoorTiming;
    }

    private string AnalyzeTechniques(NoteResult result, double analysisStart, double pitchWindowSeconds, bool chord)
    {
        var statuses = new List<string>();
        if (chord) statuses.Add(result.HasPitchMatch ? "Chord tone OK" : "Chord tone missing");
        if (result.Note.Techniques.Count > 0) statuses.Add("Technique " + string.Join(",", result.Note.Techniques.Select(t => t.ToString())));

        double duration = Math.Max(result.Note.Duration, result.Note.EndTime - result.Note.Time);
        if (result.Note.IsSustain && duration > 0.04)
            statuses.Add(CheckSustain(result, duration));

        if (result.Note.TargetFret.HasValue && result.ExpectedHz.HasValue)
        {
            double target = result.ExpectedHz.Value * Math.Pow(2, (result.Note.TargetFret.Value - result.Note.Fret) / 12.0);
            double targetTime = duration > 0.10 ? result.Note.Time + duration * 0.72 : analysisStart;
            var targetPitch = _yin.AnalyzeAtTime(_audio.PitchSamples, _audio.PitchSampleRate, targetTime, _audio.GateThresholdRms);
            statuses.Add(targetPitch.IsVoiced && Math.Abs(Cents(targetPitch.FrequencyHz, target)) <= 50 ? "Target reached" : "Target not reached");
        }

        if (result.Note.VibratoLength > 0.04 && result.ExpectedHz.HasValue)
            statuses.Add(CheckVibrato(result, result.Note.VibratoLength));

        return statuses.Count == 0 ? "—" : string.Join("; ", statuses);
    }

    private string CheckSustain(NoteResult result, double duration)
    {
        double start = Math.Max(0, result.Note.Time);
        double end = Math.Min(_audio.DurationSeconds, start + duration);
        if (end <= start) return "Sustain unavailable";
        double step = 0.020;
        int total = 0, active = 0;
        for (double time = start; time < end; time += step)
        {
            if (RmsAtTime(time, step) >= _audio.GateThresholdRms) active++;
            total++;
        }
        double activeRatio = total == 0 ? 0 : (double)active / total;
        int pitchChecks = 0, pitchMatches = 0;
        if (result.ExpectedHz.HasValue)
        {
            double pitchStep = Math.Max(0.10, duration / 8);
            for (double time = start + 0.04; time + 0.085 < end; time += pitchStep)
            {
                var pitch = _yin.AnalyzeAtTime(_audio.PitchSamples, _audio.PitchSampleRate, time, _audio.GateThresholdRms);
                pitchChecks++;
                if (pitch.IsVoiced && Math.Abs(Cents(pitch.FrequencyHz, result.ExpectedHz.Value)) <= 50) pitchMatches++;
            }
        }
        double pitchRatio = pitchChecks == 0 ? 0 : (double)pitchMatches / pitchChecks;
        return activeRatio >= 0.8 && (pitchChecks == 0 || pitchRatio >= 0.65)
            ? "Sustain OK" : activeRatio < 0.8 ? $"Sustain short ({activeRatio:P0})" : "Sustain pitch drift";
    }

    private string CheckVibrato(NoteResult result, double duration)
    {
        if (!result.ExpectedHz.HasValue || duration < 0.30) return "Vibrato too short to verify";
        double start = Math.Max(0, result.Note.Time + 0.04);
        double end = Math.Min(_audio.DurationSeconds, result.Note.Time + duration);
        const double step = 0.025;
        var cents = new List<double>();
        for (double time = start; time + 0.085 < end; time += step)
        {
            var pitch = new YinPitchDetector(1024, 0.16).AnalyzeAtTime(_audio.PitchSamples, _audio.PitchSampleRate, time, _audio.GateThresholdRms);
            if (pitch.IsVoiced && pitch.Confidence > 0.45)
                cents.Add(Cents(pitch.FrequencyHz, result.ExpectedHz.Value));
        }
        if (cents.Count < 14) return "Vibrato not confirmed";
        double mean = cents.Average();
        double variance = cents.Average(x => (x - mean) * (x - mean));
        double rms = Math.Sqrt(variance);
        double bestCorrelation = 0;
        int bestLag = 0;
        for (int lag = 5; lag <= 12; lag++)
        {
            if (lag >= cents.Count) continue;
            double numerator = 0, left = 0, right = 0;
            for (int i = 0; i + lag < cents.Count; i++)
            {
                double a = cents[i] - mean, b = cents[i + lag] - mean;
                numerator += a * b;
                left += a * a;
                right += b * b;
            }
            double correlation = numerator / Math.Sqrt(Math.Max(left * right, 1e-20));
            if (correlation > bestCorrelation) { bestCorrelation = correlation; bestLag = lag; }
        }
        double rate = bestLag == 0 ? 0 : 1.0 / (bestLag * step);
        return rms >= 8 && bestCorrelation >= 0.25 && rate is >= 4 and <= 8
            ? $"Vibrato OK ({rate:F1} Hz)" : "Vibrato not confirmed";
    }

    private double RmsAtTime(double time, double duration)
    {
        int start = (int)Math.Round(time * _audio.SampleRate);
        int count = Math.Max(1, (int)Math.Round(duration * _audio.SampleRate));
        if (start < 0 || start >= _audio.MonoSamples.Length) return 0;
        count = Math.Min(count, _audio.MonoSamples.Length - start);
        return DspMath.Rms(_audio.MonoSamples.AsSpan(start, count));
    }

    private double? ExpectedFrequency(GuitarNote note)
    {
        int index = note.String - _options.StringIndexBase;
        if ((uint)index >= OpenStringHz.Length || note.Fret < 0 || note.Fret > 36) return null;
        return OpenStringHz[index] * Math.Pow(2, note.Fret / 12.0);
    }

    private static double Cents(double frequency, double expected) => 1200 * Math.Log2(frequency / expected);

    private static string NoteName(double frequency)
    {
        if (frequency <= 0) return "?";
        int midi = (int)Math.Round(69 + 12 * Math.Log2(frequency / 440.0));
        return $"{PitchNames[((midi % 12) + 12) % 12]}{midi / 12 - 1}";
    }
}
