using System.Buffers.Binary;
using System.IO.Compression;
using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Fonts;

/// <summary>
/// Polices : WOFF (1.0) &lt;-&gt; TTF/OTF, integre. Conversion d'emballage sans perte (les tables
/// de la police sont recopiees a l'identique, seule la compression change).
/// WOFF2 n'est pas pris en charge (transformations de tables specifiques non implementees).
/// FileWheel n'installe jamais une police : il produit seulement le fichier.
/// </summary>
public sealed class FontProvider : ProviderBase
{
    public override string Id => "fonts";
    public override string DisplayName => "Polices (intégré)";

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } =
    [
        Cap("fonts", ["ttf", "otf"], "woff", priority: 10, lossNote: "Tables recopiées à l'identique, compressées (sans perte)."),
        Cap("fonts", ["woff"], "ttf", priority: 10, lossNote: "Décompression sans perte.", limitations: "Si la police contient des contours CFF, le résultat est une police OpenType (OTF)."),
        Cap("fonts", ["woff"], "otf", priority: 10, lossNote: "Décompression sans perte.", limitations: "Si la police contient des contours TrueType, le résultat est une police TTF."),
    ];

    public override Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var data = File.ReadAllBytes(req.Input);
            if (data.Length > 256 << 20) throw new ConversionException(ConversionErrorKind.TooLarge, "Police anormalement volumineuse.");
            var warnings = new List<string>();
            byte[] output;
            if (req.TargetFormat == "woff") output = SfntToWoff(data);
            else
            {
                output = WoffToSfnt(data, out uint flavor);
                bool isCff = flavor == 0x4F54544F; // « OTTO »
                if (req.TargetFormat == "ttf" && isCff) warnings.Add("Cette police a des contours CFF : c'est en réalité une police OpenType (OTF), enregistrée telle quelle.");
                if (req.TargetFormat == "otf" && !isCff) warnings.Add("Cette police a des contours TrueType : le fichier produit est une police TrueType, même avec l'extension .otf.");
            }
            File.WriteAllBytes(req.TempOutput, output);
            return new ConversionResult([req.TempOutput], warnings);
        }, ct);

    private sealed record TableEntry(uint Tag, uint Checksum, int Offset, int Length);

    public static byte[] SfntToWoff(byte[] sfnt)
    {
        if (sfnt.Length < 12) throw new ConversionException(ConversionErrorKind.Corrupt, "Police invalide.");
        uint flavor = BinaryPrimitives.ReadUInt32BigEndian(sfnt);
        if (flavor == 0x74746366) throw new ConversionException(ConversionErrorKind.NotSupported, "Les collections de polices (TTC) ne sont pas prises en charge.");
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        if (numTables == 0 || 12 + numTables * 16 > sfnt.Length) throw new ConversionException(ConversionErrorKind.Corrupt, "Table des tables de police invalide.");
        var tables = new List<TableEntry>();
        for (int i = 0; i < numTables; i++)
        {
            int o = 12 + i * 16;
            var t = new TableEntry(BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(o)), BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(o + 4)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(o + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(o + 12)));
            if (t.Offset < 0 || t.Length < 0 || (long)t.Offset + t.Length > sfnt.Length) throw new ConversionException(ConversionErrorKind.Corrupt, "Table de police hors limites.");
            tables.Add(t);
        }
        var compressed = new List<(TableEntry t, byte[] data, int origLen)>();
        foreach (var t in tables)
        {
            var raw = sfnt.AsSpan(t.Offset, t.Length).ToArray();
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw);
            var c = ms.ToArray();
            compressed.Add((t, c.Length < raw.Length ? c : raw, raw.Length));
        }
        int headerSize = 44 + 20 * numTables;
        int offset = headerSize;
        var dir = new List<(TableEntry t, int off, int compLen, int origLen)>();
        foreach (var (t, data, orig) in compressed)
        {
            dir.Add((t, offset, data.Length, orig));
            offset += (data.Length + 3) & ~3;
        }
        int total = offset;
        long totalSfnt = 12 + 16 * numTables + tables.Sum(t => (t.Length + 3) & ~3);
        var woff = new byte[total];
        var s = woff.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(s, 0x774F4646);               // wOFF
        BinaryPrimitives.WriteUInt32BigEndian(s[4..], flavor);
        BinaryPrimitives.WriteUInt32BigEndian(s[8..], (uint)total);
        BinaryPrimitives.WriteUInt16BigEndian(s[12..], (ushort)numTables);
        BinaryPrimitives.WriteUInt32BigEndian(s[16..], (uint)totalSfnt);
        BinaryPrimitives.WriteUInt16BigEndian(s[20..], 1);                   // version majeure
        for (int i = 0; i < dir.Count; i++)
        {
            var (t, off, compLen, origLen) = dir[i];
            int o = 44 + i * 20;
            BinaryPrimitives.WriteUInt32BigEndian(s[o..], t.Tag);
            BinaryPrimitives.WriteUInt32BigEndian(s[(o + 4)..], (uint)off);
            BinaryPrimitives.WriteUInt32BigEndian(s[(o + 8)..], (uint)compLen);
            BinaryPrimitives.WriteUInt32BigEndian(s[(o + 12)..], (uint)origLen);
            BinaryPrimitives.WriteUInt32BigEndian(s[(o + 16)..], t.Checksum);
            compressed[i].data.CopyTo(s[off..]);
        }
        return woff;
    }

    public static byte[] WoffToSfnt(byte[] woff, out uint flavor)
    {
        if (woff.Length < 44 || BinaryPrimitives.ReadUInt32BigEndian(woff) != 0x774F4646)
            throw new ConversionException(ConversionErrorKind.Corrupt, "Ce fichier n'est pas un WOFF valide.");
        flavor = BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(4));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(woff.AsSpan(12));
        uint totalSfnt = BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(16));
        if (numTables == 0 || 44 + numTables * 20 > woff.Length || totalSfnt > 256u << 20)
            throw new ConversionException(ConversionErrorKind.Corrupt, "En-tête WOFF invalide.");
        var entries = new List<(uint tag, int off, int compLen, int origLen, uint checksum)>();
        for (int i = 0; i < numTables; i++)
        {
            int o = 44 + i * 20;
            var e = (BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(o)), (int)BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(o + 4)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(o + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(o + 12)),
                BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(o + 16)));
            if (e.Item2 < 0 || e.Item3 < 0 || e.Item4 < 0 || (long)e.Item2 + e.Item3 > woff.Length || e.Item4 > 64 << 20)
                throw new ConversionException(ConversionErrorKind.Corrupt, "Table WOFF hors limites.");
            entries.Add(e);
        }
        entries.Sort((a, b) => a.tag.CompareTo(b.tag));
        int searchRange = 1, entrySelector = 0;
        while (searchRange * 2 <= numTables) { searchRange *= 2; entrySelector++; }
        searchRange *= 16;
        int dataStart = 12 + 16 * numTables;
        long size = dataStart + entries.Sum(e => (e.origLen + 3) & ~3);
        var sfnt = new byte[size];
        var s = sfnt.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(s, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(s[4..], (ushort)numTables);
        BinaryPrimitives.WriteUInt16BigEndian(s[6..], (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(s[8..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(s[10..], (ushort)(numTables * 16 - searchRange));
        int offset = dataStart;
        for (int i = 0; i < entries.Count; i++)
        {
            var (tag, off, compLen, origLen, checksum) = entries[i];
            byte[] data;
            if (compLen < origLen)
            {
                using var input = new MemoryStream(woff, off, compLen);
                using var z = new ZLibStream(input, CompressionMode.Decompress);
                data = new byte[origLen];
                z.ReadExactly(data);
            }
            else data = woff.AsSpan(off, origLen).ToArray();
            int o = 12 + i * 16;
            BinaryPrimitives.WriteUInt32BigEndian(s[o..], tag);
            BinaryPrimitives.WriteUInt32BigEndian(s[(o + 4)..], checksum);
            BinaryPrimitives.WriteUInt32BigEndian(s[(o + 8)..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(s[(o + 12)..], (uint)origLen);
            data.CopyTo(s[offset..]);
            offset += (origLen + 3) & ~3;
        }
        return sfnt;
    }
}
