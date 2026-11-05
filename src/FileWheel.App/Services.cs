using FileWheel.Conversion;

namespace FileWheel.App;

/// <summary>Services partages par les fenetres (application residente ou mode de verification).</summary>
public static class Services
{
    public static ConversionEngine Engine { get; set; } = null!;
}
