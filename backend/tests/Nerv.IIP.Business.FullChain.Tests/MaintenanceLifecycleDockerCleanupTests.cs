namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class MaintenanceLifecycleDockerCleanupTests
{
    [Fact]
    public void Run_identity_remains_unique_when_created_concurrently()
    {
        const int identityCount = 4096;
        var identities = Enumerable.Range(0, identityCount)
            .AsParallel()
            .Select(_ => MaintenanceLifecycleDockerRunIdentity.Create())
            .ToArray();

        Assert.Equal(identityCount, identities.Select(identity => identity.RunId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(identities, identity =>
        {
            Assert.Matches("^[0-9a-f]+$", identity.RunId);
            Assert.InRange(identity.RunId.Length, 16, 32);
            Assert.EndsWith(identity.RunId, identity.PostgresContainerName, StringComparison.Ordinal);
            Assert.EndsWith(identity.RunId, identity.RedisContainerName, StringComparison.Ordinal);
            Assert.EndsWith(identity.RunId, identity.PostgresVolumeName, StringComparison.Ordinal);
            Assert.EndsWith(identity.RunId, identity.RedisVolumeName, StringComparison.Ordinal);
            Assert.EndsWith(identity.RunId, identity.OwnershipLabel, StringComparison.Ordinal);
            Assert.EndsWith(identity.RunId, identity.DatabaseName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Cleanup_attempts_every_owned_resource_and_residue_scan_when_failures_occur()
    {
        var attempts = new List<string>();

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            DockerOwnedResourceCleanup.CleanupAsync(
                ["redis", "postgres"],
                resourceName =>
                {
                    attempts.Add(resourceName);
                    return resourceName == "redis"
                        ? Task.FromException(new InvalidOperationException("redis removal failed"))
                        : Task.CompletedTask;
                },
                () =>
                {
                    attempts.Add("residue-scan");
                    return Task.FromException(new InvalidOperationException("residue found"));
                }));

        Assert.Equal(["redis", "postgres", "residue-scan"], attempts);
        Assert.Collection(
            exception.InnerExceptions,
            error => Assert.Equal("redis removal failed", error.Message),
            error => Assert.Equal("residue found", error.Message));
    }
}

internal static class DockerOwnedResourceCleanup
{
    public static async Task CleanupAsync(
        IReadOnlyCollection<string> resourceNames,
        Func<string, Task> removeAsync,
        Func<Task> assertNoResidueAsync)
    {
        List<Exception> failures = [];
        foreach (var resourceName in resourceNames)
        {
            try
            {
                await removeAsync(resourceName);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            await assertNoResidueAsync();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("One or more owned Docker resources could not be cleaned up.", failures);
        }
    }
}
