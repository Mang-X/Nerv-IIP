using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

[Collection(ConnectorTimeoutCollection.Name)]
public sealed class DockerContainerLifecycleTests
{
    [Fact(Timeout = ConnectorTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task Create_failure_after_daemon_materializes_container_still_removes_exact_resource()
    {
        const string containerName = "nerv-iip-create-failure";
        var docker = new FakeDockerCommands(containerName, materializeBeforeCreateFailure: true);
        var bodyRan = false;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DockerContainerLifecycle.RunAsync(
                containerName,
                docker.RunAsync,
                () =>
                {
                    bodyRan = true;
                    return Task.CompletedTask;
                },
                delayAsync: docker.DelayAsync));

        Assert.Same(docker.CreateFailure, exception);
        Assert.False(bodyRan);
        Assert.False(docker.TargetExists);
        Assert.True(docker.UnrelatedContainerExists);
    }

    [Fact(Timeout = ConnectorTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task Cleanup_keeps_sweeping_after_initial_absence_and_removes_delayed_container()
    {
        const string containerName = "nerv-iip-delayed-create";
        var docker = new FakeDockerCommands(containerName, materializeAfterFirstCleanupSweep: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DockerContainerLifecycle.RunAsync(
                containerName,
                docker.RunAsync,
                () => Task.CompletedTask,
                delayAsync: docker.DelayAsync));

        Assert.Same(docker.CreateFailure, exception);
        Assert.True(docker.DelayedContainerMaterialized);
        Assert.False(docker.TargetExists);
        Assert.True(docker.UnrelatedContainerExists);
    }

    private sealed class FakeDockerCommands
    {
        private const string UnrelatedContainerName = "unrelated-container";
        private readonly string _targetContainerName;
        private readonly bool _materializeBeforeCreateFailure;
        private readonly bool _materializeAfterFirstCleanupSweep;
        private readonly HashSet<string> _containers = [UnrelatedContainerName];
        private int _delayCount;

        public FakeDockerCommands(
            string targetContainerName,
            bool materializeBeforeCreateFailure = false,
            bool materializeAfterFirstCleanupSweep = false)
        {
            _targetContainerName = targetContainerName;
            _materializeBeforeCreateFailure = materializeBeforeCreateFailure;
            _materializeAfterFirstCleanupSweep = materializeAfterFirstCleanupSweep;
        }

        public InvalidOperationException CreateFailure { get; } = new("docker create transport failed");
        public bool DelayedContainerMaterialized { get; private set; }
        public bool TargetExists => _containers.Contains(_targetContainerName);
        public bool UnrelatedContainerExists => _containers.Contains(UnrelatedContainerName);

        public Task<DockerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            bool allowFailure,
            TimeSpan budget)
        {
            Assert.True(budget > TimeSpan.Zero);

            if (arguments is ["container", "create", "--name", var createName, ..])
            {
                if (_materializeBeforeCreateFailure)
                {
                    _containers.Add(createName);
                }

                throw CreateFailure;
            }

            if (arguments is ["container", "rm", "--force", var removeName])
            {
                Assert.True(allowFailure);
                _containers.Remove(removeName);
                return Task.FromResult(new DockerCommandResult(0, string.Empty));
            }

            if (arguments is ["container", "ls", "--all", "--quiet", "--filter", var filter])
            {
                Assert.False(allowFailure);
                var expectedFilter = $"name=^/{_targetContainerName}$";
                Assert.Equal(expectedFilter, filter);
                var output = TargetExists ? "target-container-id\n" : string.Empty;
                return Task.FromResult(new DockerCommandResult(0, output));
            }

            throw new InvalidOperationException($"Unexpected Docker command: {string.Join(' ', arguments)}");
        }

        public Task DelayAsync(TimeSpan delay)
        {
            Assert.True(delay > TimeSpan.Zero);
            _delayCount++;
            if (_materializeAfterFirstCleanupSweep && _delayCount == 1)
            {
                _containers.Add(_targetContainerName);
                DelayedContainerMaterialized = true;
            }

            return Task.CompletedTask;
        }
    }
}
