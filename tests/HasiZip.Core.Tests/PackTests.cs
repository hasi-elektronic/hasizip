using System.IO.Compression;
using Xunit;

namespace HasiZip.Core.Tests;

public class PackTests
{
    private static Dictionary<string, byte[]> ReadAll(string zipPath, out List<string> directories)
    {
        // Gegenprobe mit dem unabhängigen Leser aus .NET
        var files = new Dictionary<string, byte[]>();
        directories = new List<string>();
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/')) { directories.Add(e.FullName); continue; }
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            files[e.FullName] = ms.ToArray();
            Assert.Equal(e.Crc32, Crc32Util.Compute(ms.ToArray()));
        }
        return files;
    }

    [Theory]
    [InlineData(PackLevel.Fast)]
    [InlineData(PackLevel.Normal)]
    [InlineData(PackLevel.Max)]
    public async Task Folder_roundtrips_with_all_levels(PackLevel level)
    {
        using var t = new TempDir();
        var text = TempDir.TextLike(300_000);
        var rnd = TempDir.Random(200_000);
        t.File("Projekt/a.txt", text);
        t.File("Projekt/sub/b.bin", rnd);
        t.File("Projekt/leer.txt", Array.Empty<byte>());
        t.File("Projekt/Größe & Ölwechsel ğüşİ.txt", "Umlaute");
        Directory.CreateDirectory(t.Sub("Projekt", "nur ein Ordner"));

        var zip = t.Sub("out.zip");
        var r = await ZipPacker.PackAsync(new[] { t.Sub("Projekt") }, zip, new PackOptions { Level = level });

        Assert.Equal(4, r.Files);
        Assert.Equal(3, r.Folders);
        Assert.True(r.OutputBytes < r.InputBytes);
        Assert.False(File.Exists(zip + ".part"));

        var files = ReadAll(zip, out var dirs);
        Assert.Equal(text, files["Projekt/a.txt"]);
        Assert.Equal(rnd, files["Projekt/sub/b.bin"]);
        Assert.Empty(files["Projekt/leer.txt"]);
        Assert.Equal("Umlaute"u8.ToArray(), files["Projekt/Größe & Ölwechsel ğüşİ.txt"]);
        Assert.Contains("Projekt/nur ein Ordner/", dirs);
        Assert.Contains("Projekt/", dirs);
    }

    [Theory]
    [InlineData(1, 64 * 1024)]   // ein Kern, viele kleine Blöcke
    [InlineData(4, 64 * 1024)]
    [InlineData(0, 1 << 20)]
    public async Task Large_file_is_split_into_parallel_blocks_and_stays_valid(int threads, int chunk)
    {
        using var t = new TempDir();
        // gemischt: komprimierbar, Zufall (Stored-Blöcke), wieder komprimierbar
        var data = TempDir.TextLike(700_000).Concat(TempDir.Random(400_000)).Concat(TempDir.TextLike(333_333, 7)).ToArray();
        var src = t.File("gross.dat", data);
        var zip = t.Sub("gross.zip");

        await ZipPacker.PackAsync(new[] { src }, zip, new PackOptions { Threads = threads, ChunkSize = chunk });

        var files = ReadAll(zip, out _);
        Assert.Equal(data, files["gross.dat"]);
    }

    [Fact]
    public async Task Incompressible_data_does_not_grow_noticeably()
    {
        using var t = new TempDir();
        var data = TempDir.Random(3_000_000);
        var src = t.File("zufall.bin", data);
        var zip = t.Sub("z.zip");
        var r = await ZipPacker.PackAsync(new[] { src }, zip);
        Assert.True(r.OutputBytes < data.Length + 1024, $"{r.OutputBytes} vs {data.Length}");
        Assert.Equal(data, ReadAll(zip, out _)["zufall.bin"]);
    }

    [Fact]
    public async Task Already_compressed_formats_are_stored()
    {
        using var t = new TempDir();
        var src = t.File("foto.jpg", TempDir.TextLike(100_000)); // Inhalt egal, Endung zählt
        var zip = t.Sub("f.zip");
        await ZipPacker.PackAsync(new[] { src }, zip);
        using var z = ZipFile.OpenRead(zip);
        var e = z.GetEntry("foto.jpg")!;
        Assert.Equal(e.Length, e.CompressedLength);
    }

    [Fact]
    public async Task Zip64_structures_are_readable()
    {
        using var t = new TempDir();
        var a = t.File("x/a.txt", TempDir.TextLike(2_500_000));
        t.File("x/b.txt", "klein");
        var zip = t.Sub("z64.zip");
        await ZipPacker.PackAsync(new[] { t.Sub("x") }, zip, new PackOptions { ForceZip64 = true, ChunkSize = 1 << 20 });
        var files = ReadAll(zip, out _);
        Assert.Equal(File.ReadAllBytes(a), files["x/a.txt"]);
        Assert.Equal("klein"u8.ToArray(), files["x/b.txt"]);
    }

    [Fact]
    public async Task Multiple_inputs_with_same_name_get_unique_entries()
    {
        using var t = new TempDir();
        var a = t.File("eins/bericht.txt", "A");
        var b = t.File("zwei/bericht.txt", "B");
        var zip = t.Sub("m.zip");
        await ZipPacker.PackAsync(new[] { a, b }, zip);
        var files = ReadAll(zip, out _);
        Assert.Equal("A"u8.ToArray(), files["bericht.txt"]);
        Assert.Equal("B"u8.ToArray(), files["bericht (2).txt"]);
    }

    [Fact]
    public async Task Output_inside_the_source_folder_is_not_packed_into_itself()
    {
        using var t = new TempDir();
        t.File("src/a.txt", "a");
        var zip = t.Sub("src", "selbst.zip");
        await ZipPacker.PackAsync(new[] { t.Sub("src") }, zip);
        var files = ReadAll(zip, out _);
        Assert.Single(files);
    }

    [Fact]
    public async Task Cancel_deletes_partial_file_and_releases_handles()
    {
        using var t = new TempDir();
        var src = t.File("gross.dat", TempDir.TextLike(20_000_000));
        var zip = t.Sub("abbruch.zip");
        using var cts = new CancellationTokenSource();
        var progress = new Progress<ArchiveProgress>(p => { if (p.DoneBytes > 0) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ZipPacker.PackAsync(new[] { src }, zip, new PackOptions { ChunkSize = 64 * 1024, Level = PackLevel.Max }, progress, cts.Token));
        Assert.False(File.Exists(zip));
        Assert.False(File.Exists(zip + ".part"));
        File.Delete(src); // Handle ist freigegeben
    }

    [Fact]
    public async Task Missing_input_throws_clear_error()
    {
        using var t = new TempDir();
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => ZipPacker.PackAsync(new[] { t.Sub("gibtsnicht") }, t.Sub("x.zip")));
        Assert.Contains("Nicht gefunden", ex.Message);
    }

    [Fact]
    public void Stored_blocks_are_valid_deflate()
    {
        var data = TempDir.Random(200_000);
        var (buf, len) = ZipPacker.StoredBlocks(data, data.Length, last: true);
        using var ds = new DeflateStream(new MemoryStream(buf, 0, len), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        ds.CopyTo(ms);
        Assert.Equal(data, ms.ToArray());
    }

    [Fact]
    public void Dos_time_is_encoded_like_pkzip()
    {
        var v = ZipStreamWriter.ToDosTime(new DateTime(2026, 10, 16, 13, 45, 58));
        Assert.Equal(((2026 - 1980) << 9) | (10 << 5) | 16, (int)(v >> 16));
        Assert.Equal((13 << 11) | (45 << 5) | 29, (int)(v & 0xFFFF));
        Assert.Equal(ZipStreamWriter.ToDosTime(new DateTime(1980, 1, 1)), ZipStreamWriter.ToDosTime(new DateTime(1970, 1, 1)));
    }
}
