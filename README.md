# DSP Guitar Comparer

Aplikacja konsolowa .NET 8 porównuje nagranie DI gitary z oczekiwanymi nutami z JSON i zapisuje szczegółowe raporty tekstowe oraz CSV.

```powershell
dotnet restore
dotnet run -- --audio audio.wav --notes notes.json --report report.txt
```

Domyślne nazwy plików to `audio.wav`, `notes.json`, `report.txt` i `report.csv`. CSV zawiera rekordy `summary` z metrykami podsumowania oraz rekordy `note` ze szczegółami każdej nuty. Bez podania opcji program przyjmuje indeksowanie 0–5, jeśli w JSON występuje `String: 0`; w przeciwnym razie wybiera 1–6. Możesz wymusić wybrany wariant:

```powershell
dotnet run -- --string-index-base 1
```

Argumenty `--audio`, `--notes` i `--report` przyjmują ścieżki plików. Program obsługuje pliki czytane przez `NAudio.AudioFileReader`; analiza zmienia wielokanałowy dźwięk na mono.

Argument `--csv` wybiera ścieżkę raportu CSV, np. `dotnet run -- --csv wyniki.csv`.
