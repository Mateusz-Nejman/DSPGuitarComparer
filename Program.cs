using System.Text.Json;

namespace DSPGuitarComparer;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static int Main(string[] args)
    {
        AppOptions options;
        try { options = AppOptions.Parse(args); }
        catch (HelpRequestedException)
        {
            PrintUsage();
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Błąd argumentów: {ex.Message}");
            PrintUsage();
            return 2;
        }

        try
        {
            if (!File.Exists(options.AudioPath)) throw new FileNotFoundException("Nie znaleziono pliku audio.", options.AudioPath);
            if (!File.Exists(options.NotesPath)) throw new FileNotFoundException("Nie znaleziono pliku notes.json.", options.NotesPath);

            Console.WriteLine($"Wczytywanie audio: {options.AudioPath}");
            AudioData audio = new AudioPreprocessor().Load(options.AudioPath);
            Console.WriteLine($"Wczytywanie nut: {options.NotesPath}");
            List<GuitarNote> notes = JsonSerializer.Deserialize<List<GuitarNote>>(File.ReadAllText(options.NotesPath), JsonOptions)
                ?? throw new InvalidDataException("Plik JSON musi zawierać tablicę nut.");
            if (notes.Count == 0) throw new InvalidDataException("Plik notes.json nie zawiera nut.");
            ValidateNotes(notes);
            int stringIndexBase = options.StringIndexBase < 0 ? (notes.Any(note => note.String == 0) ? 0 : 1) : options.StringIndexBase;
            options = options with { StringIndexBase = stringIndexBase };

            Console.WriteLine("Detekcja onsetów (Spectral Flux)...");
            List<Onset> onsets = new OnsetDetector().Detect(audio);
            var evaluator = new NoteEvaluator(options, audio);
            List<NoteEvent> events = evaluator.GroupNotes(notes);
            Console.WriteLine($"Ewaluacja {notes.Count} nut w {events.Count} zdarzeniach...");
            List<NoteResult> results = evaluator.Evaluate(events, onsets);
            var summary = new AnalysisSummary { AudioPath = options.AudioPath, NotesPath = options.NotesPath, StringIndexBase = options.StringIndexBase, Audio = audio, Notes = notes, Events = events, Onsets = onsets, Results = results };
            new ReportGenerator().Write(options.ReportPath, summary);
            new CsvReportGenerator().Write(options.CsvPath, summary);
            Console.WriteLine($"Raport zapisany: {Path.GetFullPath(options.ReportPath)}");
            Console.WriteLine($"Raport CSV zapisany: {Path.GetFullPath(options.CsvPath)}");
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"Brak pliku: {ex.FileName ?? ex.Message}");
            return 1;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Nieprawidłowy format JSON: {ex.Message}");
            return 1;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Nieprawidłowe dane wejściowe: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine($"Analiza nie powiodła się: {ex.Message}");
            return 1;
        }
    }

    private static void ValidateNotes(List<GuitarNote> notes)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            GuitarNote note = notes[i];
            if (!double.IsFinite(note.Time) || note.Time < 0)
                throw new InvalidDataException($"Nuta o indeksie {i} ma nieprawidłowy Time.");
            if (note.Duration < 0 || note.VibratoLength < 0 || !double.IsFinite(note.Duration) || !double.IsFinite(note.VibratoLength))
                throw new InvalidDataException($"Nuta {note.Id} ma ujemny lub nieprawidłowy czas trwania.");
            note.Id = string.IsNullOrWhiteSpace(note.Id) ? $"note-{i + 1:D5}" : note.Id;
            note.Techniques ??= [];
        }
    }

    private static void PrintUsage() =>
        Console.WriteLine("Użycie: dotnet run -- [--audio audio.wav] [--notes notes.json] [--report report.txt] [--csv report.csv] [--string-index-base 0|1]");
}
