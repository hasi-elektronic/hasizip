using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using HasiZip.Core;
using Microsoft.Win32;

namespace HasiZip.App;

/// <summary>
/// Ein Fenster für alles: Dateien hineinziehen → ZIP; Archiv hineinziehen → entpacken.
/// Aus dem Kontextmenü gestartet läuft der Auftrag sofort und das Fenster schließt sich danach selbst.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly StartCommand _start;
    private readonly ContentControl _stage = new();
    private readonly Rectangle _dropFrame;
    private readonly Border _dropZone;
    private readonly ComboBox _level = new() { Width = 120, Margin = new Thickness(8, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _reveal = new() { Content = "Danach im Explorer zeigen", VerticalAlignment = VerticalAlignment.Center };

    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _closeRequested;

    // Fortschrittsanzeige
    private TextBlock? _busyTitle, _busyFile, _busyStats;
    private ProgressBar? _bar;
    private readonly Stopwatch _clock = new();

    public MainWindow(StartCommand start)
    {
        _start = start;
        Title = "HasiZip";
        Width = 560;
        Height = 440;
        MinWidth = 460;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush(Ui.Background);
        FontFamily = new FontFamily("Segoe UI");
        AllowDrop = true;
        TaskbarItemInfo = new TaskbarItemInfo();
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/hasizip.ico")); }
        catch { /* Icon optional */ }

        // Kopfzeile
        var header = new Border { Background = Ui.Brush(Ui.Navy), Padding = new Thickness(22, 14, 18, 14) };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = "HasiZip", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        titleStack.Children.Add(new TextBlock { Text = "Packen & Entpacken – schnell und einfach", FontSize = 12, Foreground = Ui.Brush(Ui.Blue) });
        headerGrid.Children.Add(titleStack);
        var about = new TextBlock { Text = "Über", Foreground = Ui.Brush(Ui.Blue), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        about.MouseLeftButtonUp += (_, _) => new AboutWindow { Owner = this }.ShowDialog();
        Grid.SetColumn(about, 1);
        headerGrid.Children.Add(about);
        header.Child = headerGrid;

        // Fußzeile mit Einstellungen
        foreach (var name in new[] { "Schnell", "Normal", "Maximal" }) _level.Items.Add(name);
        _level.SelectedIndex = (int)Settings.Current.Level;
        _level.SelectionChanged += (_, _) => { Settings.Current.Level = (PackLevel)_level.SelectedIndex; Settings.Current.Save(); };
        _reveal.IsChecked = Settings.Current.RevealWhenDone;
        _reveal.Click += (_, _) => { Settings.Current.RevealWhenDone = _reveal.IsChecked == true; Settings.Current.Save(); };
        var footer = new Border
        {
            Background = Brushes.White,
            BorderBrush = Ui.Brush(Ui.Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 10, 18, 10),
        };
        var footerPanel = new DockPanel { LastChildFill = false };
        footerPanel.Children.Add(new TextBlock { Text = "Kompression:", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush(Ui.Gray) });
        footerPanel.Children.Add(_level);
        footerPanel.Children.Add(_reveal);
        var site = new TextBlock { Text = "hasi-elektronic.de", Foreground = Ui.Brush(Ui.Gray), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        site.MouseLeftButtonUp += (_, _) => Shell("https://hasi-elektronic.de");
        DockPanel.SetDock(site, Dock.Right);
        footerPanel.Children.Add(site);
        footer.Child = footerPanel;

        // Ablagefläche
        _dropFrame = new Rectangle
        {
            Stroke = Ui.Brush(Ui.Blue),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 6, 4 },
            RadiusX = 14,
            RadiusY = 14,
        };
        _dropZone = new Border { CornerRadius = new CornerRadius(14), Background = Brushes.White, Padding = new Thickness(24) };
        _stage.VerticalAlignment = VerticalAlignment.Center;
        _stage.HorizontalAlignment = HorizontalAlignment.Stretch;
        _dropZone.Child = _stage;
        var dropGrid = new Grid { Margin = new Thickness(18) };
        dropGrid.Children.Add(_dropZone);
        dropGrid.Children.Add(_dropFrame);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(dropGrid);
        Content = root;

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => Highlight(false);
        Drop += OnDrop;
        Closing += OnClosing;
        Loaded += async (_, _) => await RunStartCommand();

        ShowIdle();
    }

    // ---------- Zustände ----------

    private void ShowIdle()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Glyph(Ui.GlyphPackage, Ui.Blue, 46));
        stack.Children.Add(Ui.Label("Dateien oder Ordner hierher ziehen", 18, Ui.Text, FontWeights.SemiBold));
        stack.Children.Add(WithMargin(Ui.Label("Ordner & Dateien → ZIP   ·   ZIP, 7z, RAR, TAR, GZ → entpacken", 12, Ui.Gray), 0, 4, 0, 20));
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var folder = Ui.Button("Ordner packen…", primary: true);
        folder.Click += async (_, _) => await PickFoldersAndPack();
        var files = Ui.Button("Dateien packen…");
        files.Click += async (_, _) => await PickFilesAndPack();
        var extract = Ui.Button("Entpacken…");
        extract.Click += async (_, _) => await PickArchivesAndExtract();
        buttons.Children.Add(folder);
        buttons.Children.Add(files);
        buttons.Children.Add(extract);
        stack.Children.Add(buttons);
        stack.Children.Add(WithMargin(Ui.Label("Tipp: Rechtsklick im Explorer → „Mit HasiZip packen“ / „Hier entpacken (HasiZip)“", 11, Ui.Gray), 0, 18, 0, 0));
        _stage.Content = stack;
        SetTaskbar(TaskbarItemProgressState.None, 0);
    }

    internal void ShowBusy(string title)
    {
        var stack = new StackPanel();
        _busyTitle = Ui.Label(title, 17, Ui.Text, FontWeights.SemiBold);
        _busyFile = Ui.Label(" ", 12, Ui.Gray);
        _busyFile.TextWrapping = TextWrapping.NoWrap;
        _busyFile.TextTrimming = TextTrimming.CharacterEllipsis;
        _bar = new ProgressBar
        {
            Height = 12,
            Minimum = 0,
            Maximum = 1,
            Margin = new Thickness(0, 14, 0, 10),
            Foreground = Ui.Brush(Ui.Blue),
            Background = Ui.Brush(Ui.Line),
            BorderThickness = new Thickness(0),
            IsIndeterminate = true,
        };
        _busyStats = Ui.Label(" ", 12, Ui.Gray);
        var cancel = Ui.Button("Abbrechen");
        cancel.Margin = new Thickness(0, 16, 0, 0);
        cancel.HorizontalAlignment = HorizontalAlignment.Center;
        cancel.Click += (_, _) => _cts?.Cancel();
        stack.Children.Add(_busyTitle);
        stack.Children.Add(WithMargin(_busyFile, 0, 4, 0, 0));
        stack.Children.Add(_bar);
        stack.Children.Add(_busyStats);
        stack.Children.Add(cancel);
        _stage.Content = stack;
        SetTaskbar(TaskbarItemProgressState.Indeterminate, 0);
        _clock.Restart();
    }

    internal void OnProgress(ArchiveProgress p)
    {
        if (_bar == null || _busyStats == null || _busyFile == null) return;
        _busyFile.Text = p.CurrentName ?? " ";
        var elapsed = _clock.Elapsed;
        var speed = elapsed.TotalSeconds > 0.2 ? p.DoneBytes / elapsed.TotalSeconds : 0;
        if (p.TotalBytes > 0)
        {
            _bar.IsIndeterminate = false;
            _bar.Value = p.Fraction;
            SetTaskbar(TaskbarItemProgressState.Normal, p.Fraction);
            var eta = speed > 0 ? TimeSpan.FromSeconds((p.TotalBytes - p.DoneBytes) / speed) : (TimeSpan?)null;
            _busyStats.Text = $"{Ui.Size(p.DoneBytes)} von {Ui.Size(p.TotalBytes)}"
                + (speed > 0 ? $"  ·  {Ui.Size((long)speed)}/s" : "")
                + (eta is TimeSpan e && elapsed.TotalSeconds > 1 ? $"  ·  noch {Ui.Duration(e)}" : "");
        }
        else
        {
            _busyStats.Text = Ui.Size(p.DoneBytes) + (speed > 0 ? $"  ·  {Ui.Size((long)speed)}/s" : "");
        }
    }

    internal void ShowDone(string title, string detail, string resultPath)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Glyph(Ui.GlyphCheck, Ui.Green, 44));
        stack.Children.Add(Ui.Label(title, 18, Ui.Text, FontWeights.SemiBold));
        stack.Children.Add(WithMargin(Ui.Label(detail, 12, Ui.Gray), 0, 6, 0, 20));
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var reveal = Ui.Button("Im Explorer zeigen", primary: true);
        reveal.Click += (_, _) => Reveal(resultPath);
        var open = Ui.Button("Öffnen");
        open.Click += (_, _) => Shell(resultPath);
        var again = Ui.Button("Weiter");
        again.Click += (_, _) => ShowIdle();
        buttons.Children.Add(reveal);
        buttons.Children.Add(open);
        buttons.Children.Add(again);
        stack.Children.Add(buttons);
        _stage.Content = stack;
        SetTaskbar(TaskbarItemProgressState.None, 0);
    }

    internal void ShowError(string message)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Glyph(Ui.GlyphError, Ui.Red, 44));
        stack.Children.Add(Ui.Label("Das hat nicht geklappt", 18, Ui.Text, FontWeights.SemiBold));
        stack.Children.Add(WithMargin(Ui.Label(message, 12, Ui.Gray), 0, 6, 0, 20));
        var ok = Ui.Button("OK", primary: true);
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.Click += (_, _) => ShowIdle();
        stack.Children.Add(ok);
        _stage.Content = stack;
        SetTaskbar(TaskbarItemProgressState.Error, 1);
    }

    // ---------- Aufträge ----------

    private async Task RunStartCommand()
    {
        if (_start.Paths.Count == 0) return;
        bool ok = _start.Mode switch
        {
            StartMode.Pack => await Pack(_start.Paths),
            StartMode.Extract => await Extract(_start.Paths, null),
            StartMode.ExtractTo => await ExtractToPicked(_start.Paths),
            _ => false,
        };
        if (ok && Settings.Current.CloseWhenDone && !_closeRequested)
        {
            await Task.Delay(900);
            if (!IsMouseOver) Close();
        }
    }

    private async Task<bool> Pack(IReadOnlyList<string> inputs)
    {
        string target;
        try { target = SmartNames.PackTarget(inputs); }
        catch (Exception ex) { ShowError(ex.Message); return false; }

        return await RunJob($"Packe „{System.IO.Path.GetFileName(target)}“", async (progress, token) =>
        {
            PackResult r;
            try
            {
                r = await ZipPacker.PackAsync(inputs, target, new PackOptions { Level = Settings.Current.Level }, progress, token);
            }
            catch (UnauthorizedAccessException)
            {
                // Schreibgeschützter Ordner (z. B. Netzlaufwerk, Programme): Ziel selbst wählen lassen
                var dialog = new SaveFileDialog
                {
                    FileName = System.IO.Path.GetFileName(target),
                    Filter = "ZIP-Archiv (*.zip)|*.zip",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Title = "Hier darf HasiZip nicht schreiben – wohin speichern?",
                };
                if (dialog.ShowDialog(this) != true) throw new OperationCanceledException();
                if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName); // Dialog hat das Überschreiben bestätigt
                r = await ZipPacker.PackAsync(inputs, dialog.FileName, new PackOptions { Level = Settings.Current.Level }, progress, token);
            }
            var saved = r.InputBytes > 0 ? 100 - r.OutputBytes * 100.0 / r.InputBytes : 0;
            var detail = $"{System.IO.Path.GetFileName(r.ZipPath)}  ·  {Ui.Count(r.Files, "Datei", "Dateien")}  ·  {Ui.Size(r.InputBytes)} → {Ui.Size(r.OutputBytes)}"
                + (saved >= 1 ? $" (−{saved:0} %)" : "");
            if (r.Skipped.Count > 0)
                detail += $"\n{Ui.Count(r.Skipped.Count, "Datei", "Dateien")} übersprungen (geöffnet oder gesperrt): {string.Join(", ", r.Skipped.Take(3).Select(System.IO.Path.GetFileName))}{(r.Skipped.Count > 3 ? " …" : "")}";
            return (r.ZipPath, $"Fertig in {Ui.Duration(r.Elapsed)}", detail);
        });
    }

    private async Task<bool> Extract(IReadOnlyList<string> archives, string? targetParent)
    {
        return await RunJob(archives.Count == 1 ? $"Entpacke „{System.IO.Path.GetFileName(archives[0])}“" : "Entpacke …", async (progress, token) =>
        {
            var total = Stopwatch.StartNew();
            ExtractResult? last = null;
            long bytes = 0;
            int files = 0;
            for (int i = 0; i < archives.Count; i++)
            {
                var archive = archives[i];
                if (_busyTitle != null)
                    _busyTitle.Text = archives.Count == 1 ? $"Entpacke „{System.IO.Path.GetFileName(archive)}“" : $"Entpacke {i + 1} von {archives.Count}: „{System.IO.Path.GetFileName(archive)}“";
                string? password = null;
                bool wrong = false;
                while (true)
                {
                    try
                    {
                        last = await Extractor.ExtractSmartAsync(archive, targetParent, new ExtractOptions { Password = password }, progress, token);
                        break;
                    }
                    catch (Exception ex) when (ex is PasswordRequiredException or WrongPasswordException)
                    {
                        wrong = ex is WrongPasswordException;
                        password = PasswordDialog.Ask(this, System.IO.Path.GetFileName(archive), wrong);
                        if (password == null) throw new OperationCanceledException();
                    }
                }
                bytes += last.Bytes;
                files += last.Files;
            }
            var output = archives.Count == 1 ? last!.OutputPath : System.IO.Path.GetDirectoryName(last!.OutputPath)!;
            var detail = $"{System.IO.Path.GetFileName(last.OutputPath)}  ·  {Ui.Count(files, "Datei", "Dateien")}  ·  {Ui.Size(bytes)}";
            if (archives.Count > 1) detail = $"{archives.Count} Archive  ·  {Ui.Count(files, "Datei", "Dateien")}  ·  {Ui.Size(bytes)}";
            return (output, $"Fertig in {Ui.Duration(total.Elapsed)}", detail);
        });
    }

    private async Task<bool> ExtractToPicked(IReadOnlyList<string> archives)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Wohin entpacken?",
            InitialDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(archives[0])),
        };
        if (dialog.ShowDialog(this) != true)
        {
            if (_start.Mode == StartMode.ExtractTo) Close();
            return false;
        }
        return await Extract(archives, dialog.FolderName);
    }

    /// <summary>Gemeinsamer Ablauf: Fortschritt, Abbrechen, Fehlertexte, Ergebnis anzeigen.</summary>
    private async Task<bool> RunJob(string title, Func<IProgress<ArchiveProgress>, CancellationToken, Task<(string Path, string Title, string Detail)>> job)
    {
        if (_busy) return false;
        _busy = true;
        _cts = new CancellationTokenSource();
        ShowBusy(title);
        var progress = new Progress<ArchiveProgress>(OnProgress);
        try
        {
            var (path, doneTitle, detail) = await job(progress, _cts.Token);
            ShowDone(doneTitle, detail, path);
            if (Settings.Current.RevealWhenDone && !_closeRequested) Reveal(path);
            return true;
        }
        catch (OperationCanceledException)
        {
            ShowIdle();
            return false;
        }
        catch (Exception ex)
        {
            ShowError(Describe(ex));
            return false;
        }
        finally
        {
            _busy = false;
            _cts.Dispose();
            _cts = null;
            if (_closeRequested) Close();
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnsupportedArchiveException => ex.Message,
        WrongPasswordException => "Das Passwort ist falsch.",
        InvalidDataException => ex.Message,
        UnauthorizedAccessException => "Keine Berechtigung: " + ex.Message,
        FileNotFoundException f => $"Nicht gefunden: {f.FileName}",
        IOException when ex.HResult == unchecked((int)0x80070070) => "Auf dem Laufwerk ist nicht genug Platz.",
        IOException => ex.Message,
        _ => ex.Message,
    };

    // ---------- Auswahl & Drag and Drop ----------

    private async Task PickFoldersAndPack()
    {
        var dialog = new OpenFolderDialog { Title = "Ordner zum Packen wählen", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await Pack(dialog.FolderNames);
    }

    private async Task PickFilesAndPack()
    {
        var dialog = new OpenFileDialog { Title = "Dateien zum Packen wählen", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await Pack(dialog.FileNames);
    }

    private async Task PickArchivesAndExtract()
    {
        var exts = string.Join(";", SmartNames.SupportedArchiveExtensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog { Title = "Archiv zum Entpacken wählen", Multiselect = true, Filter = $"Archive ({exts})|{exts}|Alle Dateien (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) await Extract(dialog.FileNames, null);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool ok = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        Highlight(ok);
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        Highlight(false);
        if (_busy || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        Activate();
        if (paths.All(SmartNames.LooksLikeArchive)) await Extract(paths, null);
        else await Pack(paths);
    }

    private void Highlight(bool on)
    {
        _dropZone.Background = on ? Ui.Brush(Color.FromRgb(0xEA, 0xF6, 0xFC)) : Brushes.White;
        _dropFrame.Stroke = Ui.Brush(on ? Ui.BlueDark : Ui.Blue);
        _dropFrame.StrokeThickness = on ? 3 : 2;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_busy) return;
        var answer = MessageBox.Show(this, "Der Vorgang läuft noch. Abbrechen und schließen?", "HasiZip", MessageBoxButton.YesNo, MessageBoxImage.Question);
        e.Cancel = true;
        if (answer == MessageBoxResult.Yes)
        {
            _closeRequested = true; // schließt in RunJob, sobald aufgeräumt ist
            _cts?.Cancel();
        }
    }

    // ---------- Hilfen ----------

    private void SetTaskbar(TaskbarItemProgressState state, double value)
    {
        TaskbarItemInfo.ProgressState = state;
        TaskbarItemInfo.ProgressValue = value;
    }

    private static T WithMargin<T>(T element, double l, double t, double r, double b) where T : FrameworkElement
    {
        element.Margin = new Thickness(l, t, r, b);
        return element;
    }

    private static void Reveal(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch { /* Explorer nicht verfügbar */ }
    }

    private static void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* keine Zuordnung */ }
    }
}
