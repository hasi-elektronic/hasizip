using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using HasiZip.Core;

namespace HasiZip.App;

public enum StartMode { Window, Pack, Extract, ExtractTo }

/// <summary>Aufruf aus dem Explorer-Kontextmenü: --pack, --extract, --extract-to (siehe installer/HasiZip.iss).</summary>
public sealed record StartCommand(StartMode Mode, IReadOnlyList<string> Paths)
{
    public static StartCommand Parse(string[] args)
    {
        if (args.Length == 0) return new StartCommand(StartMode.Window, Array.Empty<string>());
        var mode = args[0].ToLowerInvariant() switch
        {
            "--pack" => StartMode.Pack,
            "--extract" => StartMode.Extract,
            "--extract-to" => StartMode.ExtractTo,
            _ => StartMode.Window,
        };
        var paths = (mode == StartMode.Window ? args : args.Skip(1)).Where(a => !a.StartsWith("--")).ToList();
        // Doppelklick/„Öffnen mit“ auf ein Archiv → entpacken; Dateien auf die EXE gezogen → packen
        if (mode == StartMode.Window && paths.Count > 0)
            mode = paths.All(SmartNames.LooksLikeArchive) ? StartMode.Extract : StartMode.Pack;
        return new StartCommand(mode, paths);
    }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--selftest")
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Environment.Exit(SelfTest.Run(args[1]));
        }

        var command = StartCommand.Parse(args);
        if (command.Mode is StartMode.Pack or StartMode.Extract && command.Paths.Count > 0)
        {
            // Explorer startet bei Mehrfachauswahl einen Prozess pro Datei → hier bündeln
            var all = InstanceCollector.Collect(command.Mode.ToString(), command.Paths, TimeSpan.FromMilliseconds(400));
            if (all == null) return;
            command = command with { Paths = all };
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Startup += (_, _) =>
        {
            var window = new MainWindow(command);
            app.MainWindow = window;
            window.Show();
        };
        app.Run();
    }
}
