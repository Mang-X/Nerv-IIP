namespace Nerv.IIP.FileStorage.Web.Application.Files;

internal static class FileStorageScanPolicy
{
    public const string Clean = "clean";
    public const string Malware = "malware";
    public const string Failed = "failed";
    public const string Pending = "pending";
    public const string Available = "available";

    public static string InitialScanStatus(IConfiguration? configuration)
    {
        return IsScanningEnabled(configuration)
            ? Pending
            : Clean;
    }

    public static bool IsScanningEnabled(IConfiguration? configuration)
    {
        return configuration?.GetValue<bool>("FileStorage:Scanning:Enabled") == true
            || !string.IsNullOrWhiteSpace(configuration?["FileStorage:Scanning:Adapter"]);
    }

    public static bool CanDownload(string scanStatus, string status, IConfiguration? configuration)
    {
        if (!string.Equals(status, Available, StringComparison.Ordinal))
        {
            return false;
        }

        return configuration?.GetValue<bool?>("FileStorage:Scanning:RequireCleanForDownload") != false
            ? string.Equals(scanStatus, Clean, StringComparison.Ordinal)
            : true;
    }
}
