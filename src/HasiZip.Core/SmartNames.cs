using System.Text.RegularExpressions;

namespace HasiZip.Core;

/// <summary>Ziel-Namen ohne Rückfragen: nichts wird überschrieben, bei Konflikt „Name (2)“.</summary>
public static class SmartNames
{
    private static readonly string[] MultiExtensions =
        { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tar.lz", ".tar.lzma" };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".tbz2", ".xz", ".txz", ".zst", ".lz", ".lzma",
    };

    /// <summary>Dateiendungen, die HasiZip entpacken kann (für Kontextmenü und Drag &amp; Drop).</summary>
    public static IReadOnlyCollection<string> SupportedArchiveExtensions => ArchiveExtensions;

    public static bool LooksLikeArchive(string path) =>
        File.Exists(path) && ArchiveExtensions.Contains(Path.GetExtension(path));

    /// <summary>„Projekt“ → „Projekt.zip“; mehrere Elemente → Name des Elternordners.</summary>
    public static string PackTarget(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0) throw new ArgumentException("Keine Quellen angegeben.", nameof(inputs));
        var first = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputs[0]));
        var parent = Path.GetDirectoryName(first) ?? first;
        string baseName;
        if (inputs.Count == 1)
        {
            baseName = Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first);
            if (string.IsNullOrEmpty(baseName)) baseName = Path.GetFileName(first);
        }
        else
        {
            baseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(parent));
        }
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Archiv";
        return Unique(Path.Combine(parent, SafePath.CleanSegment(baseName) + ".zip"), isDirectory: false);
    }

    /// <summary>Name des Archivs ohne Endung(en): „daten.tar.gz“ → „daten“, „x.part1.rar“ → „x“.</summary>
    public static string ArchiveBaseName(string archivePath)
    {
        var name = Path.GetFileName(archivePath);
        foreach (var ext in MultiExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && name.Length > ext.Length)
                return name[..^ext.Length];
        }
        var stem = Path.GetFileNameWithoutExtension(name);
        stem = Regex.Replace(stem, @"\.part0*1$", "", RegexOptions.IgnoreCase);
        return string.IsNullOrWhiteSpace(stem) ? "Archiv" : stem;
    }

    /// <summary>Freier Pfad: „Bericht.zip“ → „Bericht (2).zip“, Ordner „v1.2“ → „v1.2 (2)“.</summary>
    public static string Unique(string path, bool isDirectory)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileName(path);
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isDirectory ? "" : Path.GetExtension(name);
        for (int i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException($"Kein freier Name für {path} gefunden.");
    }
}
