using System.Reflection;
using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Connectors.Docker.Tests;

[Collection(ConnectorTimeoutCollection.Name)]
public sealed class DockerContainerLifecycleTests
{
    [Fact(Timeout = ConnectorTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task Integration_timeout_covers_full_internal_budget_after_create_failure_still_cleans_up()
    {
        const string containerName = "nerv-iip-budgeted-create-failure";
        var docker = new FakeDockerCommands(
            containerName,
            materializeBeforeCreateFailure: true,
            consumeCommandBudgets: true,
            elapsedBeforeLifecycle: TimeSpan.FromSeconds(60));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DockerContainerLifecycle.RunAsync(
                containerName,
                docker.RunAsync,
                () => Task.CompletedTask,
                delayAsync: docker.DelayAsync));

        Assert.Same(docker.CreateFailure, exception);
        Assert.True(docker.CleanupRan);
        Assert.False(docker.TargetExists);
        Assert.True(docker.Elapsed > TimeSpan.FromMilliseconds(ConnectorTimeoutCollection.TestTimeoutMilliseconds));
    }

    [Fact]
    public void Real_Docker_integration_fact_timeout_is_480_seconds_and_exceeds_maximum_internal_budget()
    {
        var integrationMethod = typeof(DockerCliIntegrationTests).GetMethod(
            nameof(DockerCliIntegrationTests.Docker_connector_discovers_and_restarts_a_real_container),
            BindingFlags.Instance | BindingFlags.Public);
        var integrationFact = integrationMethod?.GetCustomAttribute<DockerCliFactAttribute>();

        Assert.NotNull(integrationFact);
        Assert.Equal(480_000, integrationFact.Timeout);

        var integrationTimeout = TimeSpan.FromMilliseconds(integrationFact.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(404), DockerCliIntegrationBudget.MaximumInternalBudget);
        Assert.True(
            integrationTimeout > DockerCliIntegrationBudget.MaximumInternalBudget,
            $"Docker integration timeout {integrationTimeout} must strictly exceed the 404s internal worst-case budget.");
    }

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

    [Fact(Timeout = ConnectorTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task Cleanup_confirms_stable_absence_when_container_materializes_after_empty_observation()
    {
        const string containerName = "nerv-iip-after-empty-create";
        var docker = new FakeDockerCommands(containerName, materializeAfterFirstEmptyObservation: true);

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

    [Fact(Timeout = ConnectorTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task Cleanup_runs_all_five_sweeps_when_container_materializes_after_third_empty_snapshot()
    {
        const string containerName = "nerv-iip-after-third-empty-snapshot";
        var docker = new FakeDockerCommands(containerName, materializeAfterThirdEmptySnapshot: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DockerContainerLifecycle.RunAsync(
                containerName,
                docker.RunAsync,
                () => Task.CompletedTask,
                delayAsync: docker.DelayAsync));

        Assert.Same(docker.CreateFailure, exception);
        var cleanupException = Assert.IsType<Xunit.Sdk.XunitException>(exception.Data["DockerCleanupException"]);
        Assert.Contains("could not confirm 3 stable exact-name absence observations", cleanupException.Message);
        Assert.True(docker.DelayedContainerMaterialized);
        Assert.Equal(5, docker.CleanupObservationCount);
        Assert.False(docker.TargetExists);
        Assert.True(docker.UnrelatedContainerExists);
    }

    [Fact(Timeout = ConnectorTimeoutCollection.TestTimeoutMilliseconds)]
    public async Task Cleanup_failure_does_not_replace_the_original_scenario_exception()
    {
        const string containerName = "nerv-iip-primary-failure";
        var docker = new FakeDockerCommands(containerName, failCleanupCommands: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DockerContainerLifecycle.RunAsync(
                containerName,
                docker.RunAsync,
                () => Task.CompletedTask,
                delayAsync: docker.DelayAsync));

        Assert.Same(docker.CreateFailure, exception);
        Assert.Same(docker.CleanupFailure, exception.Data["DockerCleanupException"]);
    }

    private sealed class FakeDockerCommands
    {
        private const string UnrelatedContainerName = "unrelated-container";
        private readonly string _targetContainerName;
        private readonly bool _materializeBeforeCreateFailure;
        private readonly bool _materializeAfterFirstCleanupSweep;
        private readonly bool _materializeAfterFirstEmptyObservation;
        private readonly bool _materializeAfterThirdEmptySnapshot;
        private readonly bool _consumeCommandBudgets;
        private readonly bool _failCleanupCommands;
        private readonly HashSet<string> _containers = [UnrelatedContainerName];
        private int _delayCount;
        private bool _materializeOnNextDelay;

        public FakeDockerCommands(
            string targetContainerName,
            bool materializeBeforeCreateFailure = false,
            bool materializeAfterFirstCleanupSweep = false,
            bool materializeAfterFirstEmptyObservation = false,
            bool materializeAfterThirdEmptySnapshot = false,
            bool consumeCommandBudgets = false,
            bool failCleanupCommands = false,
            TimeSpan elapsedBeforeLifecycle = default)
        {
            _targetContainerName = targetContainerName;
            _materializeBeforeCreateFailure = materializeBeforeCreateFailure;
            _materializeAfterFirstCleanupSweep = materializeAfterFirstCleanupSweep;
            _materializeAfterFirstEmptyObservation = materializeAfterFirstEmptyObservation;
            _materializeAfterThirdEmptySnapshot = materializeAfterThirdEmptySnapshot;
            _consumeCommandBudgets = consumeCommandBudgets;
            _failCleanupCommands = failCleanupCommands;
            Elapsed = elapsedBeforeLifecycle;
        }

        public InvalidOperationException CreateFailure { get; } = new("docker create transport failed");
        public InvalidOperationException CleanupFailure { get; } = new("docker cleanup transport failed");
        public bool DelayedContainerMaterialized { get; private set; }
        public bool CleanupRan { get; private set; }
        public int CleanupObservationCount { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public bool TargetExists => _containers.Contains(_targetContainerName);
        public bool UnrelatedContainerExists => _containers.Contains(UnrelatedContainerName);

        public Task<DockerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            bool allowFailure,
            TimeSpan budget)
        {
            Assert.True(budget > TimeSpan.Zero);
            if (_consumeCommandBudgets)
            {
                Elapsed += budget;
            }

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
                CleanupRan = true;
                if (_failCleanupCommands)
                {
                    throw CleanupFailure;
                }

                var removed = _containers.Remove(removeName);
                return Task.FromResult(new DockerCommandResult(0, removed ? removeName : string.Empty));
            }

            if (arguments is ["container", "ls", "--all", "--quiet", "--filter", var filter])
            {
                Assert.False(allowFailure);
                if (_failCleanupCommands)
                {
                    throw CleanupFailure;
                }

                var expectedFilter = $"name=^/{_targetContainerName}$";
                Assert.Equal(expectedFilter, filter);
                CleanupObservationCount++;
                var output = TargetExists ? "target-container-id\n" : string.Empty;
                if (_materializeAfterFirstEmptyObservation
                    && string.IsNullOrEmpty(output)
                    && !DelayedContainerMaterialized)
                {
                    _materializeOnNextDelay = true;
                }

                if (_materializeAfterThirdEmptySnapshot
                    && CleanupObservationCount == 3
                    && string.IsNullOrEmpty(output)
                    && !DelayedContainerMaterialized)
                {
                    _containers.Add(_targetContainerName);
                    DelayedContainerMaterialized = true;
                }

                return Task.FromResult(new DockerCommandResult(0, output));
            }

            throw new InvalidOperationException($"Unexpected Docker command: {string.Join(' ', arguments)}");
        }

        public Task DelayAsync(TimeSpan delay)
        {
            Assert.True(delay > TimeSpan.Zero);
            _delayCount++;
            if (_consumeCommandBudgets)
            {
                Elapsed += delay;
            }

            if (_materializeAfterFirstCleanupSweep && _delayCount == 1)
            {
                _containers.Add(_targetContainerName);
                DelayedContainerMaterialized = true;
            }

            if (_materializeOnNextDelay)
            {
                _containers.Add(_targetContainerName);
                DelayedContainerMaterialized = true;
                _materializeOnNextDelay = false;
            }

            return Task.CompletedTask;
        }
    }
}
