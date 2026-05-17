namespace Nerv.IIP.Iam.Infrastructure;

public sealed class IamSeedService
{
    public Task SeedAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
