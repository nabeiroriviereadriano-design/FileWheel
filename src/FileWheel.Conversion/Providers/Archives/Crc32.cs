namespace FileWheel.Conversion.Providers.Archives;

/// <summary>CRC-32 (IEEE 802.3), utilise pour verifier l'integrite des ZIP sans dependance externe.</summary>
public static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        crc ^= 0xFFFFFFFFu;
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Update(0, data);
}
