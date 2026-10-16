using System.Diagnostics;
using System.Globalization;
using HasiZip.Core;

namespace HasiZip.Cli;

/// <summary>
/// hasizip – Kommandozeile für Skripte, Server und Benchmarks.
///   hasizip a [-l fast|normal|max] [-t N] [-o ziel.zip] quelle…   packen
///   hasizip x [-p passwort] [-t N] archiv [zielordner]            entpacken (ohne Ziel: intelligent daneben)
///   hasizip bench ordner                                          Geschwindigkeit messen
/// </summary>
public static class Program
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            var rest = args.Skip(1).ToList();
            return args[0] switch
            {
                "a" or "pack" => await Pack(rest, cts.Token),
                "x" or "extract" => await Extract(rest, cts.Token),
                "bench" => await Bench(rest, cts.Token),
                _ => Fail($"Unbekannter Befehl „{args[0]}“. Hilfe: hasizip --help"),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nAbgebrochen.");
            return 130;
        }
        catch (PasswordRequiredException)
        {
            return Fail("Das Archiv ist verschlüsselt. Passwort mit -p angeben.", 3);
        }
        catch (WrongPasswordException)
        {
            return Fail("Das Passwort ist falsch.", 3);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsupportedArchiveException or InvalidDataException)
        {
            return Fail(ex.Message, 2);
        }
    }

    private static async Task<int> Pack(List<string> args, CancellationToken token)
    {
        var level = PackLevel.Normal;
        int threads = 0;
        string? output = null;
        var inputs = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "-l": level = ParseLevel(Next(args, ref i)); break;
                case "-t": threads = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                case "-o": output = Next(args, ref i); break;
                default: inputs.Add(args[i]); break;
            }
        }
        if (inputs.Count == 0) return Fail("Keine Quellen angegeben.");
        // Kompatibel zu „7z a ziel.zip quellen…“
        if (output == null && inputs.Count > 1 && inputs[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !File.Exists(inputs[0]) && !Directory.Exists(inputs[0]))
        {
            output = inputs[0];
            inputs.RemoveAt(0);
        }
        output ??= SmartNames.PackTarget(inputs);
        if (File.Exists(output)) return Fail($"Ziel existiert bereits: {output}");

        var result = await ZipPacker.PackAsync(inputs, output, new PackOptions { Level = level, Threads = threads }, new ConsoleProgress("Packe"), token);
        ConsoleProgress.Clear();
        Console.WriteLine($"{result.ZipPath}");
        Console.WriteLine($"  {Files(result.Files)}, {Size(result.InputBytes)} → {Size(result.OutputBytes)} ({Ratio(result.InputBytes, result.OutputBytes)}) in {Secs(result.Elapsed)} · {Speed(result.InputBytes, result.Elapsed)}");
        foreach (var s in result.Skipped.Take(5)) Console.Error.WriteLine($"  übersprungen (gesperrt/nicht lesbar): {s}");
        if (result.Skipped.Count > 5) Console.Error.WriteLine($"  … und {result.Skipped.Count - 5} weitere");
        return result.Skipped.Count > 0 ? 4 : 0;
    }

    private static async Task<int> Extract(List<string> args, CancellationToken token)
    {
        string? password = null;
        int threads = 0;
        var positional = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "-p": password = Next(args, ref i); break;
                case "-t": threads = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                default: positional.Add(args[i]); break;
            }
        }
        if (positional.Count is 0 or > 2) return Fail("Aufruf: hasizip x [-p passwort] archiv [zielordner]");
        var options = new ExtractOptions { Password = password, Threads = threads };
        var progress = new ConsoleProgress("Entpacke");
        var result = positional.Count == 2
            ? await Extractor.ExtractToAsync(positional[0], positional[1], options, progress, token)
            : await Extractor.ExtractSmartAsync(positional[0], null, options, progress, token);
        ConsoleProgress.Clear();
        Console.WriteLine(result.OutputPath);
        Console.WriteLine($"  {Files(result.Files)}, {Size(result.Bytes)} in {Secs(result.Elapsed)} · {Speed(result.Bytes, result.Elapsed)}");
        return 0;
    }

    private static async Task<int> Bench(List<string> args, CancellationToken token)
    {
        if (args.Count != 1 || !Directory.Exists(args[0])) return Fail("Aufruf: hasizip bench ordner");
        var work = Path.Combine(Path.GetTempPath(), "hasizip-bench-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            Console.WriteLine($"Quelle: {args[0]} · {Environment.ProcessorCount} Kerne");
            foreach (var level in new[] { PackLevel.Fast, PackLevel.Normal, PackLevel.Max })
            {
                var zip = Path.Combine(work, $"{level}.zip");
                var r = await ZipPacker.PackAsync(new[] { args[0] }, zip, new PackOptions { Level = level }, null, token);
                Console.WriteLine($"  Packen {level,-6}: {Secs(r.Elapsed),8} · {Speed(r.InputBytes, r.Elapsed),12} · {Size(r.OutputBytes)} ({Ratio(r.InputBytes, r.OutputBytes)})");
                if (level == PackLevel.Normal)
                {
                    var x = await Extractor.ExtractToAsync(zip, Path.Combine(work, "out"), null, null, token);
                    Console.WriteLine($"  Entpacken    : {Secs(x.Elapsed),8} · {Speed(x.Bytes, x.Elapsed),12}");
                }
                File.Delete(zip);
            }
        }
        finally
        {
            Directory.Delete(work, true);
        }
        return 0;
    }

    private static PackLevel ParseLevel(string s) => s.ToLowerInvariant() switch
    {
        "fast" or "schnell" or "1" => PackLevel.Fast,
        "max" or "maximal" or "9" => PackLevel.Max,
        _ => PackLevel.Normal,
    };

    private static string Next(List<string> args, ref int i)
    {
        if (i + 1 >= args.Count) throw new IOException($"Wert fehlt nach {args[i]}");
        return args[++i];
    }

    private static int Fail(string message, int code = 1)
    {
        ConsoleProgress.Clear();
        Console.Error.WriteLine("Fehler: " + message);
        return code;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            HasiZip – schnell packen und entpacken (Hasi Elektronic)

              hasizip a [-l fast|normal|max] [-t threads] [-o ziel.zip] quelle…
                  Packt Dateien/Ordner in eine ZIP (alle Kerne). Ohne -o: Name automatisch.
              hasizip x [-p passwort] [-t threads] archiv [zielordner]
                  Entpackt ZIP, 7z, RAR, TAR, GZ, BZ2, XZ. Ohne Ziel: intelligent neben das Archiv.
              hasizip bench ordner
                  Misst Pack- und Entpackgeschwindigkeit.
            """);
    }

    internal static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : v.ToString(v >= 100 ? "0" : "0.0", De) + " " + units[u];
    }

    private static string Files(int n) => n == 1 ? "1 Datei" : n.ToString("N0", De) + " Dateien";

    private static string Speed(long bytes, TimeSpan t) => t.TotalSeconds <= 0 ? "–" : Size((long)(bytes / t.TotalSeconds)) + "/s";
    private static string Secs(TimeSpan t) => t.TotalSeconds.ToString("0.00", De) + " s";
    private static string Ratio(long input, long output) =>
        input == 0 ? "–" : (output <= input ? "−" : "+") + Math.Abs(100 - output * 100.0 / input).ToString("0", De) + " %";

    private sealed class ConsoleProgress : IProgress<ArchiveProgress>
    {
        private readonly string _verb;
        public ConsoleProgress(string verb) { _verb = verb; }
        public static void Clear()
        {
            if (!Console.IsErrorRedirected) Console.Error.Write("\r" + new string(' ', Math.Max(0, Console.BufferWidth - 1)) + "\r");
        }
        public void Report(ArchiveProgress p)
        {
            if (Console.IsErrorRedirected) return;
            var pct = p.TotalBytes > 0 ? $"{p.Fraction * 100,3:0} %" : "";
            var line = $"\r{_verb} {pct} {Size(p.DoneBytes)} · {p.CurrentName}";
            int width = Math.Max(20, Console.BufferWidth - 1);
            Console.Error.Write(line.Length > width ? line[..width] : line.PadRight(width));
        }
    }
}
