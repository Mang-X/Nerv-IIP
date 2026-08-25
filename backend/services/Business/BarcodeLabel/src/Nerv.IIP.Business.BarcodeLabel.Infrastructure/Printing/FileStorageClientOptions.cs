namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

public sealed class FileStorageClientOptions
{
    public const string SectionName = "FileStorage";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
