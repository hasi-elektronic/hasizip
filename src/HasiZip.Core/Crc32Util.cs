namespace HasiZip.Core;

/// <summary>
/// CRC-32 (ZIP/zlib-Polynom). Die Blöcke werden parallel berechnet und hier wie bei zlib
/// (crc32_combine) zu einer Gesamt-Prüfsumme zusammengesetzt.
/// </summary>
public static class Crc32Util
{
    public static uint Compute(ReadOnlySpan<byte> data) => System.IO.Hashing.Crc32.HashToUInt32(data);

    /// <summary>CRC von A‖B aus crc(A), crc(B) und der Länge von B.</summary>
    public static uint Combine(uint crc1, uint crc2, long len2)
    {
        if (len2 <= 0) return crc1;
        Span<uint> even = stackalloc uint[32];
        Span<uint> odd = stackalloc uint[32];

        odd[0] = 0xEDB88320u;
        uint row = 1;
        for (int n = 1; n < 32; n++)
        {
            odd[n] = row;
            row <<= 1;
        }
        Square(even, odd);
        Square(odd, even);

        do
        {
            Square(even, odd);
            if ((len2 & 1) != 0) crc1 = Times(even, crc1);
            len2 >>= 1;
            if (len2 == 0) break;
            Square(odd, even);
            if ((len2 & 1) != 0) crc1 = Times(odd, crc1);
            len2 >>= 1;
        } while (len2 != 0);

        return crc1 ^ crc2;
    }

    private static uint Times(ReadOnlySpan<uint> mat, uint vec)
    {
        uint sum = 0;
        int i = 0;
        while (vec != 0)
        {
            if ((vec & 1) != 0) sum ^= mat[i];
            vec >>= 1;
            i++;
        }
        return sum;
    }

    private static void Square(Span<uint> square, ReadOnlySpan<uint> mat)
    {
        for (int n = 0; n < 32; n++) square[n] = Times(mat, mat[n]);
    }
}
