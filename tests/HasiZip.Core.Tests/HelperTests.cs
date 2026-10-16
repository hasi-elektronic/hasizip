using Xunit;

namespace HasiZip.Core.Tests;

public class HelperTests
{
    [Fact]
    public void Crc_combine_matches_crc_of_concatenation()
    {
        var a = TempDir.Random(12345, 1);
        var b = TempDir.Random(54321, 2);
        uint combined = Crc32Util.Combine(Crc32Util.Compute(a), Crc32Util.Compute(b), b.Length);
        Assert.Equal(Crc32Util.Compute(a.Concat(b).ToArray()), combined);
        Assert.Equal(Crc32Util.Compute(a), Crc32Util.Combine(Crc32Util.Compute(a), 0, 0));
    }

    [Theory]
    [InlineData("ordner/datei.txt", "ordner/datei.txt")]
    [InlineData("ordner\\datei.txt", "ordner/datei.txt")]
    [InlineData("/absolut/x.txt", "absolut/x.txt")]
    [InlineData("./a/./b.txt", "a/b.txt")]
    [InlineData("C:\\Windows\\x.dll", "C_/Windows/x.dll")]
    [InlineData("a/b<c>d?.txt", "a/b_c_d_.txt")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("nul", "_nul")]
    [InlineData("punkt am ende.", "punkt am ende")]
    [InlineData("../raus.txt", null)]
    [InlineData("a/../../raus.txt", null)]
    [InlineData("", null)]
    [InlineData("///", null)]
    public void Entry_names_are_cleaned(string input, string? expected)
    {
        Assert.Equal(expected, SafePath.CleanRelative(input));
    }

    [Fact]
    public void Unique_names_never_overwrite()
    {
        using var t = new TempDir();
        t.File("Bericht.zip", "x");
        t.File("Bericht (2).zip", "x");
        Directory.CreateDirectory(t.Sub("v1.2"));
        Assert.Equal(t.Sub("Bericht (3).zip"), SmartNames.Unique(t.Sub("Bericht.zip"), false));
        Assert.Equal(t.Sub("v1.2 (2)"), SmartNames.Unique(t.Sub("v1.2"), true));
        Assert.Equal(t.Sub("frei.zip"), SmartNames.Unique(t.Sub("frei.zip"), false));
    }

    [Theory]
    [InlineData("daten.tar.gz", "daten")]
    [InlineData("Backup.2026.zip", "Backup.2026")]
    [InlineData("film.part1.rar", "film")]
    [InlineData("x.7z", "x")]
    public void Archive_base_name(string file, string expected)
    {
        Assert.Equal(expected, SmartNames.ArchiveBaseName(file));
    }

    [Fact]
    public void Pack_target_uses_folder_or_parent_name()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Sub("Kunde Müller"));
        var a = t.File("Kunde Müller/rechnung.pdf", "x");
        var b = t.File("Kunde Müller/angebot.pdf", "x");
        Assert.Equal(t.Sub("Kunde Müller.zip"), SmartNames.PackTarget(new[] { t.Sub("Kunde Müller") }));
        Assert.Equal(t.Sub("Kunde Müller", "rechnung.zip"), SmartNames.PackTarget(new[] { a }));
        Assert.Equal(t.Sub("Kunde Müller", "Kunde Müller.zip"), SmartNames.PackTarget(new[] { a, b }));
    }

    [Fact]
    public void Collector_gathers_paths_from_other_instances()
    {
        var channel = "test" + Guid.NewGuid().ToString("N")[..6];
        IReadOnlyList<string>? primary = null;
        var server = new Thread(() => primary = InstanceCollector.Collect(channel, new[] { "/a" }, TimeSpan.FromMilliseconds(800)));
        server.Start();
        Thread.Sleep(200);
        var results = new IReadOnlyList<string>?[3];
        var clients = Enumerable.Range(0, 3).Select(i => new Thread(() => results[i] = InstanceCollector.Collect(channel, new[] { $"/b{i}" }, TimeSpan.FromMilliseconds(800)))).ToList();
        clients.ForEach(c => c.Start());
        clients.ForEach(c => c.Join());
        server.Join();

        Assert.All(results, Assert.Null);
        Assert.NotNull(primary);
        Assert.Equal("/a", primary![0]);
        Assert.Equal(new[] { "/a", "/b0", "/b1", "/b2" }, primary.OrderBy(x => x).ToArray());
    }
}
