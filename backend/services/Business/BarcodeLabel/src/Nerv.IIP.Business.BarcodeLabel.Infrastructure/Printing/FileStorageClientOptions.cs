namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;

public sealed class FileStorageClientOptions
{
    public const string SectionName = "FileStorage";
    public const string DownloadClientName = "BarcodeLabelTemplateDownload";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
