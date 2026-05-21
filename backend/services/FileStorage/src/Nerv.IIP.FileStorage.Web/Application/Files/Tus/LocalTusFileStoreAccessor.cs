namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public interface ILocalTusFileStoreAccessor
{
    bool TryGet(out LocalTusFileStore store);
}

public sealed class LocalTusFileStoreAccessor(IConfiguration configuration) : ILocalTusFileStoreAccessor
{
    private readonly object syncRoot = new();
    private LocalTusFileStore? store;

    public bool TryGet(out LocalTusFileStore store)
    {
        if (!string.Equals(configuration["FileStorage:UploadProvider"], "tus", StringComparison.OrdinalIgnoreCase))
        {
            store = null!;
            return false;
        }

        if (this.store is null)
        {
            lock (syncRoot)
            {
                this.store ??= new LocalTusFileStore(configuration);
            }
        }

        store = this.store;
        return true;
    }
}
