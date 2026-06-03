namespace Nerv.IIP.PlatformGateway.Web.Application.FileStorage;

internal static class ConsoleFileStorageTransferRoutes
{
    public const string DownstreamTusPrefix = "/api/files/v1/tus/";
    public const string ConsoleTusPrefix = "/api/console/v1/files/tus/";
    public const string DownstreamDownloadGrantPrefix = "/api/files/v1/download-grants/";
    public const string ConsoleDownloadGrantPrefix = "/api/console/v1/files/download-grants/";
}
