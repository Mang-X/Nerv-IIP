namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public interface ILocalTusFileStoreAccessor
{
    bool TryGet(out LocalUploadByteStore store);
}

public sealed class LocalTusFileStoreAccessor(IConfiguration configuration) : ILocalTusFileStoreAccessor
{
    private readonly object syncRoot = new();
    private LocalUploadByteStore? store;

    public bool TryGet(out LocalUploadByteStore store)
    {
        if (!string.Equals(configuration["FileStorage:UploadProvider"] ?? "tus", "tus", StringComparison.OrdinalIgnoreCase))
        {
            store = null!;
            return false;
        }

        if (this.store is null)
        {
            lock (syncRoot)
            {
                this.store ??= new LocalUploadByteStore(configuration);
            }
        }

        store = this.store;
        return true;
    }
}
