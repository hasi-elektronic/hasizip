using System.Buffers.Binary;
using System.Text;

namespace HasiZip.Core;

/// <summary>
/// Schreibt das ZIP-Containerformat (PKWARE APPNOTE 6.3) selbst, damit vorab parallel
/// komprimierte Daten direkt übernommen werden können. UTF-8-Namen (Bit 11), ZIP64 bei Bedarf.
/// </summary>
internal sealed class ZipStreamWriter
{
    public const ushort MethodStored = 0;
    public const ushort MethodDeflate = 8;

    /// <summary>Ab dieser Originalgröße bekommt der lokale Header ZIP64-Felder (Reserve für Block-Overhead).</summary>
    internal const long Zip64LocalThreshold = 0xFF000000L;

    private const ushort FlagUtf8 = 0x0800;
    private const ushort VersionDefault = 20;
    private const ushort VersionZip64 = 45;

    private readonly Stream _out;
    private readonly bool _forceZip64;
    private readonly List<Entry> _entries = new();
    private readonly byte[] _scratch = new byte[64];

    public ZipStreamWriter(Stream output, bool forceZip64 = false)
    {
        _out = output;
        _forceZip64 = forceZip64;
    }

    public sealed class Entry
    {
        internal byte[] Name = Array.Empty<byte>();
        internal bool IsDirectory;
        internal ushort Method;
        internal uint DosTime;
        internal uint Crc;
        internal long CompressedSize;
        internal long UncompressedSize;
        internal long LocalHeaderOffset;
        internal bool Zip64Local;
    }

    public int Count => _entries.Count;

    /// <summary>Eintrag, dessen CRC und Größen schon feststehen (Ordner, leere und kleine Dateien).</summary>
    public Entry BeginEntry(string name, bool isDirectory, ushort method, DateTime modified, long uncompressedSize, uint crc, long compressedSize)
    {
        var e = Create(name, isDirectory, method, modified, uncompressedSize);
        e.Crc = crc;
        e.CompressedSize = compressedSize;
        WriteLocalHeader(e);
        return e;
    }

    /// <summary>Eintrag mit Platzhaltern; CRC und komprimierte Größe kommen per <see cref="Patch"/>.</summary>
    public Entry BeginDeferredEntry(string name, ushort method, DateTime modified, long uncompressedSize)
    {
        var e = Create(name, false, method, modified, uncompressedSize);
        WriteLocalHeader(e);
        return e;
    }

    public void WriteData(ReadOnlySpan<byte> data) => _out.Write(data);

    public void Patch(Entry e, uint crc, long compressedSize)
    {
        e.Crc = crc;
        e.CompressedSize = compressedSize;
        if (!e.Zip64Local && compressedSize >= uint.MaxValue)
            throw new IOException("Interner Fehler: Eintrag ist ohne ZIP64-Header zu groß geworden.");
        var end = _out.Position;
        var buf = _scratch.AsSpan();
        _out.Position = e.LocalHeaderOffset + 14;
        BinaryPrimitives.WriteUInt32LittleEndian(buf, crc);
        if (e.Zip64Local)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf[4..], uint.MaxValue);
            BinaryPrimitives.WriteUInt32LittleEndian(buf[8..], uint.MaxValue);
            _out.Write(buf[..12]);
            // ZIP64-Extra: Header-ID (2) + Länge (2) + Originalgröße (8) + komprimierte Größe (8)
            _out.Position = e.LocalHeaderOffset + 30 + e.Name.Length + 4;
            BinaryPrimitives.WriteInt64LittleEndian(buf, e.UncompressedSize);
            BinaryPrimitives.WriteInt64LittleEndian(buf[8..], compressedSize);
            _out.Write(buf[..16]);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf[4..], (uint)compressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(buf[8..], (uint)e.UncompressedSize);
            _out.Write(buf[..12]);
        }
        _out.Position = end;
    }

    /// <summary>Zentralverzeichnis und End-of-Central-Directory schreiben.</summary>
    public void Finish()
    {
        long cdStart = _out.Position;
        foreach (var e in _entries) WriteCentralHeader(e);
        long cdEnd = _out.Position;
        long cdSize = cdEnd - cdStart;
        long count = _entries.Count;

        bool zip64 = _forceZip64 || count >= 0xFFFF || cdStart >= uint.MaxValue || cdSize >= uint.MaxValue;
        var b = new BinaryBuffer();
        if (zip64)
        {
            long zip64EocdOffset = _out.Position;
            b.U32(0x06064b50).U64(44).U16(VersionZip64).U16(VersionZip64).U32(0).U32(0)
             .U64((ulong)count).U64((ulong)count).U64((ulong)cdSize).U64((ulong)cdStart);
            b.U32(0x07064b50).U32(0).U64((ulong)zip64EocdOffset).U32(1);
        }
        b.U32(0x06054b50).U16(0).U16(0)
         .U16(zip64 ? (ushort)0xFFFF : (ushort)count).U16(zip64 ? (ushort)0xFFFF : (ushort)count)
         .U32(zip64 ? uint.MaxValue : (uint)cdSize).U32(zip64 ? uint.MaxValue : (uint)cdStart)
         .U16(0);
        b.WriteTo(_out);
    }

    private Entry Create(string name, bool isDirectory, ushort method, DateTime modified, long uncompressedSize)
    {
        if (isDirectory && !name.EndsWith('/')) name += "/";
        var e = new Entry
        {
            Name = Encoding.UTF8.GetBytes(name),
            IsDirectory = isDirectory,
            Method = method,
            DosTime = ToDosTime(modified),
            UncompressedSize = uncompressedSize,
            LocalHeaderOffset = _out.Position,
            Zip64Local = _forceZip64 || uncompressedSize >= Zip64LocalThreshold,
        };
        if (e.Name.Length > 0xFFFF) throw new IOException($"Dateiname zu lang: {name}");
        _entries.Add(e);
        return e;
    }

    private void WriteLocalHeader(Entry e)
    {
        var b = new BinaryBuffer();
        b.U32(0x04034b50)
         .U16(e.Zip64Local ? VersionZip64 : VersionDefault)
         .U16(FlagUtf8)
         .U16(e.Method)
         .U32(e.DosTime)
         .U32(e.Crc);
        if (e.Zip64Local)
        {
            b.U32(uint.MaxValue).U32(uint.MaxValue).U16((ushort)e.Name.Length).U16(20).Bytes(e.Name);
            b.U16(0x0001).U16(16).U64((ulong)e.UncompressedSize).U64((ulong)e.CompressedSize);
        }
        else
        {
            b.U32((uint)e.CompressedSize).U32((uint)e.UncompressedSize).U16((ushort)e.Name.Length).U16(0).Bytes(e.Name);
        }
        b.WriteTo(_out);
    }

    private void WriteCentralHeader(Entry e)
    {
        bool sizes64 = e.Zip64Local || e.UncompressedSize >= uint.MaxValue || e.CompressedSize >= uint.MaxValue;
        bool offset64 = _forceZip64 || e.LocalHeaderOffset >= uint.MaxValue;
        ushort extraLen = (ushort)((sizes64 ? 16 : 0) + (offset64 ? 8 : 0));
        if (extraLen > 0) extraLen += 4;
        ushort version = sizes64 || offset64 ? VersionZip64 : VersionDefault;

        var b = new BinaryBuffer();
        b.U32(0x02014b50)
         .U16(version) // „made by“: MS-DOS/Windows-Attribute
         .U16(version)
         .U16(FlagUtf8)
         .U16(e.Method)
         .U32(e.DosTime)
         .U32(e.Crc)
         .U32(sizes64 ? uint.MaxValue : (uint)e.CompressedSize)
         .U32(sizes64 ? uint.MaxValue : (uint)e.UncompressedSize)
         .U16((ushort)e.Name.Length)
         .U16(extraLen)
         .U16(0)  // Kommentar
         .U16(0)  // Startdisk
         .U16(0)  // interne Attribute
         .U32(e.IsDirectory ? 0x10u : 0x20u) // FILE_ATTRIBUTE_DIRECTORY / FILE_ATTRIBUTE_ARCHIVE
         .U32(offset64 ? uint.MaxValue : (uint)e.LocalHeaderOffset)
         .Bytes(e.Name);
        if (extraLen > 0)
        {
            b.U16(0x0001).U16((ushort)(extraLen - 4));
            if (sizes64) b.U64((ulong)e.UncompressedSize).U64((ulong)e.CompressedSize);
            if (offset64) b.U64((ulong)e.LocalHeaderOffset);
        }
        b.WriteTo(_out);
    }

    internal static uint ToDosTime(DateTime t)
    {
        if (t.Year < 1980) t = new DateTime(1980, 1, 1);
        if (t.Year > 2107) t = new DateTime(2107, 12, 31, 23, 59, 58);
        uint date = (uint)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
        uint time = (uint)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
        return (date << 16) | time;
    }

    /// <summary>Kleiner Little-Endian-Puffer für Header.</summary>
    private sealed class BinaryBuffer
    {
        private byte[] _buf = new byte[256];
        private int _len;

        private Span<byte> Take(int n)
        {
            if (_len + n > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + n));
            var s = _buf.AsSpan(_len, n);
            _len += n;
            return s;
        }

        public BinaryBuffer U16(ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(Take(2), v); return this; }
        public BinaryBuffer U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(Take(4), v); return this; }
        public BinaryBuffer U64(ulong v) { BinaryPrimitives.WriteUInt64LittleEndian(Take(8), v); return this; }
        public BinaryBuffer Bytes(ReadOnlySpan<byte> v) { v.CopyTo(Take(v.Length)); return this; }
        public void WriteTo(Stream s) => s.Write(_buf, 0, _len);
    }
}
