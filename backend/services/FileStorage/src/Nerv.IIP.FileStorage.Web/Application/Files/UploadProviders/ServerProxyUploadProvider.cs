using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

public sealed class ServerProxyUploadProvider : IFileStorageUploadProvider
{
    public const string Name = "server-proxy";

    public string Provider => Name;
    public string UploadMode => Name;

    public TransferInstructions CreateUploadInstructions(string uploadSessionId, string fileId)
    {
        return new TransferInstructions(
            $"/api/files/v1/upload-sessions/{uploadSessionId}/content",
            new Dictionary<string, string>
            {
                ["x-nerv-upload-mode"] = Name,
                ["x-nerv-file-id"] = fileId
            });
    }
}
