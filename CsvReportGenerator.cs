using System.Globalization;
using System.Text;

namespace DSPGuitarComparer;

internal sealed class CsvReportGenerator
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly string[] Headers =
    [
        "RecordType", "Metric", "Value", "Id", "ExpectedTimeSeconds", "DetectedTimeSeconds", "TimingDeltaMs",
        "String", "Fret", "ExpectedNote", "ExpectedHz", "DetectedNote", "DetectedHz", "PitchErrorCents",
        "TechniquesSustain", "Status"
    ];

    public void Write(string path, AnalysisSummary summary)
    {
        var output = new StringBuilder(Math.Max(16_384, summary.Results.Count * 128));
        WriteRow(output, Headers);

        int successful = summary.Results.Count(r => r.HasPitchMatch && r.TimingDeltaMs.HasValue && Math.Abs(r.TimingDeltaMs.Value) <= 100);
        double accuracy = summary.Results.Count == 0 ? 0 : 100.0 * successful / summary.Results.Count;
        var timing = summary.Results.Where(r => r.TimingDeltaMs.HasValue && r.Status != NoteStatus.Missed).Select(r => r.TimingDeltaMs!.Value).ToArray();
        var pitch = summary.Results.Where(r => r.PitchErrorCents.HasValue).Select(r => r.PitchErrorCents!.Value).ToArray();
        double timingMean = timing.Length == 0 ? 0 : timing.Average();
        double timingJitter = timing.Length == 0 ? 0 : Math.Sqrt(timing.Average(x => (x - timingMean) * (x - timingMean)));
        double pitchMean = pitch.Length == 0 ? 0 : pitch.Average();
        double noiseDb = summary.Audio.NoiseRms <= 0 ? double.NegativeInfinity : 20 * Math.Log10(summary.Audio.NoiseRms);

        WriteSummary(output, "AudioPath", Path.GetFullPath(summary.AudioPath));
        WriteSummary(output, "NotesPath", Path.GetFullPath(summary.NotesPath));
        WriteSummary(output, "DurationSeconds", F(summary.Audio.DurationSeconds));
        WriteSummary(output, "SampleRateHz", summary.Audio.SampleRate.ToString(Inv));
        WriteSummary(output, "PitchTrackingSampleRateHz", summary.Audio.PitchSampleRate.ToString(Inv));
        WriteSummary(output, "Channels", summary.Audio.Channels.ToString(Inv));
        WriteSummary(output, "NoiseRms", F(summary.Audio.NoiseRms));
        WriteSummary(output, "NoiseDbFs", double.IsNegativeInfinity(noiseDb) ? "-Infinity" : F(noiseDb));
        WriteSummary(output, "GateThresholdRms", F(summary.Audio.GateThresholdRms));
        WriteSummary(output, "StringIndexBase", summary.StringIndexBase.ToString(Inv));
        WriteSummary(output, "NoteCount", summary.Results.Count.ToString(Inv));
        WriteSummary(output, "SingleEvents", summary.Events.Count(e => !e.IsChord).ToString(Inv));
        WriteSummary(output, "ChordEvents", summary.Events.Count(e => e.IsChord).ToString(Inv));
        WriteSummary(output, "DetectedOnsets", summary.Onsets.Count.ToString(Inv));
        WriteSummary(output, "AccuracyPercent", F(accuracy));
        WriteSummary(output, "MeanTimingOffsetMs", F(timingMean));
        WriteSummary(output, "TimingJitterMs", F(timingJitter));
        WriteSummary(output, "MeanPitchErrorCents", F(pitchMean));

        foreach (NoteResult result in summary.Results)
        {
            WriteRow(output,
            [
                "note", "", "", result.Note.Id, F(result.Note.Time), NullableF(result.DetectedTime), NullableF(result.TimingDeltaMs),
                result.Note.String.ToString(Inv), result.Note.Fret.ToString(Inv), result.ExpectedName, NullableF(result.ExpectedHz),
                result.DetectedName ?? "", NullableF(result.DetectedHz), NullableF(result.PitchErrorCents),
                result.TechniqueStatus, StatusText(result.Status)
            ]);
        }

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, output.ToString(), new UTF8Encoding(false));
    }

    private static void WriteSummary(StringBuilder output, string metric, string value) =>
        WriteRow(output, ["summary", metric, value, "", "", "", "", "", "", "", "", "", "", "", "", ""]);

    private static void WriteRow(StringBuilder output, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) output.Append(',');
            string field = fields[i];
            if (field.Contains('"') || field.Contains(',') || field.Contains('\r') || field.Contains('\n'))
                output.Append('"').Append(field.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            else
                output.Append(field);
        }
        output.AppendLine();
    }

    private static string F(double value) => value.ToString("0.########", Inv);
    private static string NullableF(double? value) => value.HasValue ? F(value.Value) : "";
    private static string StatusText(NoteStatus status) => status switch
    {
        NoteStatus.Perfect => "HIT - PERFECT",
        NoteStatus.Early => "HIT - EARLY",
        NoteStatus.Late => "HIT - LATE",
        NoteStatus.PoorTiming => "HIT - POOR TIMING",
        NoteStatus.WrongPitch => "WRONG PITCH",
        _ => "MISSED"
    };
}
