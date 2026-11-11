using System.Globalization;
using System.Text;

namespace DSPGuitarComparer;

internal sealed class ReportGenerator
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public void Write(string path, AnalysisSummary summary)
    {
        var results = summary.Results;
        int chordEvents = summary.Events.Count(e => e.IsChord);
        int singleNotes = summary.Events.Count(e => !e.IsChord);
        int successful = results.Count(r => r.HasPitchMatch && r.TimingDeltaMs.HasValue && Math.Abs(r.TimingDeltaMs.Value) <= 100);
        double accuracy = results.Count == 0 ? 0 : 100.0 * successful / results.Count;
        var timing = results.Where(r => r.TimingDeltaMs.HasValue && r.Status != NoteStatus.Missed).Select(r => r.TimingDeltaMs!.Value).ToArray();
        var pitch = results.Where(r => r.PitchErrorCents.HasValue).Select(r => r.PitchErrorCents!.Value).ToArray();
        double timingMean = timing.Length == 0 ? 0 : timing.Average();
        double timingStd = timing.Length == 0 ? 0 : Math.Sqrt(timing.Average(x => (x - timingMean) * (x - timingMean)));
        double pitchMean = pitch.Length == 0 ? 0 : pitch.Average();
        double noiseDb = summary.Audio.NoiseRms <= 0 ? double.NegativeInfinity : 20 * Math.Log10(summary.Audio.NoiseRms);

        var output = new StringBuilder(Math.Max(16_384, results.Count * 105));
        output.AppendLine("DSP GUITAR COMPARER — RAPORT ANALIZY");
        output.AppendLine(new string('=', 132));
        output.AppendLine($"Plik audio:             {Path.GetFullPath(summary.AudioPath)}");
        output.AppendLine($"Mapa nut:               {Path.GetFullPath(summary.NotesPath)}");
        output.AppendLine($"Czas trwania:           {summary.Audio.DurationSeconds:F3} s");
        output.AppendLine($"Sample rate:            {summary.Audio.SampleRate} Hz (pitch tracking: {summary.Audio.PitchSampleRate} Hz)");
        output.AppendLine($"Kanały wejściowe:       {summary.Audio.Channels} (analiza mono)");
        output.AppendLine($"Poziom szumu RMS:       {summary.Audio.NoiseRms:F6} ({FormatDb(noiseDb)} dBFS)");
        output.AppendLine($"Próg bramki RMS:        {summary.Audio.GateThresholdRms:F6} ({20 * Math.Log10(summary.Audio.GateThresholdRms):F1} dBFS)");
        output.AppendLine($"Indeksowanie strun:     {summary.StringIndexBase}-based");
        output.AppendLine($"Nuty w notes.json:      {results.Count}");
        output.AppendLine($"Pojedyncze zdarzenia:   {singleNotes}");
        output.AppendLine($"Zdarzenia akordowe:     {chordEvents}");
        output.AppendLine($"Wykryte onsety:         {summary.Onsets.Count}");
        output.AppendLine($"Accuracy (intonacja i timing ≤ 100 ms): {accuracy:F2}% ({successful}/{results.Count})");
        output.AppendLine($"Średni błąd czasu:      {timingMean:+0.0;-0.0;0.0} ms");
        output.AppendLine($"Timing jitter (σ):      {timingStd:F1} ms");
        output.AppendLine($"Średni błąd wysokości:  {pitchMean:+0.0;-0.0;0.0} centów ({pitch.Length} nut z estymacją)");
        output.AppendLine();
        output.AppendLine("SZCZEGÓŁOWE WYNIKI");
        output.AppendLine(new string('-', 132));
        output.AppendLine("ID       | Expected [s] | Detected [s] | Delta [ms] | String/Fret     | Expected Hz | Detected Hz | Pitch [c] | Techniques / Sustain       | Status");
        output.AppendLine(new string('-', 132));
        foreach (NoteResult result in results)
        {
            string id = Clip(result.Note.Id, 8).PadRight(8);
            string expectedTime = result.Note.Time.ToString("F3", Inv).PadLeft(12);
            string detectedTime = result.DetectedTime.HasValue ? result.DetectedTime.Value.ToString("F3", Inv).PadLeft(12) : "---".PadLeft(12);
            string delta = result.TimingDeltaMs.HasValue ? (result.TimingDeltaMs.Value >= 0 ? "+" : "") + result.TimingDeltaMs.Value.ToString("F1", Inv) : "---";
            string sf = Clip($"S{result.Note.String} F{result.Note.Fret} ({result.ExpectedName})", 15).PadRight(15);
            string expectedHz = result.ExpectedHz.HasValue ? result.ExpectedHz.Value.ToString("F2", Inv).PadLeft(11) : "---".PadLeft(11);
            string detectedHz = result.DetectedHz.HasValue ? result.DetectedHz.Value.ToString("F2", Inv).PadLeft(11) : "---".PadLeft(11);
            string pitchCents = result.PitchErrorCents.HasValue ? result.PitchErrorCents.Value.ToString("+0.0;-0.0;0.0", Inv).PadLeft(9) : "---".PadLeft(9);
            string techniques = Clip(result.TechniqueStatus, 27).PadRight(27);
            output.AppendLine($"{id} | {expectedTime} | {detectedTime} | {delta,10} | {sf} | {expectedHz} | {detectedHz} | {pitchCents} | {techniques} | {StatusText(result.Status)}");
        }

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, output.ToString(), new UTF8Encoding(false));
    }

    private static string StatusText(NoteStatus status) => status switch
    {
        NoteStatus.Perfect => "[HIT - PERFECT]",
        NoteStatus.Early => "[HIT - EARLY]",
        NoteStatus.Late => "[HIT - LATE]",
        NoteStatus.PoorTiming => "[HIT - POOR TIMING]",
        NoteStatus.WrongPitch => "[WRONG PITCH]",
        _ => "[MISSED]"
    };

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
    private static string FormatDb(double db) => double.IsNegativeInfinity(db) ? "-inf" : db.ToString("F1", Inv);
}
