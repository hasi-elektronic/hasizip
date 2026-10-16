using System.Diagnostics;

namespace HasiZip.Core;

/// <summary>Fortschritt einer Pack- oder Entpack-Aktion. <see cref="TotalBytes"/> ist 0, wenn die Größe unbekannt ist.</summary>
public readonly record struct ArchiveProgress(long DoneBytes, long TotalBytes, int DoneFiles, int TotalFiles, string? CurrentName)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)DoneBytes / TotalBytes, 0, 1);
}

/// <summary>Thread-sicherer Zähler, der höchstens alle ~80 ms an <see cref="IProgress{T}"/> meldet.</summary>
internal sealed class ProgressTracker
{
    private readonly IProgress<ArchiveProgress>? _sink;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private long _doneBytes;
    private int _doneFiles;
    private long _lastReportTicks = long.MinValue;
    private string? _current;

    public ProgressTracker(IProgress<ArchiveProgress>? sink, long totalBytes, int totalFiles)
    {
        _sink = sink;
        TotalBytes = totalBytes;
        TotalFiles = totalFiles;
    }

    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
    public long DoneBytes => Interlocked.Read(ref _doneBytes);
    public int DoneFiles => Volatile.Read(ref _doneFiles);

    public void AddBytes(long bytes, string? current = null)
    {
        Interlocked.Add(ref _doneBytes, bytes);
        if (current != null) _current = current;
        Report(false);
    }

    /// <summary>Setzt den Byte-Stand absolut (z. B. Position im komprimierten Datenstrom).</summary>
    public void SetBytes(long bytes, string? current = null)
    {
        Interlocked.Exchange(ref _doneBytes, bytes);
        if (current != null) _current = current;
        Report(false);
    }

    public void FileDone(string? current = null)
    {
        Interlocked.Increment(ref _doneFiles);
        if (current != null) _current = current;
        Report(false);
    }

    public void Report(bool force)
    {
        if (_sink == null) return;
        var now = _clock.ElapsedTicks;
        if (!force && now - Interlocked.Read(ref _lastReportTicks) < Stopwatch.Frequency / 12) return;
        lock (_gate)
        {
            if (!force && now - _lastReportTicks < Stopwatch.Frequency / 12) return;
            _lastReportTicks = now;
            _sink.Report(new ArchiveProgress(DoneBytes, TotalBytes, DoneFiles, TotalFiles, _current));
        }
    }
}
