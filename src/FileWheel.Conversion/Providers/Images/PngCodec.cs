using System.Buffers.Binary;
using System.IO.Compression;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Archives;

namespace FileWheel.Conversion.Providers.Images;

/// <summary>Image decodee en RGBA 8 bits.</summary>
public sealed class RgbaImage(int width, int height, byte[] pixels, bool hasAlpha)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    /// <summary>RGBA, 4 octets par pixel, lignes contigues.</summary>
    public byte[] Pixels { get; } = pixels;
    public bool HasAlpha { get; } = hasAlpha;
}

/// <summary>
/// Decodeur et encodeur PNG integres (pas de dependance), utilises pour assembler des PDF et
/// enregistrer les pages rendues par PDFium. Gere toutes les profondeurs et types de couleur
/// standard, la transparence (tRNS) et l'entrelacement Adam7.
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    public const long MaxPixels = 120_000_000;

    public static RgbaImage Decode(Stream s)
    {
        var sig = new byte[8];
        s.ReadExactly(sig);
        if (!sig.AsSpan().SequenceEqual(Signature)) throw new ConversionException(ConversionErrorKind.Corrupt, "Ce fichier n'est pas un PNG valide.");
        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        var lenBuf = new byte[8];
        while (true)
        {
            if (s.Read(lenBuf, 0, 8) < 8) throw new ConversionException(ConversionErrorKind.Corrupt, "PNG tronqué.");
            int len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
            var type = System.Text.Encoding.ASCII.GetString(lenBuf, 4, 4);
            if (len < 0 || len > 1 << 30) throw new ConversionException(ConversionErrorKind.Corrupt, "PNG invalide (bloc anormal).");
            var data = new byte[len];
            s.ReadExactly(data);
            var crc = new byte[4];
            s.ReadExactly(crc);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                    bitDepth = data[8]; colorType = data[9]; interlace = data[12];
                    if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
                        throw new ConversionException(ConversionErrorKind.TooLarge, $"Image PNG trop grande ({width}×{height}).");
                    break;
                case "PLTE": palette = data; break;
                case "tRNS": trns = data; break;
                case "IDAT": idat.Write(data); break;
            }
            if (type == "IEND") break;
        }
        if (width == 0) throw new ConversionException(ConversionErrorKind.Corrupt, "PNG sans en-tête.");
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new ConversionException(ConversionErrorKind.Corrupt, "Type de couleur PNG inconnu.") };
        idat.Position = 0;
        byte[] raw;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        using (var ms = new MemoryStream())
        {
            z.CopyTo(ms);
            raw = ms.ToArray();
        }

        var rgba = new byte[(long)width * height * 4];
        bool hasAlpha = colorType is 4 or 6 || trns is not null;
        if (interlace == 0)
        {
            Unfilter(raw, 0, width, height, channels, bitDepth, out _, (x, y, px) => Store(rgba, width, x, y, px, colorType, bitDepth, palette, trns));
        }
        else
        {
            int[] xs = [0, 4, 0, 2, 0, 1, 0], ys = [0, 0, 4, 0, 2, 0, 1], dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
            int offset = 0;
            for (int p = 0; p < 7; p++)
            {
                int pw = (width - xs[p] + dx[p] - 1) / dx[p];
                int ph = (height - ys[p] + dy[p] - 1) / dy[p];
                if (pw <= 0 || ph <= 0) continue;
                int pass = p;
                Unfilter(raw, offset, pw, ph, channels, bitDepth, out int used,
                    (x, y, px) => Store(rgba, width, xs[pass] + x * dx[pass], ys[pass] + y * dy[pass], px, colorType, bitDepth, palette, trns));
                offset += used;
            }
        }
        return new RgbaImage(width, height, rgba, hasAlpha);
    }

    private delegate void PixelSink(int x, int y, ReadOnlySpan<ushort> samples);

    private static void Unfilter(byte[] raw, int offset, int w, int h, int channels, int depth, out int used, PixelSink sink)
    {
        int bitsPerPixel = channels * depth;
        int bpp = Math.Max(1, bitsPerPixel / 8);
        int stride = (w * bitsPerPixel + 7) / 8;
        used = (stride + 1) * h;
        if (offset + used > raw.Length) throw new ConversionException(ConversionErrorKind.Corrupt, "Données PNG incomplètes.");
        var prev = new byte[stride];
        var cur = new byte[stride];
        Span<ushort> px = stackalloc ushort[4];
        for (int y = 0; y < h; y++)
        {
            int rowStart = offset + y * (stride + 1);
            byte filter = raw[rowStart];
            Buffer.BlockCopy(raw, rowStart + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = filter switch
                {
                    0 => cur[i],
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + ((a + b) >> 1)),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => throw new ConversionException(ConversionErrorKind.Corrupt, "Filtre PNG inconnu."),
                };
            }
            for (int x = 0; x < w; x++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleIndex = x * channels + ch;
                    px[ch] = depth switch
                    {
                        8 => cur[sampleIndex],
                        16 => (ushort)((cur[sampleIndex * 2] << 8) | cur[sampleIndex * 2 + 1]),
                        _ => (ushort)((cur[sampleIndex * depth / 8] >> (8 - depth - (sampleIndex * depth % 8))) & ((1 << depth) - 1)),
                    };
                }
                sink(x, y, px[..channels]);
            }
            (prev, cur) = (cur, prev);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Store(byte[] rgba, int width, int x, int y, ReadOnlySpan<ushort> s, int colorType, int depth, byte[]? palette, byte[]? trns)
    {
        long o = ((long)y * width + x) * 4;
        byte To8(ushort v) => depth switch { 16 => (byte)(v >> 8), 8 => (byte)v, _ => (byte)(v * 255 / ((1 << depth) - 1)) };
        switch (colorType)
        {
            case 0:
            {
                byte g = To8(s[0]);
                rgba[o] = rgba[o + 1] = rgba[o + 2] = g;
                bool transparent = trns is { Length: >= 2 } && BinaryPrimitives.ReadUInt16BigEndian(trns) == s[0];
                rgba[o + 3] = transparent ? (byte)0 : (byte)255;
                break;
            }
            case 2:
            {
                rgba[o] = To8(s[0]); rgba[o + 1] = To8(s[1]); rgba[o + 2] = To8(s[2]);
                bool transparent = trns is { Length: >= 6 } && BinaryPrimitives.ReadUInt16BigEndian(trns) == s[0]
                                   && BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(2)) == s[1] && BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(4)) == s[2];
                rgba[o + 3] = transparent ? (byte)0 : (byte)255;
                break;
            }
            case 3:
            {
                int i = s[0];
                if (palette is null || i * 3 + 2 >= palette.Length) { rgba[o + 3] = 255; break; }
                rgba[o] = palette[i * 3]; rgba[o + 1] = palette[i * 3 + 1]; rgba[o + 2] = palette[i * 3 + 2];
                rgba[o + 3] = trns is not null && i < trns.Length ? trns[i] : (byte)255;
                break;
            }
            case 4:
            {
                byte g = To8(s[0]);
                rgba[o] = rgba[o + 1] = rgba[o + 2] = g;
                rgba[o + 3] = To8(s[1]);
                break;
            }
            case 6:
                rgba[o] = To8(s[0]); rgba[o + 1] = To8(s[1]); rgba[o + 2] = To8(s[2]); rgba[o + 3] = To8(s[3]);
                break;
        }
    }

    /// <summary>Encode en PNG 8 bits (RGBA ou RGB si aucun pixel n'est transparent).</summary>
    public static void Encode(Stream output, int width, int height, ReadOnlySpan<byte> rgba, bool keepAlpha = true)
    {
        bool alpha = keepAlpha && HasTransparency(rgba);
        int channels = alpha ? 4 : 3;
        output.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8; ihdr[9] = (byte)(alpha ? 6 : 2); ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);

        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            int stride = width * channels;
            var row = new byte[stride + 1];
            var prevRow = new byte[stride];
            var curRow = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                var src = rgba.Slice(y * width * 4, width * 4);
                for (int x = 0, j = 0; x < width; x++)
                {
                    curRow[j++] = src[x * 4]; curRow[j++] = src[x * 4 + 1]; curRow[j++] = src[x * 4 + 2];
                    if (alpha) curRow[j++] = src[x * 4 + 3];
                }
                // Filtre « Up » : bon compromis pour des pages rendues
                row[0] = 2;
                for (int i = 0; i < stride; i++) row[i + 1] = (byte)(curRow[i] - prevRow[i]);
                z.Write(row);
                (prevRow, curRow) = (curRow, prevRow);
            }
        }
        WriteChunk(output, "IDAT", ms.GetBuffer().AsSpan(0, (int)ms.Length));
        WriteChunk(output, "IEND", []);
    }

    public static bool HasTransparency(ReadOnlySpan<byte> rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] != 255) return true;
        return false;
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32.Update(Crc32.Compute(typeBytes), data);
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }
}

/// <summary>Lecture des dimensions et composantes d'un JPEG (sans decodage).</summary>
public static class JpegInfo
{
    public sealed record Info(int Width, int Height, int Components, bool AdobeInverted);

    public static Info Read(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) throw new ConversionException(ConversionErrorKind.Corrupt, "JPEG invalide.");
        int i = 2;
        bool adobe = false;
        while (i + 4 <= data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }
            byte marker = data[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
            int len = (data[i + 2] << 8) | data[i + 3];
            if (marker == 0xEE && len >= 12 && System.Text.Encoding.ASCII.GetString(data, i + 4, 5) == "Adobe") adobe = true;
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                int h = (data[i + 5] << 8) | data[i + 6];
                int w = (data[i + 7] << 8) | data[i + 8];
                int comps = data[i + 9];
                if (w <= 0 || h <= 0) throw new ConversionException(ConversionErrorKind.Corrupt, "JPEG sans dimensions.");
                return new Info(w, h, comps, adobe && comps == 4);
            }
            i += 2 + len;
        }
        throw new ConversionException(ConversionErrorKind.Corrupt, "JPEG incomplet (en-tête de trame introuvable).");
    }
}
