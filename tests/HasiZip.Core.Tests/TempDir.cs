namespace HasiZip.Core.Tests;

/// <summary>Eigener Temp-Ordner pro Test, wird danach gelöscht.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hasizip-test-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public string File(string relative, byte[] content)
    {
        var full = Sub(relative.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public string File(string relative, string content) => File(relative, System.Text.Encoding.UTF8.GetBytes(content));

    public static string Fixture(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Gut komprimierbare, aber nicht triviale Daten.</summary>
    public static byte[] TextLike(int size, int seed = 1)
    {
        var rnd = new Random(seed);
        string[] words = { "Hasi", "Elektronic", "Vaihingen", "Enz", "Datenrettung", "Netzwerk", "Firewall", "Backup", "Server", "Windows" };
        var sb = new System.Text.StringBuilder(size + 32);
        while (sb.Length < size) sb.Append(words[rnd.Next(words.Length)]).Append(rnd.Next(1000)).Append(rnd.Next(8) == 0 ? '\n' : ' ');
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString(0, size));
    }

    public static byte[] Random(int size, int seed = 2)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { /* egal */ }
    }
}
