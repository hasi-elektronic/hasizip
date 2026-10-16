using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace HasiZip.Core;

public enum PackLevel
{
    /// <summary>Deflate Stufe 1 – maximale Geschwindigkeit.</summary>
    Fast,
    /// <summary>Deflate Stufe 6 – Standard, wie Windows/7-Zip „Normal“.</summary>
    Normal,
    /// <summary>Deflate Stufe 9 – kleinste ZIP-Datei.</summary>
    Max,
}

public sealed class PackOptions
{
    public PackLevel Level { get; init; } = PackLevel.Normal;
    /// <summary>0 = alle Prozessorkerne.</summary>
    public int Threads { get; init; }
    /// <summary>Größe der unabhängig komprimierten Blöcke (wie pigz). 1 MiB kostet &lt; 1 % Kompression.</summary>
    public int ChunkSize { get; init; } = 1 << 20;
    /// <summary>Nur für Tests: ZIP64-Strukturen auch für kleine Archive schreiben.</summary>
    internal bool ForceZip64 { get; init; }
}

public sealed record PackResult(
    string ZipPath, long InputBytes, long OutputBytes, int Files, int Folders,
    IReadOnlyList<string> Skipped, TimeSpan Elapsed);

/// <summary>
/// Packt Dateien und Ordner in eine Standard-ZIP-Datei – auf allen Kernen gleichzeitig.
/// Jede Datei wird in 1-MiB-Blöcke zerlegt, die Blöcke werden parallel mit Deflate komprimiert
/// und in der richtigen Reihenfolge zu einem gültigen Deflate-Strom aneinandergehängt
/// (Sync-Flush am Blockende, Prinzip wie pigz). Damit ist auch eine einzelne große Datei parallel.
/// Bereits komprimierte Formate (JPG, MP4, ZIP …) werden nur gespeichert, nicht erneut komprimiert.
/// </summary>
public static class ZipPacker
{
    private static readonly HashSet<string> StoreExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".avif", ".jxl",
        ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv", ".3gp",
        ".mp3", ".aac", ".m4a", ".ogg", ".opus", ".flac", ".wma",
        ".zip", ".7z", ".rar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".lz4", ".lzma", ".cab",
        ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".epub", ".jar", ".apk", ".aab", ".nupkg", ".vsix",
    };

    public static Task<PackResult> PackAsync(
        IReadOnlyList<string> inputs, string zipPath, PackOptions? options = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new PackOptions();
        return Task.Run(() => PackCoreAsync(inputs, zipPath, options, progress, cancellationToken), cancellationToken);
    }

    private static async Task<PackResult> PackCoreAsync(
        IReadOnlyList<string> inputs, string zipPath, PackOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        zipPath = Path.GetFullPath(zipPath);
        var partPath = zipPath + ".part";
        var items = Scan(inputs, new[] { zipPath, partPath });
        long totalBytes = 0;
        int fileCount = 0, folderCount = 0;
        foreach (var it in items)
        {
            if (it.IsDirectory) folderCount++;
            else { fileCount++; totalBytes += it.Length; }
        }

        var tracker = new ProgressTracker(progress, totalBytes, fileCount);
        tracker.Report(true);

        int threads = options.Threads > 0 ? options.Threads : Environment.ProcessorCount;
        int chunkSize = Math.Max(64 * 1024, options.ChunkSize);
        var level = options.Level switch
        {
            PackLevel.Fast => CompressionLevel.Fastest,
            PackLevel.Max => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };

        var skipped = new SkipLog();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;
        var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, threads).ConcurrentScheduler;
        var slots = new SemaphoreSlim(Math.Max(4, threads * 3));
        var channel = Channel.CreateUnbounded<Pending>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        var output = new FileStream(partPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
        var producer = Task.Run(() => Produce(items, chunkSize, level, scheduler, slots, channel.Writer, skipped, tracker, token));
        bool success = false;
        try
        {
            var zip = new ZipStreamWriter(output, options.ForceZip64);
            ZipStreamWriter.Entry? current = null;
            uint crc = 0;
            long compressed = 0;

            await foreach (var p in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                var job = p.Job;
                var item = job.Item;
                if (p.Chunk == null)
                {
                    // Ordner oder leere Datei
                    zip.BeginEntry(item.EntryName, item.IsDirectory, ZipStreamWriter.MethodStored, item.Modified, 0, 0, 0);
                    if (!item.IsDirectory) tracker.FileDone(item.EntryName);
                    continue;
                }

                var r = await p.Chunk.ConfigureAwait(false);
                try
                {
                    if (p.Index == 0)
                    {
                        if (job.ChunkCount == 1)
                            zip.BeginEntry(item.EntryName, false, job.Method, item.Modified, job.Length, r.Crc, r.Length);
                        else
                            current = zip.BeginDeferredEntry(item.EntryName, job.Method, item.Modified, job.Length);
                        crc = r.Crc;
                        compressed = 0;
                    }
                    else
                    {
                        crc = Crc32Util.Combine(crc, r.Crc, r.RawLength);
                    }
                    zip.WriteData(r.Data.AsSpan(0, r.Length));
                    compressed += r.Length;
                }
                finally
                {
                    r.Release();
                    slots.Release();
                }
                tracker.AddBytes(r.RawLength, item.EntryName);

                if (p.Index == job.ChunkCount - 1)
                {
                    if (job.ChunkCount > 1) zip.Patch(current!, crc, compressed);
                    current = null;
                    tracker.FileDone(item.EntryName);
                }
            }
            await producer.ConfigureAwait(false);

            zip.Finish();
            output.Flush(true);
            long outputBytes = output.Length;
            output.Dispose();
            File.Move(partPath, zipPath, overwrite: false);
            success = true;
            tracker.Report(true);
            // Producer ist fertig (await oben), die SkipLog-Werte sind damit sichtbar
            return new PackResult(zipPath, totalBytes - skipped.Bytes + skipped.SizeDelta, outputBytes, fileCount - skipped.Paths.Count, folderCount, skipped.Paths, clock.Elapsed);
        }
        finally
        {
            if (!success)
            {
                cts.Cancel();
                try { await producer.ConfigureAwait(false); } catch { /* Folgefehler */ }
                while (channel.Reader.TryRead(out var rest))
                {
                    if (rest.Chunk == null) continue;
                    try { (await rest.Chunk.ConfigureAwait(false)).Release(); } catch { /* bereits gemeldet */ }
                }
                output.Dispose();
                try { File.Delete(partPath); } catch { /* best effort */ }
            }
        }
    }

    private static async Task Produce(
        List<SourceItem> items, int chunkSize, CompressionLevel level, TaskScheduler scheduler,
        SemaphoreSlim slots, ChannelWriter<Pending> writer, SkipLog skipped, ProgressTracker tracker, CancellationToken token)
    {
        try
        {
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.IsDirectory || item.Length == 0)
                {
                    writer.TryWrite(new Pending(new FileJob(item), -1, null));
                    continue;
                }

                SafeFileHandle handle;
                long length;
                try
                {
                    handle = File.OpenHandle(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    length = RandomAccess.GetLength(handle);
                    if (length != item.Length)
                    {
                        // Datei hat sich seit dem Einlesen der Ordner geändert
                        skipped.SizeDelta += length - item.Length;
                        tracker.TotalBytes += length - item.Length;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gesperrte Datei (z. B. geöffnete Outlook-PST): überspringen statt abbrechen
                    skipped.Paths.Add(item.FullPath);
                    skipped.Bytes += item.Length;
                    tracker.TotalBytes -= item.Length;
                    tracker.TotalFiles--;
                    continue;
                }

                var job = new FileJob(item)
                {
                    Handle = handle,
                    Length = length,
                    ChunkCount = length == 0 ? 0 : (int)((length + chunkSize - 1) / chunkSize),
                    Method = StoreExtensions.Contains(Path.GetExtension(item.FullPath)) ? ZipStreamWriter.MethodStored : ZipStreamWriter.MethodDeflate,
                };
                try
                {
                    if (job.ChunkCount == 0)
                    {
                        writer.TryWrite(new Pending(job, -1, null));
                        continue;
                    }
                    for (int i = 0; i < job.ChunkCount; i++)
                    {
                        await slots.WaitAsync(token).ConfigureAwait(false);
                        job.AddRef();
                        int index = i;
                        // Kein Token an StartNew: jeder Task muss laufen, damit das Datei-Handle sicher geschlossen wird.
                        var task = Task.Factory.StartNew(
                            () => CompressChunk(job, index, chunkSize, level, token),
                            CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler);
                        writer.TryWrite(new Pending(job, index, task));
                    }
                }
                finally
                {
                    job.ReleaseRef();
                }
            }
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            throw;
        }
    }

    private static ChunkResult CompressChunk(FileJob job, int index, int chunkSize, CompressionLevel level, CancellationToken token)
    {
        byte[]? raw = null;
        bool handleReleased = false;
        try
        {
            token.ThrowIfCancellationRequested();
            long offset = (long)index * chunkSize;
            int len = (int)Math.Min(chunkSize, job.Length - offset);
            raw = ArrayPool<byte>.Shared.Rent(len);
            int read = 0;
            while (read < len)
            {
                int n = RandomAccess.Read(job.Handle!, raw.AsSpan(read, len - read), offset + read);
                if (n == 0) throw new IOException($"Datei wurde während des Packens verändert: {job.Item.FullPath}");
                read += n;
            }
            job.ReleaseRef();
            handleReleased = true;

            uint crc = Crc32Util.Compute(raw.AsSpan(0, len));
            if (job.Method == ZipStreamWriter.MethodStored)
            {
                var stored = new ChunkResult(raw, len, crc, len);
                raw = null; // gehört jetzt dem Ergebnis
                return stored;
            }
            bool last = index == job.ChunkCount - 1;
            var (data, length) = DeflateBlock(raw, len, last, level);
            return new ChunkResult(data, length, crc, len);
        }
        catch
        {
            if (!handleReleased) job.ReleaseRef();
            throw;
        }
        finally
        {
            if (raw != null) ArrayPool<byte>.Shared.Return(raw);
        }
    }

    /// <summary>
    /// Komprimiert einen Block zu einem Stück Deflate-Strom. Nicht-letzte Blöcke enden mit einem
    /// Sync-Flush (Byte-Grenze, ohne BFINAL), sodass die Stücke direkt aneinandergehängt werden können.
    /// Wird der Block nicht kleiner, entstehen „stored“-Deflate-Blöcke (max. 5 Byte je 64 KiB Overhead).
    /// </summary>
    internal static (byte[] Data, int Length) DeflateBlock(byte[] raw, int len, bool last, CompressionLevel level)
    {
        if (LooksIncompressible(raw, len)) return StoredBlocks(raw, len, last);

        var outBuf = ArrayPool<byte>.Shared.Rent(len);
        var sink = new BoundedSink(outBuf, len);
        using (var ds = new DeflateStream(sink, level, leaveOpen: true))
        {
            ds.Write(raw, 0, len);
            if (!last)
            {
                ds.Flush(); // Z_SYNC_FLUSH
                sink.Freeze(); // was Dispose noch anhängt (Final-Block), wird verworfen
            }
        }
        if (!sink.Overflowed && sink.Length < len) return (outBuf, (int)sink.Length);

        ArrayPool<byte>.Shared.Return(outBuf);
        return StoredBlocks(raw, len, last);
    }

    /// <summary>
    /// Stichprobe (2 × 16 KiB, Stufe „Fastest“): spart sie keine 3 %, ist der Block verschlüsselt
    /// oder schon komprimiert (Backups, Container, Zufallsdaten) und wird nur gespeichert.
    /// </summary>
    internal static bool LooksIncompressible(byte[] raw, int len)
    {
        const int sample = 16 * 1024;
        if (len < 8 * sample) return false;
        var buf = ArrayPool<byte>.Shared.Rent(2 * sample);
        try
        {
            var sink = new BoundedSink(buf, (int)(2 * sample * 0.97));
            using (var ds = new DeflateStream(sink, CompressionLevel.Fastest, leaveOpen: true))
            {
                ds.Write(raw, 0, sample);
                ds.Write(raw, len / 2, sample);
            }
            return sink.Overflowed;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    internal static (byte[] Data, int Length) StoredBlocks(byte[] raw, int len, bool last)
    {
        int blocks = Math.Max(1, (len + 65534) / 65535);
        var buf = ArrayPool<byte>.Shared.Rent(len + blocks * 5);
        int pos = 0, src = 0;
        for (int b = 0; b < blocks; b++)
        {
            int n = Math.Min(65535, len - src);
            buf[pos++] = (byte)(last && b == blocks - 1 ? 1 : 0); // BFINAL, BTYPE=00
            buf[pos++] = (byte)n;
            buf[pos++] = (byte)(n >> 8);
            buf[pos++] = (byte)~n;
            buf[pos++] = (byte)(~n >> 8);
            Buffer.BlockCopy(raw, src, buf, pos, n);
            pos += n;
            src += n;
        }
        return (buf, pos);
    }

    /// <summary>Schreibziel mit fester Kapazität; läuft es über, lohnt sich Deflate für den Block nicht.</summary>
    private sealed class BoundedSink : Stream
    {
        private readonly byte[] _buf;
        private readonly int _capacity;
        private int _len;
        private bool _frozen;

        public BoundedSink(byte[] buf, int capacity) { _buf = buf; _capacity = capacity; }

        public bool Overflowed { get; private set; }
        public void Freeze() => _frozen = true;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_frozen || Overflowed) return;
            if (_len + buffer.Length > _capacity) { Overflowed = true; return; }
            buffer.CopyTo(_buf.AsSpan(_len));
            _len += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void WriteByte(byte value) => Write(new ReadOnlySpan<byte>(in value));
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _len;
        public override long Position { get => _len; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // ---------- Quellen einlesen ----------

    internal sealed record SourceItem(string FullPath, string EntryName, bool IsDirectory, long Length, DateTime Modified);

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,          // versteckte Dateien gehören dazu, wie bei 7-Zip
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    internal static List<SourceItem> Scan(IReadOnlyList<string> inputs, IReadOnlyCollection<string> exclude)
    {
        var items = new List<SourceItem>();
        var topNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(exclude.Select(Path.GetFullPath), SafePath.PathComparer);

        foreach (var input in inputs)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            if (Directory.Exists(full))
            {
                var info = new DirectoryInfo(full);
                var name = info.Name.TrimEnd(':', '\\', '/');
                if (string.IsNullOrEmpty(name) || name == info.FullName) name = "Laufwerk";
                AddDirectory(items, info, UniqueTop(topNames, SafePath.CleanSegment(name)), excluded);
            }
            else if (File.Exists(full))
            {
                if (excluded.Contains(full)) continue;
                var info = new FileInfo(full);
                items.Add(new SourceItem(full, UniqueTop(topNames, SafePath.CleanSegment(info.Name)), false, info.Length, info.LastWriteTime));
            }
            else
            {
                throw new FileNotFoundException($"Nicht gefunden: {input}", input);
            }
        }
        return items;
    }

    private static void AddDirectory(List<SourceItem> items, DirectoryInfo dir, string entryName, HashSet<string> excluded)
    {
        items.Add(new SourceItem(dir.FullName, entryName + "/", true, 0, dir.LastWriteTime));
        var children = dir.EnumerateFileSystemInfos("*", Enumeration).ToList();
        children.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        foreach (var child in children)
        {
            var childName = entryName + "/" + SafePath.CleanSegment(child.Name);
            if (child is DirectoryInfo sub)
            {
                // Junctions/Symlinks auf Ordner nicht folgen (Endlosschleifen, doppelte Daten)
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                AddDirectory(items, sub, childName, excluded);
            }
            else if (child is FileInfo file)
            {
                if (excluded.Contains(file.FullName)) continue;
                items.Add(new SourceItem(file.FullName, childName, false, file.Length, file.LastWriteTime));
            }
        }
    }

    private static string UniqueTop(HashSet<string> used, string name)
    {
        if (used.Add(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (used.Add(candidate)) return candidate;
        }
    }

    // ---------- interne Typen ----------

    private sealed class FileJob
    {
        private int _refs = 1;
        public FileJob(SourceItem item) { Item = item; }
        public SourceItem Item { get; }
        public SafeFileHandle? Handle { get; init; }
        public long Length { get; init; }
        public int ChunkCount { get; init; }
        public ushort Method { get; init; }
        public void AddRef() => Interlocked.Increment(ref _refs);
        public void ReleaseRef()
        {
            if (Interlocked.Decrement(ref _refs) == 0) Handle?.Dispose();
        }
    }

    /// <summary>Übersprungene (gesperrte) Dateien und Größenänderungen; wird nur vom Producer geschrieben.</summary>
    private sealed class SkipLog
    {
        public List<string> Paths { get; } = new();
        public long Bytes { get; set; }
        public long SizeDelta { get; set; }
    }

    private sealed record Pending(FileJob Job, int Index, Task<ChunkResult>? Chunk);

    private readonly struct ChunkResult
    {
        public ChunkResult(byte[] data, int length, uint crc, int rawLength)
        {
            Data = data; Length = length; Crc = crc; RawLength = rawLength;
        }
        public byte[] Data { get; }
        public int Length { get; }
        public uint Crc { get; }
        public int RawLength { get; }
        public void Release() => ArrayPool<byte>.Shared.Return(Data);
    }
}
