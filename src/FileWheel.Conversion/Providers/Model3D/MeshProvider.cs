using FileWheel.Conversion.Core;

namespace FileWheel.Conversion.Providers.Model3D;

/// <summary>
/// Maillages 3D integres : STL, OBJ, PLY, 3MF, glTF/GLB en lecture ; STL, OBJ, PLY, 3MF, GLB en ecriture.
/// Geometrie seule (positions + triangles). STEP, IGES et FBX ne sont PAS pris en charge :
/// ce sont des formats CAO/proprietaires qui demandent FreeCAD ou Blender (non integres a cette version).
/// </summary>
public sealed class MeshProvider : ProviderBase
{
    public override string Id => "mesh";
    public override string DisplayName => "3D (intégré)";

    private static readonly string[] Inputs = ["stl", "obj", "ply", "3mf", "glb", "gltf"];
    private static readonly string[] Outputs = ["stl", "obj", "ply", "3mf", "glb"];

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } =
        Outputs.Select(to => Cap("mesh", Inputs.Where(i => i != to), to, ConversionKind.Convert, [], LossKind.Structure,
            "Géométrie seule : matériaux, couleurs, textures, normales et animations ne sont pas conservés.",
            to == "3mf" ? "Unité 3MF : millimètre (les fichiers STL/OBJ n'ont pas d'unité, les valeurs sont recopiées telles quelles)." : null,
            priority: 10)).ToList();

    public override ConversionAnalysis Analyze(ConversionRequest request)
    {
        var a = base.Analyze(request);
        try
        {
            if (new FileInfo(request.Input).Length < 200L << 20)
            {
                var mesh = Mesh.Read(request.Input, request.SourceFormat.Id, CancellationToken.None);
                a.Notes.Add(mesh.Describe());
                a.Notes.AddRange(mesh.Notes);
            }
        }
        catch (ConversionException ex) { a.Warnings.Add(ex.Message); a.Blocking = true; }
        catch (Exception ex) { a.Warnings.Add("Modèle illisible : " + ex.Message); a.Blocking = true; }
        return a;
    }

    public override Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            progress.Report(new ConversionProgress(null, "Lecture du modèle…"));
            var mesh = Mesh.Read(req.Input, req.SourceFormat.Id, ct);
            if (mesh.TriangleCount == 0) throw new ConversionException(ConversionErrorKind.InvalidInput, "Le modèle ne contient aucun triangle.");
            ct.ThrowIfCancellationRequested();
            progress.Report(new ConversionProgress(0.6, "Écriture…"));
            mesh.Write(req.TempOutput, req.TargetFormat);
            var notes = new List<string>(mesh.Notes) { mesh.Describe() };
            return new ConversionResult([req.TempOutput], notes);
        }, ct);
}
