using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

public sealed class TusUploadProvider : IFileStorageUploadProvider
{
    public const string Name = "tus";

    public string Provider => Name;
    public string UploadMode => Name;

    public TransferInstructions CreateUploadInstructions(string uploadSessionId, string fileId)
    {
        return new TransferInstructions(
            $"/api/files/v1/tus/{uploadSessionId}",
            new Dictionary<string, string>
            {
                ["x-nerv-upload-mode"] = Name
            });
    }
}
