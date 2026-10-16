namespace HasiZip.Core;

/// <summary>
/// Wandelt Eintragsnamen aus Archiven in sichere Zielpfade um (Schutz gegen „Zip Slip“).
/// Es gelten immer die Windows-Regeln, damit Verhalten und Tests auf allen Systemen gleich sind.
/// </summary>
public static class SafePath
{
    private static readonly char[] InvalidChars = { '<', '>', ':', '"', '|', '?', '*' };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Relativer, bereinigter Pfad (mit '/' getrennt) oder <c>null</c>, wenn der Eintrag
    /// übersprungen werden muss (leer oder versucht mit „..“ aus dem Zielordner auszubrechen).
    /// </summary>
    public static string? CleanRelative(string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;
        var parts = entryName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var clean = new List<string>(parts.Length);
        foreach (var raw in parts)
        {
            if (raw == ".") continue;
            if (raw == "..") return null;
            clean.Add(CleanSegment(raw));
        }
        return clean.Count == 0 ? null : string.Join('/', clean);
    }

    /// <summary>Absoluter Zielpfad innerhalb von <paramref name="destinationRoot"/> oder <c>null</c>.</summary>
    public static string? Resolve(string destinationRoot, string? entryName)
    {
        var rel = CleanRelative(entryName);
        if (rel == null) return null;
        var root = Path.GetFullPath(destinationRoot);
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSep = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, PathComparison) ? full : null;
    }

    internal static string CleanSegment(string segment)
    {
        var chars = segment.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] < 0x20 || Array.IndexOf(InvalidChars, chars[i]) >= 0) chars[i] = '_';
        }
        var s = new string(chars).TrimEnd('.', ' ');
        if (s.Length == 0) return "_";
        var stem = s;
        var dot = s.IndexOf('.');
        if (dot >= 0) stem = s[..dot];
        if (ReservedNames.Contains(stem.TrimEnd(' '))) s = "_" + s;
        return s;
    }
}
