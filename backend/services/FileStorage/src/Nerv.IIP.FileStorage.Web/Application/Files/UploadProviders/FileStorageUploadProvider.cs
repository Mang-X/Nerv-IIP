using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

public interface IFileStorageUploadProvider
{
    string Provider { get; }
    string UploadMode { get; }
    TransferInstructions CreateUploadInstructions(string uploadSessionId, string fileId);
}
