using System.IO.Compression;
using Xunit;

namespace HasiZip.Core.Tests;

public class ExtractTests
{
    [Fact]
    public async Task Own_zip_extracts_identically_in_parallel()
    {
        using var t = new TempDir();
        var files = new Dictionary<string, byte[]>();
        for (int i = 0; i < 40; i++)
        {
            var data = i % 3 == 0 ? TempDir.Random(10_000 + i * 997, i) : TempDir.TextLike(5_000 + i * 31_337, i);
            files[$"Daten/ordner{i % 5}/datei{i}.bin"] = data;
            t.File($"src/Daten/ordner{i % 5}/datei{i}.bin", data);
        }
        var zip = t.Sub("d.zip");
        await ZipPacker.PackAsync(new[] { t.Sub("src", "Daten") }, zip);

        var dest = t.Sub("ziel");
        var r = await Extractor.ExtractToAsync(zip, dest, new ExtractOptions { Threads = 4 });
        Assert.Equal(40, r.Files);
        foreach (var (rel, data) in files)
            Assert.Equal(data, File.ReadAllBytes(Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task Smart_extract_single_root_folder_lands_next_to_archive()
    {
        using var t = new TempDir();
        t.File("src/Projekt/a.txt", "a");
        var zip = t.Sub("ablage", "Irgendwas.zip");
        Directory.CreateDirectory(t.Sub("ablage"));
        await ZipPacker.PackAsync(new[] { t.Sub("src", "Projekt") }, zip);

        var r1 = await Extractor.ExtractSmartAsync(zip);
        Assert.Equal(t.Sub("ablage", "Projekt"), r1.OutputPath);
        Assert.True(File.Exists(t.Sub("ablage", "Projekt", "a.txt")));

        // Zweites Mal: nichts überschreiben
        var r2 = await Extractor.ExtractSmartAsync(zip);
        Assert.Equal(t.Sub("ablage", "Projekt (2)"), r2.OutputPath);
        Assert.Empty(Directory.GetDirectories(t.Sub("ablage"), ".hasizip-*"));
    }

    [Fact]
    public async Task Smart_extract_many_items_creates_folder_named_like_archive()
    {
        using var t = new TempDir();
        var a = t.File("src/a.txt", "a");
        var b = t.File("src/b.txt", "b");
        var zip = t.Sub("Rechnungen 2026.zip");
        await ZipPacker.PackAsync(new[] { a, b }, zip);
        var r = await Extractor.ExtractSmartAsync(zip);
        Assert.Equal(t.Sub("Rechnungen 2026"), r.OutputPath);
        Assert.True(File.Exists(t.Sub("Rechnungen 2026", "b.txt")));
    }

    [Fact]
    public async Task Zip_slip_entries_never_leave_the_destination()
    {
        using var t = new TempDir();
        var zip = t.Sub("boese.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "../../ausbruch.txt", "/etc/absolut.txt", "ok/../../../tief.txt", "C:\\Windows\\win.txt", "gut/datei.txt" })
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write("x");
            }
        }
        var dest = t.Sub("ziel", "innen");
        await Extractor.ExtractToAsync(zip, dest);

        Assert.False(File.Exists(t.Sub("ausbruch.txt")));
        Assert.False(File.Exists(t.Sub("ziel", "ausbruch.txt")));
        Assert.False(File.Exists(t.Sub("tief.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "gut", "datei.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "etc", "absolut.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "C_", "Windows", "win.txt")));
        foreach (var f in Directory.GetFiles(t.Path, "*", SearchOption.AllDirectories))
            Assert.True(f.StartsWith(dest) || f == zip, f);
    }

    [Fact]
    public async Task Corrupted_data_is_detected()
    {
        using var t = new TempDir();
        var src = t.File("a.txt", TempDir.TextLike(200_000));
        var zip = t.Sub("kaputt.zip");
        await ZipPacker.PackAsync(new[] { src }, zip);
        var bytes = File.ReadAllBytes(zip);
        bytes[2000] ^= 0x55; // mitten in den Deflate-Daten
        File.WriteAllBytes(zip, bytes);
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => Extractor.ExtractToAsync(zip, t.Sub("ziel")));
    }

    [Theory]
    [InlineData("plain.7z")]
    [InlineData("plain.tar.gz")]
    [InlineData("bzip2.zip")]
    public async Task Other_formats_extract(string fixture)
    {
        using var t = new TempDir();
        var r = await Extractor.ExtractToAsync(TempDir.Fixture(fixture), t.Sub("ziel"));
        Assert.Equal("Hallo von Hasi Elektronic\n", File.ReadAllText(t.Sub("ziel", "Projekt", "liesmich.txt")));
        Assert.Equal("Geheim: 07042/16391\n", File.ReadAllText(t.Sub("ziel", "Projekt", "Unterordner", "notiz.txt")));
        Assert.True(r.Files >= 2);
    }

    [Theory]
    [InlineData("aes.zip")]
    [InlineData("zipcrypto.zip")]
    [InlineData("secret.7z")]
    public async Task Encrypted_archives_need_the_right_password(string fixture)
    {
        using var t = new TempDir();
        var archive = TempDir.Fixture(fixture);

        await Assert.ThrowsAsync<PasswordRequiredException>(() => Extractor.ExtractToAsync(archive, t.Sub("a")));
        await Assert.ThrowsAsync<WrongPasswordException>(() => Extractor.ExtractToAsync(archive, t.Sub("b"), new ExtractOptions { Password = "falsch" }));

        await Extractor.ExtractToAsync(archive, t.Sub("c"), new ExtractOptions { Password = "hasi123" });
        Assert.Equal("Geheim: 07042/16391\n", File.ReadAllText(t.Sub("c", "Projekt", "Unterordner", "notiz.txt")));
    }

    [Fact]
    public async Task Smart_extract_of_encrypted_archive_without_password_leaves_no_temp_folder()
    {
        using var t = new TempDir();
        var copy = t.Sub("aes.zip");
        File.Copy(TempDir.Fixture("aes.zip"), copy);
        await Assert.ThrowsAsync<PasswordRequiredException>(() => Extractor.ExtractSmartAsync(copy));
        Assert.Single(Directory.GetFileSystemEntries(t.Path));
    }

    [Fact]
    public async Task Garbage_file_reports_unsupported_format()
    {
        using var t = new TempDir();
        var f = t.File("kein-archiv.zip", TempDir.Random(5000));
        await Assert.ThrowsAsync<UnsupportedArchiveException>(() => Extractor.ExtractToAsync(f, t.Sub("ziel")));
    }
}
