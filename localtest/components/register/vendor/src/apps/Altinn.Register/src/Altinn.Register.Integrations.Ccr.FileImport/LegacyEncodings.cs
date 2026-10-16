using System.Text;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Provides access to legacy encodings that may be used in CCR files, such as Latin-9 (ISO-8859-15).
/// </summary>
internal static class LegacyEncodings
{
    static LegacyEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Windows-1252 encoding, which is commonly used in Norwegian CCR files.
    /// </summary>
    public static Encoding Windows1252 { get; }
}
