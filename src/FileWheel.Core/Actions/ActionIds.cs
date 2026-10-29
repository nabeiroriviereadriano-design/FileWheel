namespace FileWheel.Core.Actions;

/// <summary>Identifiants des commandes de la roue (executees par l'application).</summary>
public static class ActionIds
{
    public const string ConvertQuickTarget = "convert.target";   // Argument = format cible
    public const string ConvertDialog = "convert.dialog";
    public const string ConvertAdvanced = "convert.advanced";
    public const string ImageResize = "convert.resize";
    public const string Copy = "file.copy";                      // presse-papiers
    public const string CopyTo = "file.copyto";                  // Argument = dossier (vide = choisir)
    public const string MoveTo = "file.moveto";                  // Argument = dossier (vide = choisir)
    public const string Open = "file.open";
    public const string OpenWith = "file.openwith";
    public const string Rename = "file.rename";
    public const string Zip = "archive.zip";
    public const string CompressAs = "archive.compressas";       // Argument = format d'archive
    public const string ExtractHere = "archive.extracthere";
    public const string ExtractTo = "archive.extractto";
    public const string TestArchive = "archive.test";
    public const string Share = "file.share";
    public const string SendTo = "file.sendto";                  // Argument = dossier
    public const string OpenParent = "file.parent";
    public const string Terminal = "file.terminal";
    public const string Properties = "file.properties";
    public const string CopyPath = "file.copypath";
    public const string CopyName = "file.copyname";
    public const string Shortcut = "file.shortcut";
    public const string Recycle = "file.recycle";
    public const string DeletePermanently = "file.delete";
    public const string AddFavorite = "fw.favorite";
    public const string NewCustomAction = "fw.newaction";
    public const string Custom = "fw.custom";                    // Argument = id de l'action perso
    public const string Settings = "fw.settings";

    // Outils image (fenetres d'edition ; le resultat est enregistre a cote de l'original)
    public const string ImageCrop = "image.crop";
    public const string ImageBackground = "image.background";
    public const string ImageCompress = "image.compress";
    public const string ImageAnnotate = "image.annotate";
    public const string ImageMetadata = "image.metadata";
}
