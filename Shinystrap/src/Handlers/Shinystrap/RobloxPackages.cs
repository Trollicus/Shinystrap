namespace Shinystrap.Handlers.Shinystrap;

public class RobloxPackage
{
    public string Name { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;   // MD5
    public long PackedSize { get; set; }
    public long Size { get; set; }         // Unpacked size
}