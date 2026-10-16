using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HasiZip.Core;

namespace HasiZip.App;

/// <summary>
/// HasiZip.exe --selftest &lt;ordner&gt;: rendert alle Fensterzustände als PNG und macht einen
/// echten Pack-/Entpack-Durchlauf – ohne Maus, für CI (windows-latest) und schnelle Sichtprüfung.
/// </summary>
internal static class SelfTest
{
    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var window = new MainWindow(new StartCommand(StartMode.Window, Array.Empty<string>()));
        Render(window, Path.Combine(outDir, "1-start.png"));

        window.ShowBusy("Packe „Kunde Müller.zip“");
        window.OnProgress(new ArchiveProgress(412L << 20, 1300L << 20, 812, 2400, "Kunde Müller/Rechnungen/2026/RE-2026-0815.pdf"));
        Render(window, Path.Combine(outDir, "2-fortschritt.png"));

        window.ShowDone("Fertig in 1,8 s", "Kunde Müller.zip  ·  2.400 Dateien  ·  1,3 GB → 512 MB (−61 %)", outDir);
        Render(window, Path.Combine(outDir, "3-fertig.png"));

        window.ShowError("Das Archiv ist beschädigt oder wird nicht unterstützt.");
        Render(window, Path.Combine(outDir, "4-fehler.png"));

        // Echter Durchlauf mit dem Kern
        var work = Path.Combine(outDir, "roundtrip");
        Directory.CreateDirectory(Path.Combine(work, "src", "Ordner mit Ümlaut"));
        File.WriteAllText(Path.Combine(work, "src", "Ordner mit Ümlaut", "test.txt"), string.Concat(Enumerable.Repeat("Hasi Elektronic ", 50_000)));
        var zip = Path.Combine(work, "test.zip");
        var packed = ZipPacker.PackAsync(new[] { Path.Combine(work, "src") }, zip).GetAwaiter().GetResult();
        var extracted = Extractor.ExtractToAsync(zip, Path.Combine(work, "out")).GetAwaiter().GetResult();
        var same = File.ReadAllText(Path.Combine(work, "out", "src", "Ordner mit Ümlaut", "test.txt"))
                   == File.ReadAllText(Path.Combine(work, "src", "Ordner mit Ümlaut", "test.txt"));
        Console.WriteLine($"Roundtrip: {packed.InputBytes} → {packed.OutputBytes} Bytes, {extracted.Files} Datei(en), identisch: {same}");
        Directory.Delete(work, true);
        return same ? 0 : 1;
    }

    private static void Render(Window window, string file)
    {
        var content = (FrameworkElement)window.Content;
        var size = new Size(window.Width, window.Height - 32);
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        var dpi = 1.5;
        var bmp = new RenderTargetBitmap((int)(size.Width * dpi), (int)(size.Height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(size));
        }
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        encoder.Save(fs);
        Console.WriteLine(file);
    }
}
