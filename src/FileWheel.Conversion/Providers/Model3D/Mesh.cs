using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Data;

namespace FileWheel.Conversion.Providers.Model3D;

/// <summary>Maillage triangule (geometrie seule : positions et triangles).</summary>
public sealed class Mesh
{
    public List<float> Positions { get; } = new();   // x,y,z a la suite
    public List<int> Triangles { get; } = new();      // indices a,b,c a la suite
    public List<string> Notes { get; } = new();

    public int VertexCount => Positions.Count / 3;
    public int TriangleCount => Triangles.Count / 3;
    public const int MaxTriangles = 50_000_000;

    public int AddVertex(float x, float y, float z)
    {
        Positions.Add(x); Positions.Add(y); Positions.Add(z);
        return VertexCount - 1;
    }

    public void AddTriangle(int a, int b, int c)
    {
        if (TriangleCount >= MaxTriangles) throw new ConversionException(ConversionErrorKind.TooLarge, $"Modèle trop volumineux (plus de {MaxTriangles:N0} triangles).");
        int n = VertexCount;
        if (a < 0 || b < 0 || c < 0 || a >= n || b >= n || c >= n) throw new ConversionException(ConversionErrorKind.Corrupt, "Indice de sommet invalide dans le modèle.");
        Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
    }

    public (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) Bounds()
    {
        if (Positions.Count == 0) return default;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < Positions.Count; i += 3)
        {
            minX = Math.Min(minX, Positions[i]); maxX = Math.Max(maxX, Positions[i]);
            minY = Math.Min(minY, Positions[i + 1]); maxY = Math.Max(maxY, Positions[i + 1]);
            minZ = Math.Min(minZ, Positions[i + 2]); maxZ = Math.Max(maxZ, Positions[i + 2]);
        }
        return (minX, minY, minZ, maxX, maxY, maxZ);
    }

    /// <summary>Statistiques lisibles : dimensions, triangles, aretes ouvertes, triangles degeneres.</summary>
    public string Describe()
    {
        var b = Bounds();
        int degenerate = 0;
        var edges = new Dictionary<(int, int), int>();
        for (int t = 0; t < Triangles.Count; t += 3)
        {
            int a = Triangles[t], c1 = Triangles[t + 1], c2 = Triangles[t + 2];
            if (a == c1 || c1 == c2 || a == c2) { degenerate++; continue; }
            foreach (var (u, v) in new[] { (a, c1), (c1, c2), (c2, a) })
            {
                var key = u < v ? (u, v) : (v, u);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }
        int open = edges.Count(e => e.Value == 1);
        int nonManifold = edges.Count(e => e.Value > 2);
        var inv = CultureInfo.GetCultureInfo("fr-FR");
        var sb = new StringBuilder();
        sb.Append($"{TriangleCount.ToString("N0", inv)} triangles, {VertexCount.ToString("N0", inv)} sommets. ");
        sb.Append($"Dimensions : {(b.maxX - b.minX).ToString("0.###", inv)} × {(b.maxY - b.minY).ToString("0.###", inv)} × {(b.maxZ - b.minZ).ToString("0.###", inv)} (unités du fichier).");
        if (open > 0) sb.Append($" Maillage ouvert : {open.ToString("N0", inv)} arête(s) libre(s) (peut poser problème à l'impression 3D).");
        if (nonManifold > 0) sb.Append($" {nonManifold.ToString("N0", inv)} arête(s) non-manifold.");
        if (degenerate > 0) sb.Append($" {degenerate.ToString("N0", inv)} triangle(s) dégénéré(s).");
        return sb.ToString();
    }

    /// <summary>Fusionne les sommets identiques (les STL repetent chaque sommet pour chaque triangle).</summary>
    public Mesh Welded()
    {
        var m = new Mesh();
        m.Notes.AddRange(Notes);
        var map = new Dictionary<(float, float, float), int>();
        var remap = new int[VertexCount];
        for (int i = 0; i < VertexCount; i++)
        {
            var key = (Positions[i * 3], Positions[i * 3 + 1], Positions[i * 3 + 2]);
            if (!map.TryGetValue(key, out var idx)) { idx = m.AddVertex(key.Item1, key.Item2, key.Item3); map[key] = idx; }
            remap[i] = idx;
        }
        for (int t = 0; t < Triangles.Count; t += 3) m.AddTriangle(remap[Triangles[t]], remap[Triangles[t + 1]], remap[Triangles[t + 2]]);
        return m;
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // =================================================================== lecture

    public static Mesh Read(string path, string format, CancellationToken ct) => format switch
    {
        "stl" => ReadStl(path, ct),
        "obj" => ReadObj(path, ct),
        "ply" => ReadPly(path, ct),
        "3mf" => Read3mf(path),
        "glb" => ReadGltf(path, binary: true),
        "gltf" => ReadGltf(path, binary: false),
        _ => throw new ConversionException(ConversionErrorKind.NotSupported, "Format 3D non pris en charge par le moteur intégré."),
    };

    private static Mesh ReadStl(string path, CancellationToken ct)
    {
        var m = new Mesh();
        using var fs = File.OpenRead(path);
        var head = new byte[84];
        int n = fs.Read(head, 0, 84);
        bool ascii = n >= 5 && Encoding.ASCII.GetString(head, 0, 5) == "solid";
        if (ascii && n == 84)
        {
            uint tri = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(80));
            if (84 + 50L * tri == fs.Length) ascii = false; // binaire commencant par « solid »
        }
        if (!ascii)
        {
            if (n < 84) throw new ConversionException(ConversionErrorKind.Corrupt, "STL binaire tronqué.");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(80));
            if (count > MaxTriangles) throw new ConversionException(ConversionErrorKind.TooLarge, "STL trop volumineux.");
            if (84 + 50L * count > fs.Length) throw new ConversionException(ConversionErrorKind.Corrupt, "STL binaire tronqué (nombre de triangles incohérent).");
            var rec = new byte[50];
            for (uint i = 0; i < count; i++)
            {
                if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                fs.ReadExactly(rec);
                int a = m.AddVertex(F(rec, 12), F(rec, 16), F(rec, 20));
                int b = m.AddVertex(F(rec, 24), F(rec, 28), F(rec, 32));
                int c = m.AddVertex(F(rec, 36), F(rec, 40), F(rec, 44));
                m.Triangles.AddRange([a, b, c]);
            }
            return m.Welded();
        }
        fs.Position = 0;
        using var r = new StreamReader(fs);
        var verts = new List<int>();
        string? line;
        int lineNo = 0;
        while ((line = r.ReadLine()) is not null)
        {
            if ((++lineNo & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            var t = line.Trim();
            if (t.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase))
            {
                var p = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) throw new ConversionException(ConversionErrorKind.Corrupt, $"STL ASCII invalide ligne {lineNo}.");
                verts.Add(m.AddVertex(Pf(p[1], lineNo), Pf(p[2], lineNo), Pf(p[3], lineNo)));
            }
            else if (t.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
            {
                for (int k = 1; k + 1 < verts.Count; k++) m.AddTriangle(verts[0], verts[k], verts[k + 1]);
                verts.Clear();
            }
        }
        return m.Welded();
    }

    private static float F(byte[] b, int o) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(o));

    private static float Pf(string s, int line) =>
        float.TryParse(s, NumberStyles.Float, Inv, out var v) && float.IsFinite(v) ? v : throw new ConversionException(ConversionErrorKind.Corrupt, $"Nombre invalide ligne {line} : « {s} ».");

    private static Mesh ReadObj(string path, CancellationToken ct)
    {
        var m = new Mesh();
        using var r = new StreamReader(path);
        string? line;
        int lineNo = 0;
        bool hasMaterials = false, hasUv = false;
        while ((line = r.ReadLine()) is not null)
        {
            if ((++lineNo & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            var t = line.Trim();
            if (t.StartsWith("v "))
            {
                var p = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) throw new ConversionException(ConversionErrorKind.Corrupt, $"OBJ invalide ligne {lineNo}.");
                m.AddVertex(Pf(p[1], lineNo), Pf(p[2], lineNo), Pf(p[3], lineNo));
            }
            else if (t.StartsWith("f "))
            {
                var p = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var idx = new List<int>();
                for (int k = 1; k < p.Length; k++)
                {
                    var first = p[k].Split('/')[0];
                    if (!int.TryParse(first, out var v)) throw new ConversionException(ConversionErrorKind.Corrupt, $"OBJ : face invalide ligne {lineNo}.");
                    idx.Add(v > 0 ? v - 1 : m.VertexCount + v);
                }
                for (int k = 1; k + 1 < idx.Count; k++) m.AddTriangle(idx[0], idx[k], idx[k + 1]);
            }
            else if (t.StartsWith("usemtl") || t.StartsWith("mtllib")) hasMaterials = true;
            else if (t.StartsWith("vt ")) hasUv = true;
        }
        if (hasMaterials || hasUv) m.Notes.Add("Matériaux et coordonnées de texture de l'OBJ non conservés (géométrie seule).");
        return m;
    }

    private static Mesh ReadPly(string path, CancellationToken ct)
    {
        var m = new Mesh();
        using var fs = File.OpenRead(path);
        var header = new StringBuilder();
        var lineBuf = new List<byte>();
        while (true)
        {
            int b = fs.ReadByte();
            if (b < 0) throw new ConversionException(ConversionErrorKind.Corrupt, "PLY : en-tête incomplet.");
            if (b == '\n')
            {
                var l = Encoding.ASCII.GetString(lineBuf.ToArray()).TrimEnd('\r');
                header.Append(l).Append('\n');
                lineBuf.Clear();
                if (l == "end_header") break;
                if (header.Length > 1 << 20) throw new ConversionException(ConversionErrorKind.Corrupt, "PLY : en-tête anormal.");
                continue;
            }
            lineBuf.Add((byte)b);
        }
        var lines = header.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string format = "";
        var elements = new List<(string name, long count, List<(string name, string type, string? countType)> props)>();
        foreach (var l in lines)
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p[0] == "format") format = p[1];
            else if (p[0] == "element") elements.Add((p[1], long.Parse(p[2], Inv), new()));
            else if (p[0] == "property" && elements.Count > 0)
            {
                if (p[1] == "list") elements[^1].props.Add((p[4], p[3], p[2]));
                else elements[^1].props.Add((p[2], p[1], null));
            }
        }
        bool ascii = format == "ascii";
        bool big = format == "binary_big_endian";
        if (!ascii && format != "binary_little_endian" && !big) throw new ConversionException(ConversionErrorKind.Corrupt, "PLY : format inconnu.");
        var reader = ascii ? null : new BinaryReader(fs);
        StreamReader? text = ascii ? new StreamReader(fs) : null;
        Queue<string> tokens = new();
        string NextToken()
        {
            while (tokens.Count == 0)
            {
                var l = text!.ReadLine() ?? throw new ConversionException(ConversionErrorKind.Corrupt, "PLY tronqué.");
                foreach (var tk in l.Split(' ', StringSplitOptions.RemoveEmptyEntries)) tokens.Enqueue(tk);
            }
            return tokens.Dequeue();
        }
        double ReadValue(string type)
        {
            if (ascii) return double.Parse(NextToken(), NumberStyles.Float, Inv);
            byte[] Bytes(int n) { var b = reader!.ReadBytes(n); if (b.Length < n) throw new ConversionException(ConversionErrorKind.Corrupt, "PLY tronqué."); if (big) Array.Reverse(b); return b; }
            return type switch
            {
                "char" or "int8" => (sbyte)Bytes(1)[0],
                "uchar" or "uint8" => Bytes(1)[0],
                "short" or "int16" => BitConverter.ToInt16(Bytes(2)),
                "ushort" or "uint16" => BitConverter.ToUInt16(Bytes(2)),
                "int" or "int32" => BitConverter.ToInt32(Bytes(4)),
                "uint" or "uint32" => BitConverter.ToUInt32(Bytes(4)),
                "float" or "float32" => BitConverter.ToSingle(Bytes(4)),
                "double" or "float64" => BitConverter.ToDouble(Bytes(8)),
                _ => throw new ConversionException(ConversionErrorKind.Corrupt, $"PLY : type « {type} » inconnu."),
            };
        }
        foreach (var (name, count, props) in elements)
        {
            if (count > MaxTriangles) throw new ConversionException(ConversionErrorKind.TooLarge, "PLY trop volumineux.");
            for (long i = 0; i < count; i++)
            {
                if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                double x = 0, y = 0, z = 0;
                List<int>? face = null;
                foreach (var (pname, ptype, countType) in props)
                {
                    if (countType is not null)
                    {
                        int n = (int)ReadValue(countType);
                        if (n < 0 || n > 1000) throw new ConversionException(ConversionErrorKind.Corrupt, "PLY : liste anormale.");
                        var list = new List<int>(n);
                        for (int k = 0; k < n; k++) list.Add((int)ReadValue(ptype));
                        if (pname is "vertex_indices" or "vertex_index") face = list;
                        continue;
                    }
                    var v = ReadValue(ptype);
                    if (name == "vertex") { if (pname == "x") x = v; else if (pname == "y") y = v; else if (pname == "z") z = v; }
                }
                if (name == "vertex") m.AddVertex((float)x, (float)y, (float)z);
                else if (name == "face" && face is not null)
                    for (int k = 1; k + 1 < face.Count; k++) m.AddTriangle(face[0], face[k], face[k + 1]);
            }
        }
        if (elements.Any(e => e.props.Any(p => p.name is "red" or "nx" or "u" or "s")))
            m.Notes.Add("Couleurs, normales et coordonnées de texture du PLY non conservées.");
        return m;
    }

    private static readonly XNamespace Ns3mf = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    private static Mesh Read3mf(string path)
    {
        var m = new Mesh();
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("3D/3dmodel.model", StringComparison.OrdinalIgnoreCase))
                    ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                    ?? throw new ConversionException(ConversionErrorKind.Corrupt, "3MF invalide : modèle 3D absent.");
        XDocument doc;
        using (var s = entry.Open())
        using (var xr = XmlReader.Create(s, XmlJson.SafeSettings)) doc = XDocument.Load(xr);
        var ns = doc.Root!.Name.Namespace;
        bool transformed = doc.Descendants(ns + "item").Any(i => i.Attribute("transform") is not null) || doc.Descendants(ns + "component").Any();
        foreach (var mesh in doc.Descendants(ns + "mesh"))
        {
            int baseIndex = m.VertexCount;
            foreach (var v in mesh.Element(ns + "vertices")?.Elements(ns + "vertex") ?? [])
                m.AddVertex(Attr(v, "x"), Attr(v, "y"), Attr(v, "z"));
            foreach (var t in mesh.Element(ns + "triangles")?.Elements(ns + "triangle") ?? [])
                m.AddTriangle(baseIndex + (int)Attr(t, "v1"), baseIndex + (int)Attr(t, "v2"), baseIndex + (int)Attr(t, "v3"));
        }
        if (transformed) m.Notes.Add("3MF : les transformations d'objets (position, rotation, assemblages) ne sont pas appliquées ; seuls les maillages bruts sont exportés.");
        if (doc.Descendants().Any(e => e.Name.LocalName is "basematerials" or "colorgroup" or "texture2d"))
            m.Notes.Add("Couleurs et matériaux 3MF non conservés.");
        return m;
    }

    private static float Attr(XElement e, string name) =>
        float.TryParse((string?)e.Attribute(name), NumberStyles.Float, Inv, out var v) ? v : throw new ConversionException(ConversionErrorKind.Corrupt, $"3MF : attribut « {name} » invalide.");

    private static Mesh ReadGltf(string path, bool binary)
    {
        var m = new Mesh();
        JsonNode json;
        byte[]? bin = null;
        if (binary)
        {
            var all = File.ReadAllBytes(path);
            if (all.Length < 20 || Encoding.ASCII.GetString(all, 0, 4) != "glTF") throw new ConversionException(ConversionErrorKind.Corrupt, "GLB invalide.");
            int offset = 12;
            json = null!;
            while (offset + 8 <= all.Length)
            {
                int len = BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(offset));
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(offset + 4));
                if (len < 0 || offset + 8 + len > all.Length) throw new ConversionException(ConversionErrorKind.Corrupt, "GLB tronqué.");
                if (type == 0x4E4F534A) json = JsonNode.Parse(all.AsSpan(offset + 8, len))!;
                else if (type == 0x004E4942) bin = all.AsSpan(offset + 8, len).ToArray();
                offset += 8 + len;
            }
            if (json is null) throw new ConversionException(ConversionErrorKind.Corrupt, "GLB sans partie JSON.");
        }
        else json = JsonNode.Parse(File.ReadAllText(path))!;

        var buffers = new List<byte[]>();
        foreach (var b in json["buffers"]?.AsArray() ?? [])
        {
            var uri = b?["uri"]?.GetValue<string>();
            if (uri is null) { buffers.Add(bin ?? []); continue; }
            if (uri.StartsWith("data:"))
            {
                var comma = uri.IndexOf(',');
                buffers.Add(Convert.FromBase64String(uri[(comma + 1)..]));
                continue;
            }
            // Fichier voisin uniquement : pas de chemin absolu ni de remontee de dossier
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var target = FileWheel.Core.FileOperations.PathSafety.ResolveArchiveEntry(dir, Uri.UnescapeDataString(uri));
            if (target is null || !File.Exists(target)) throw new ConversionException(ConversionErrorKind.InvalidInput, $"glTF : tampon externe introuvable ou refusé ({uri}).");
            buffers.Add(File.ReadAllBytes(target));
        }
        var views = json["bufferViews"]?.AsArray() ?? [];
        var accessors = json["accessors"]?.AsArray() ?? [];

        byte[] View(int accessorIndex, out int byteOffset, out int stride)
        {
            var acc = accessors[accessorIndex]!;
            var view = views[acc["bufferView"]!.GetValue<int>()]!;
            var buf = buffers[view["buffer"]!.GetValue<int>()];
            byteOffset = (view["byteOffset"]?.GetValue<int>() ?? 0) + (acc["byteOffset"]?.GetValue<int>() ?? 0);
            stride = view["byteStride"]?.GetValue<int>() ?? 0;
            return buf;
        }

        bool hasNodesTransforms = (json["nodes"]?.AsArray() ?? []).Any(n => n?["matrix"] is not null || n?["translation"] is not null || n?["rotation"] is not null || n?["scale"] is not null);
        foreach (var mesh in json["meshes"]?.AsArray() ?? [])
        {
            foreach (var prim in mesh?["primitives"]?.AsArray() ?? [])
            {
                int mode = prim?["mode"]?.GetValue<int>() ?? 4;
                if (mode != 4) continue;
                var posIdx = prim?["attributes"]?["POSITION"]?.GetValue<int>();
                if (posIdx is null) continue;
                if (prim?["extensions"]?["KHR_draco_mesh_compression"] is not null)
                    throw new ConversionException(ConversionErrorKind.NotSupported, "glTF compressé avec Draco : non pris en charge par le moteur intégré.");
                var pacc = accessors[posIdx.Value]!;
                int count = pacc["count"]!.GetValue<int>();
                var pbuf = View(posIdx.Value, out int poff, out int pstride);
                if (pstride == 0) pstride = 12;
                int baseIndex = m.VertexCount;
                for (int i = 0; i < count; i++)
                {
                    int o = poff + i * pstride;
                    if (o + 12 > pbuf.Length) throw new ConversionException(ConversionErrorKind.Corrupt, "glTF : tampon de positions trop court.");
                    m.AddVertex(BinaryPrimitives.ReadSingleLittleEndian(pbuf.AsSpan(o)), BinaryPrimitives.ReadSingleLittleEndian(pbuf.AsSpan(o + 4)), BinaryPrimitives.ReadSingleLittleEndian(pbuf.AsSpan(o + 8)));
                }
                var idxAcc = prim?["indices"]?.GetValue<int>();
                if (idxAcc is null)
                {
                    for (int i = 0; i + 2 < count; i += 3) m.AddTriangle(baseIndex + i, baseIndex + i + 1, baseIndex + i + 2);
                    continue;
                }
                var iacc = accessors[idxAcc.Value]!;
                int icount = iacc["count"]!.GetValue<int>();
                int ctype = iacc["componentType"]!.GetValue<int>();
                var ibuf = View(idxAcc.Value, out int ioff, out _);
                int size = ctype switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new ConversionException(ConversionErrorKind.Corrupt, "glTF : type d'indice invalide.") };
                if (ioff + (long)icount * size > ibuf.Length) throw new ConversionException(ConversionErrorKind.Corrupt, "glTF : tampon d'indices trop court.");
                int Read(int k) => size switch
                {
                    1 => ibuf[ioff + k],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(ibuf.AsSpan(ioff + k * 2)),
                    _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(ibuf.AsSpan(ioff + k * 4)),
                };
                for (int k = 0; k + 2 < icount; k += 3) m.AddTriangle(baseIndex + Read(k), baseIndex + Read(k + 1), baseIndex + Read(k + 2));
            }
        }
        if (hasNodesTransforms) m.Notes.Add("glTF : transformations des nœuds non appliquées (géométrie dans le repère de chaque maillage).");
        if (json["materials"] is JsonArray { Count: > 0 } || json["textures"] is JsonArray { Count: > 0 })
            m.Notes.Add("Matériaux et textures glTF non conservés (géométrie seule).");
        if (json["animations"] is JsonArray { Count: > 0 }) m.Notes.Add("Animations glTF non conservées.");
        return m;
    }

    // =================================================================== ecriture

    public void Write(string path, string format)
    {
        switch (format)
        {
            case "stl": WriteStl(path); break;
            case "obj": WriteObj(path); break;
            case "ply": WritePly(path); break;
            case "3mf": Write3mf(path); break;
            case "glb": WriteGlb(path); break;
            default: throw new ConversionException(ConversionErrorKind.NotSupported, "Format 3D de sortie non pris en charge.");
        }
    }

    private void WriteStl(string path)
    {
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var w = new BinaryWriter(fs);
        var header = new byte[80];
        Encoding.ASCII.GetBytes("FileWheel STL binaire").CopyTo(header, 0);
        w.Write(header);
        w.Write((uint)TriangleCount);
        for (int t = 0; t < Triangles.Count; t += 3)
        {
            var (ax, ay, az) = V(Triangles[t]);
            var (bx, by, bz) = V(Triangles[t + 1]);
            var (cx, cy, cz) = V(Triangles[t + 2]);
            float ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0) { nx /= len; ny /= len; nz /= len; }
            foreach (var f in new[] { nx, ny, nz, ax, ay, az, bx, by, bz, cx, cy, cz }) w.Write(f);
            w.Write((ushort)0);
        }
    }

    private (float, float, float) V(int i) => (Positions[i * 3], Positions[i * 3 + 1], Positions[i * 3 + 2]);

    private void WriteObj(string path)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("# FileWheel - géométrie seule");
        for (int i = 0; i < Positions.Count; i += 3)
            w.WriteLine($"v {Positions[i].ToString("R", Inv)} {Positions[i + 1].ToString("R", Inv)} {Positions[i + 2].ToString("R", Inv)}");
        for (int t = 0; t < Triangles.Count; t += 3)
            w.WriteLine($"f {Triangles[t] + 1} {Triangles[t + 1] + 1} {Triangles[t + 2] + 1}");
    }

    private void WritePly(string path)
    {
        using var fs = new FileStream(path, FileMode.CreateNew);
        var header = $"ply\nformat binary_little_endian 1.0\ncomment FileWheel\nelement vertex {VertexCount}\nproperty float x\nproperty float y\nproperty float z\nelement face {TriangleCount}\nproperty list uchar int vertex_indices\nend_header\n";
        fs.Write(Encoding.ASCII.GetBytes(header));
        using var w = new BinaryWriter(fs);
        foreach (var p in Positions) w.Write(p);
        for (int t = 0; t < Triangles.Count; t += 3)
        {
            w.Write((byte)3);
            w.Write(Triangles[t]); w.Write(Triangles[t + 1]); w.Write(Triangles[t + 2]);
        }
    }

    private void Write3mf(string path)
    {
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Text(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var sw = new StreamWriter(e.Open(), new UTF8Encoding(false));
            sw.Write(content);
        }
        Text("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/></Types>");
        Text("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");
        var e2 = zip.CreateEntry("3D/3dmodel.model", CompressionLevel.Optimal);
        using var w = new StreamWriter(e2.Open(), new UTF8Encoding(false));
        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<model unit=\"millimeter\" xml:lang=\"fr-FR\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">\n<resources><object id=\"1\" type=\"model\"><mesh><vertices>\n");
        for (int i = 0; i < Positions.Count; i += 3)
            w.Write($"<vertex x=\"{Positions[i].ToString("R", Inv)}\" y=\"{Positions[i + 1].ToString("R", Inv)}\" z=\"{Positions[i + 2].ToString("R", Inv)}\"/>\n");
        w.Write("</vertices><triangles>\n");
        for (int t = 0; t < Triangles.Count; t += 3)
            w.Write($"<triangle v1=\"{Triangles[t]}\" v2=\"{Triangles[t + 1]}\" v3=\"{Triangles[t + 2]}\"/>\n");
        w.Write("</triangles></mesh></object></resources>\n<build><item objectid=\"1\"/></build>\n</model>\n");
    }

    private void WriteGlb(string path)
    {
        var b = Bounds();
        using var binMs = new MemoryStream();
        using (var bw = new BinaryWriter(binMs, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var p in Positions) bw.Write(p);
            foreach (var i in Triangles) bw.Write((uint)i);
        }
        int posBytes = Positions.Count * 4;
        int idxBytes = Triangles.Count * 4;
        var gltf = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "FileWheel" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
            ["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0 }),
            ["meshes"] = new JsonArray(new JsonObject
            {
                ["primitives"] = new JsonArray(new JsonObject
                {
                    ["attributes"] = new JsonObject { ["POSITION"] = 0 },
                    ["indices"] = 1,
                    ["mode"] = 4,
                }),
            }),
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = posBytes + idxBytes }),
            ["bufferViews"] = new JsonArray(
                new JsonObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = posBytes, ["target"] = 34962 },
                new JsonObject { ["buffer"] = 0, ["byteOffset"] = posBytes, ["byteLength"] = idxBytes, ["target"] = 34963 }),
            ["accessors"] = new JsonArray(
                new JsonObject
                {
                    ["bufferView"] = 0, ["componentType"] = 5126, ["count"] = VertexCount, ["type"] = "VEC3",
                    ["min"] = new JsonArray(b.minX, b.minY, b.minZ), ["max"] = new JsonArray(b.maxX, b.maxY, b.maxZ),
                },
                new JsonObject { ["bufferView"] = 1, ["componentType"] = 5125, ["count"] = Triangles.Count, ["type"] = "SCALAR" }),
        };
        var jsonBytes = Encoding.UTF8.GetBytes(gltf.ToJsonString());
        int jsonPad = (4 - jsonBytes.Length % 4) % 4;
        var bin = binMs.ToArray();
        int binPad = (4 - bin.Length % 4) % 4;
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var w = new BinaryWriter(fs);
        int total = 12 + 8 + jsonBytes.Length + jsonPad + 8 + bin.Length + binPad;
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
        w.Write((uint)(jsonBytes.Length + jsonPad)); w.Write(0x4E4F534Au);
        w.Write(jsonBytes);
        for (int i = 0; i < jsonPad; i++) w.Write((byte)' ');
        w.Write((uint)(bin.Length + binPad)); w.Write(0x004E4942u);
        w.Write(bin);
        for (int i = 0; i < binPad; i++) w.Write((byte)0);
    }
}
