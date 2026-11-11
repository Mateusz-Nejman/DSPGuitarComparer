using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSPGuitarComparer;

internal sealed class GuitarNote
{
    public string Id { get; set; } = "";
    public double Time { get; set; }
    public int String { get; set; }
    public int Fret { get; set; }
    public double Duration { get; set; }
    public double VibratoLength { get; set; }
    public int Difficulty { get; set; }
    public int ChordId { get; set; } = -1;
    public string? ChordName { get; set; }
    public int? TargetFret { get; set; }
    public List<JsonElement> Techniques { get; set; } = [];
    public double EndTime { get; set; }
    public bool IsSustain { get; set; }
    public bool IsChord { get; set; }
}

internal sealed record AppOptions(string AudioPath, string NotesPath, string ReportPath, string CsvPath, int StringIndexBase)
{
    public static AppOptions Parse(string[] args)
    {
        string audio = "audio.wav";
        string notes = "notes.json";
        string report = "report.txt";
        string csv = "report.csv";
        int indexBase = -1;
        for (int i = 0; i < args.Length; i++)
        {
            string value = i + 1 < args.Length ? args[i + 1] : "";
            switch (args[i])
            {
                case "--audio": audio = RequireValue(args[i], value); i++; break;
                case "--notes": notes = RequireValue(args[i], value); i++; break;
                case "--report": report = RequireValue(args[i], value); i++; break;
                case "--csv": csv = RequireValue(args[i], value); i++; break;
                case "--string-index-base":
                    if (!int.TryParse(RequireValue(args[i], value), out int parsedBase) || parsedBase is not (0 or 1))
                        throw new ArgumentException("--string-index-base musi mieć wartość 0 albo 1.");
                    indexBase = parsedBase;
                    i++;
                    break;
                case "--help":
                    throw new HelpRequestedException();
                default: throw new ArgumentException($"Nieznany argument: {args[i]}");
            }
        }
        return new AppOptions(audio, notes, report, csv, indexBase);
    }

    private static string RequireValue(string option, string value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal)
            ? throw new ArgumentException($"Brak wartości dla {option}.") : value;
}

internal sealed class HelpRequestedException : Exception { }

internal sealed class AudioData
{
    public required float[] MonoSamples { get; init; }
    public required float[] PitchSamples { get; init; }
    public required int SampleRate { get; init; }
    public required int PitchSampleRate { get; init; }
    public required int Channels { get; init; }
    public required double NoiseRms { get; init; }
    public required double GateThresholdRms { get; init; }
    public double DurationSeconds => (double)MonoSamples.Length / SampleRate;
}

internal sealed record Onset(double TimeSeconds, double Strength);
internal sealed record PitchEstimate(double FrequencyHz, double Confidence, double Rms)
{
    public bool IsVoiced => FrequencyHz > 0 && double.IsFinite(FrequencyHz);
}

internal sealed class NoteEvent
{
    public required List<GuitarNote> Notes { get; init; }
    public required double Time { get; init; }
    public bool IsChord => Notes.Count > 1 || Notes.Any(n => n.IsChord || n.ChordId >= 0);
}

internal enum NoteStatus { Perfect, Early, Late, PoorTiming, WrongPitch, Missed }

internal sealed class NoteResult
{
    public required GuitarNote Note { get; init; }
    public double? ExpectedHz { get; set; }
    public double? DetectedHz { get; set; }
    public double? DetectedTime { get; set; }
    public double? TimingDeltaMs { get; set; }
    public double? PitchErrorCents { get; set; }
    public string ExpectedName { get; set; } = "?";
    public string? DetectedName { get; set; }
    public NoteStatus Status { get; set; }
    public string TechniqueStatus { get; set; } = "—";
    public bool HasPitchMatch { get; set; }
}

internal sealed class AnalysisSummary
{
    public required string AudioPath { get; init; }
    public required string NotesPath { get; init; }
    public required int StringIndexBase { get; init; }
    public required AudioData Audio { get; init; }
    public required List<GuitarNote> Notes { get; init; }
    public required List<NoteEvent> Events { get; init; }
    public required List<Onset> Onsets { get; init; }
    public required List<NoteResult> Results { get; init; }
}
