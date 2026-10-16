using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SysZipArchive = System.IO.Compression.ZipArchive;

namespace HasiZip.Core;

public sealed class ExtractOptions
{
    public string? Password { get; init; }
    /// <summary>0 = alle Prozessorkerne (nur ZIP wird parallel entpackt).</summary>
    public int Threads { get; init; }
}

/// <param name="OutputPath">Der neue Ordner – oder die einzelne Datei/der einzelne Ordner, wenn das Archiv nur ein Element enthielt.</param>
public sealed record ExtractResult(string OutputPath, long Bytes, int Files, TimeSpan Elapsed);

/// <summary>
/// Entpackt ZIP parallel (mehrere Leser auf derselben Datei, native zlib, CRC-Prüfung)
/// und 7z, RAR, TAR, GZ, BZ2, XZ sowie verschlüsselte ZIPs über SharpCompress.
/// Jeder Eintragsname läuft durch <see cref="SafePath"/>, nichts landet außerhalb des Ziels.
/// </summary>
public static class Extractor
{
    private enum Format { Zip, SevenZip, Rar, Stream }

    /// <summary>
    /// „Intelligentes Entpacken“ ohne Rückfragen: Enthält das Archiv genau ein Element
    /// (z. B. einen Ordner), landet es direkt neben dem Archiv, sonst in einem Ordner mit dem
    /// Archivnamen. Vorhandenes wird nie überschrieben („Name (2)“).
    /// </summary>
    public static Task<ExtractResult> ExtractSmartAsync(
        string archivePath, string? targetParent = null, ExtractOptions? options = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new ExtractOptions();
        return Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            archivePath = Path.GetFullPath(archivePath);
            var parent = Path.GetFullPath(targetParent ?? Path.GetDirectoryName(archivePath)!);
            var temp = Path.Combine(parent, $".hasizip-{Guid.NewGuid():N}"[..18]);
            var tempInfo = Directory.CreateDirectory(temp);
            try { tempInfo.Attributes |= FileAttributes.Hidden; } catch { /* nur kosmetisch */ }
            try
            {
                var (bytes, files) = ExtractInto(archivePath, temp, options, progress, cancellationToken);
                var top = Directory.EnumerateFileSystemEntries(temp).Take(2).ToList();
                string result;
                if (top.Count == 1)
                {
                    var single = top[0];
                    bool isDir = Directory.Exists(single);
                    result = SmartNames.Unique(Path.Combine(parent, Path.GetFileName(single)), isDir);
                    if (isDir) Directory.Move(single, result);
                    else File.Move(single, result);
                    Directory.Delete(temp);
                }
                else
                {
                    result = SmartNames.Unique(Path.Combine(parent, SafePath.CleanSegment(SmartNames.ArchiveBaseName(archivePath))), true);
                    Directory.Move(temp, result);
                    try { new DirectoryInfo(result).Attributes &= ~FileAttributes.Hidden; } catch { /* egal */ }
                }
                return new ExtractResult(result, bytes, files, clock.Elapsed);
            }
            catch
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { /* best effort */ }
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>Entpackt genau in <paramref name="destination"/> (wird angelegt). Vorhandene Dateien werden ersetzt.</summary>
    public static Task<ExtractResult> ExtractToAsync(
        string archivePath, string destination, ExtractOptions? options = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new ExtractOptions();
        return Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            Directory.CreateDirectory(destination);
            var (bytes, files) = ExtractInto(Path.GetFullPath(archivePath), Path.GetFullPath(destination), options, progress, cancellationToken);
            return new ExtractResult(Path.GetFullPath(destination), bytes, files, clock.Elapsed);
        }, cancellationToken);
    }

    private static (long Bytes, int Files) ExtractInto(
        string archivePath, string destination, ExtractOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        var format = Detect(archivePath);
        if (format == Format.Zip && CanUseFastZip(archivePath, options))
            return ExtractZipParallel(archivePath, destination, options, progress, token);
        return ExtractWithSharpCompress(archivePath, format, destination, options, progress, token);
    }

    private static Format Detect(string path)
    {
        Span<byte> head = stackalloc byte[8];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..n];
        if (n >= 4 && head[0] == 'P' && head[1] == 'K' && (head[2] is 3 or 5 or 7) && (head[3] is 4 or 6 or 8)) return Format.Zip;
        if (n >= 6 && head[..6].SequenceEqual(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C })) return Format.SevenZip;
        if (n >= 6 && head[..6].SequenceEqual("Rar!\x1A\x07"u8)) return Format.Rar;
        return Format.Stream;
    }

    /// <summary>Schnellweg nur für unverschlüsselte ZIPs mit Stored/Deflate/Deflate64.</summary>
    private static bool CanUseFastZip(string path, ExtractOptions options)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions());
            foreach (var e in archive.Entries)
            {
                if (e.IsEncrypted)
                {
                    if (options.Password == null) throw new PasswordRequiredException();
                    return false;
                }
                if (e.IsDirectory) continue;
                if (e.CompressionType is not (CompressionType.None or CompressionType.Deflate or CompressionType.Deflate64)) return false;
            }
            return true;
        }
        catch (PasswordRequiredException) { throw; }
        catch (Exception)
        {
            return true; // SharpCompress kommt nicht klar → .NET versuchen, das meldet ggf. einen klaren Fehler
        }
    }

    // ---------- ZIP parallel ----------

    private sealed record PlannedFile(int Index, string Target, long Length, uint Crc, DateTime Modified, string Name);

    private static (long, int) ExtractZipParallel(
        string path, string destination, ExtractOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        var byTarget = new Dictionary<string, PlannedFile>(SafePath.PathComparer);
        var directories = new HashSet<string>(SafePath.PathComparer) { destination };
        var dirTimes = new List<(string Dir, DateTime Time)>();
        using (var probe = OpenZip(path))
        {
            var entries = probe.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var target = SafePath.Resolve(destination, e.FullName);
                if (target == null) continue;
                bool isDir = e.FullName.EndsWith('/') || e.FullName.EndsWith('\\');
                if (isDir)
                {
                    directories.Add(target);
                    dirTimes.Add((target, e.LastWriteTime.DateTime));
                    continue;
                }
                directories.Add(Path.GetDirectoryName(target)!);
                // Doppelte Namen: der letzte Eintrag gewinnt (wie beim sequenziellen Entpacken)
                byTarget[target] = new PlannedFile(i, target, e.Length, e.Crc32, e.LastWriteTime.DateTime, e.FullName);
            }
        }

        foreach (var dir in directories) Directory.CreateDirectory(dir);
        var plan = byTarget.Values.OrderByDescending(p => p.Length).ToArray();
        long total = plan.Sum(p => p.Length);
        var tracker = new ProgressTracker(progress, total, plan.Length);
        tracker.Report(true);

        int workers = Math.Clamp(options.Threads > 0 ? options.Threads : Environment.ProcessorCount, 1, Math.Max(1, plan.Length));
        int cursor = -1;
        var errors = new List<Exception>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        var threads = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() =>
        {
            try
            {
                using var zip = OpenZip(path);
                var entries = zip.Entries;
                var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
                try
                {
                    while (true)
                    {
                        int next = Interlocked.Increment(ref cursor);
                        if (next >= plan.Length) break;
                        cts.Token.ThrowIfCancellationRequested();
                        var p = plan[next];
                        ExtractEntry(entries[p.Index], p, buffer, tracker, cts.Token);
                        tracker.FileDone(p.Name);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
                cts.Cancel();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task.WaitAll(threads);

        token.ThrowIfCancellationRequested();
        if (errors.Count > 0)
        {
            var real = errors.FirstOrDefault(e => e is not OperationCanceledException) ?? errors[0];
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(real).Throw();
        }

        foreach (var (dir, time) in dirTimes)
        {
            try { Directory.SetLastWriteTime(dir, time); } catch { /* egal */ }
        }
        tracker.Report(true);
        return (total, plan.Length);
    }

    private static SysZipArchive OpenZip(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        try { return new SysZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false); }
        catch (InvalidDataException ex)
        {
            fs.Dispose();
            throw new UnsupportedArchiveException("Die ZIP-Datei ist beschädigt oder unvollständig.", ex);
        }
    }

    private static void ExtractEntry(ZipArchiveEntry entry, PlannedFile p, byte[] buffer, ProgressTracker tracker, CancellationToken token)
    {
        var crc = new System.IO.Hashing.Crc32();
        using (var src = entry.Open())
        using (var dst = new FileStream(p.Target, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0,
            PreallocationSize = p.Length,
        }))
        {
            int n;
            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                crc.Append(buffer.AsSpan(0, n));
                dst.Write(buffer, 0, n);
                tracker.AddBytes(n, p.Name);
            }
        }
        if (crc.GetCurrentHashAsUInt32() != p.Crc)
            throw new InvalidDataException($"CRC-Fehler in „{p.Name}“ – das Archiv ist beschädigt.");
        try { File.SetLastWriteTime(p.Target, p.Modified); } catch { /* egal */ }
    }

    // ---------- 7z, RAR, TAR, GZ … ----------

    private static (long, int) ExtractWithSharpCompress(
        string path, Format format, string destination, ExtractOptions options,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        var readerOptions = new ReaderOptions { Password = options.Password, LeaveStreamOpen = false };
        IArchive? archive = null;
        FileStream? raw = null;
        ProgressTracker tracker;
        bool encrypted = false;
        try
        {
            if (format is Format.Zip or Format.SevenZip or Format.Rar)
            {
                archive = ArchiveFactory.OpenArchive(path, readerOptions);
                encrypted = archive.IsEncrypted || archive.Entries.Any(e => e.IsEncrypted);
                if (encrypted && options.Password == null) throw new PasswordRequiredException();
                var files = archive.Entries.Where(e => !e.IsDirectory).ToList();
                tracker = new ProgressTracker(progress, files.Sum(e => e.Size), files.Count);
            }
            else
            {
                raw = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                tracker = new ProgressTracker(progress, raw.Length, 0);
            }
        }
        catch (PasswordRequiredException) { archive?.Dispose(); raw?.Dispose(); throw; }
        catch (Exception ex) when (IsPasswordProblem(ex) || options.Password != null && format != Format.Stream && ex is SharpCompressException or InvalidDataException)
        {
            // 7z mit verschlüsselten Headern: falsches Passwort zeigt sich schon beim Öffnen als Datenfehler
            archive?.Dispose(); raw?.Dispose();
            if (options.Password == null) throw new PasswordRequiredException();
            throw new WrongPasswordException(ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SharpCompressException)
        {
            archive?.Dispose(); raw?.Dispose();
            throw new UnsupportedArchiveException("Unbekanntes oder beschädigtes Archivformat.", ex);
        }

        long bytes = 0;
        int count = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            foreach (var (e, open) in EnumerateEntries(archive, raw, format, readerOptions))
            {
                token.ThrowIfCancellationRequested();
                var key = e.Key;
                if (string.IsNullOrEmpty(key) && format == Format.Stream)
                    key = SmartNames.ArchiveBaseName(path); // .gz ohne gespeicherten Namen
                var target = SafePath.Resolve(destination, key);
                if (target == null) continue;
                if (e.IsDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                if (!string.IsNullOrEmpty(e.LinkTarget)) continue; // Symlinks aus TAR nicht anlegen
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var src = open())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 0))
                {
                    int n;
                    while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        dst.Write(buffer, 0, n);
                        bytes += n;
                        if (raw != null) tracker.SetBytes(raw.Position, key);
                        else tracker.AddBytes(n, key);
                    }
                }
                if (e.LastModifiedTime is DateTime t)
                {
                    try { File.SetLastWriteTime(target, t); } catch { /* egal */ }
                }
                count++;
                tracker.FileDone(key);
            }
        }
        catch (Exception ex) when (encrypted && options.Password != null
                                   && (IsPasswordProblem(ex) || ex is InvalidDataException or SharpCompressException))
        {
            // Falsches Passwort zeigt sich bei ZipCrypto/7z-AES oft erst als Daten- oder CRC-Fehler
            throw new WrongPasswordException(ex);
        }
        catch (Exception ex) when (IsPasswordProblem(ex))
        {
            if (options.Password == null) throw new PasswordRequiredException();
            throw new WrongPasswordException(ex);
        }
        catch (Exception ex) when (ex is SharpCompressException)
        {
            throw new UnsupportedArchiveException("Das Archiv ist beschädigt oder wird nicht unterstützt: " + ex.Message, ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            archive?.Dispose();
            raw?.Dispose();
        }
        tracker.Report(true);
        return (bytes, count);
    }

    /// <summary>
    /// Solide Archive (7z, solide RAR) und Streams (TAR/GZ …) werden am Stück gelesen,
    /// alle anderen Eintrag für Eintrag.
    /// </summary>
    private static IEnumerable<(IEntry Entry, Func<Stream> Open)> EnumerateEntries(
        IArchive? archive, FileStream? raw, Format format, ReaderOptions readerOptions)
    {
        if (archive != null && format != Format.SevenZip && !archive.IsSolid)
        {
            foreach (var e in archive.Entries) yield return (e, e.OpenEntryStream);
            yield break;
        }
        using var reader = archive != null ? archive.ExtractAllEntries() : OpenStreamReader(raw!, readerOptions);
        while (reader.MoveToNextEntry()) yield return (reader.Entry, reader.OpenEntryStream);
    }

    private static IReader OpenStreamReader(FileStream raw, ReaderOptions readerOptions)
    {
        try { return ReaderFactory.OpenReader(raw, readerOptions); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SharpCompressException)
        {
            throw new UnsupportedArchiveException("Unbekanntes oder beschädigtes Archivformat.", ex);
        }
    }

    private static bool IsPasswordProblem(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException) return true;
            var m = e.Message;
            if (m.Contains("password", StringComparison.OrdinalIgnoreCase) || m.Contains("encrypt", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
