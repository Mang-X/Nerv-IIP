using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.Docker;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Ops;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class OperationLoopTests
{
    [Fact]
    public async Task Operation_loop_executes_pending_restart_and_reports_result()
    {
        var ops = new RecordingOpsClient([CreateTask("op-000001", "docker-container-local-demo-001", "lifecycle.restart")]);
        var executor = new SuccessfulRestartExecutor();
        var loop = new ConnectorOperationLoop([executor], ops, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Single(ops.Results);
        Assert.Equal("op-000001", ops.Results.Single().OperationTaskId);
        Assert.Equal("succeeded", ops.Results.Single().ExecutionStatus);
    }

    [Fact]
    public async Task Operation_loop_reports_failed_result_when_executor_throws_and_continues()
    {
        var ops = new RecordingOpsClient([
            CreateTask("op-000001", "throwing-instance", "lifecycle.restart"),
            CreateTask("op-000002", "successful-instance", "lifecycle.restart")
        ]);
        var loop = new ConnectorOperationLoop([new ThrowingThenSuccessfulExecutor()], ops, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, ops.Results.Count);
        Assert.Equal("failed", ops.Results[0].ExecutionStatus);
        Assert.Equal("operation.execution_exception", ops.Results[0].Failure?.Code);
        Assert.Equal("runtime", ops.Results[0].Failure?.Category);
        Assert.True(ops.Results[0].Failure?.Retryable);
        Assert.Equal("succeeded", ops.Results[1].ExecutionStatus);
    }

    [Fact]
    public async Task Operation_loop_reports_unsupported_when_no_executor_can_handle_task()
    {
        var ops = new RecordingOpsClient([CreateTask("op-000001", "unknown-instance", "runtime.inspect")]);
        var loop = new ConnectorOperationLoop([], ops, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Single(ops.Results);
        Assert.Equal("failed", ops.Results.Single().ExecutionStatus);
        Assert.Equal("operation.unsupported", ops.Results.Single().Failure?.Code);
    }

    [Fact]
    public async Task Operation_loop_reports_docker_not_found_for_missing_docker_container()
    {
        var ops = new RecordingOpsClient([CreateTask("op-000001", "docker-container-missing", "lifecycle.restart")]);
        var docker = new DockerConnector(new EmptyDockerCli());
        var loop = new ConnectorOperationLoop([docker], ops, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Single(ops.Results);
        Assert.Equal("failed", ops.Results.Single().ExecutionStatus);
        Assert.Equal("docker.container.not_found", ops.Results.Single().Failure?.Code);
    }

    [Fact]
    public async Task Operation_loop_retries_unsent_result_before_polling_new_work()
    {
        var ops = new RecordingOpsClient([CreateTask("op-000001", "docker-container-local-demo-001", "lifecycle.restart")]);
        ops.SendFailures.Enqueue(new HttpRequestException("Ops unavailable after dispatch"));
        var loop = new ConnectorOperationLoop([new SuccessfulRestartExecutor()], ops, ConnectorHostRuntimeContext.DefaultLocal);

        await Assert.ThrowsAsync<HttpRequestException>(() => loop.RunCycleAsync(CancellationToken.None));
        Assert.Empty(ops.Results);
        Assert.Equal(1, ops.PendingCalls);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Single(ops.Results);
        Assert.Equal("op-000001", ops.Results.Single().OperationTaskId);
        Assert.Equal("succeeded", ops.Results.Single().ExecutionStatus);
        Assert.Equal(1, ops.PendingCalls);
    }

    private static OperationTaskDispatchItem CreateTask(string operationTaskId, string instanceKey, string operationCode)
    {
        return new OperationTaskDispatchItem(
            operationTaskId,
            $"attempt-{operationTaskId}",
            "org-001",
            "env-dev",
            "connector-host-001",
            instanceKey,
            operationCode,
            $"corr-{operationTaskId}",
            new Dictionary<string, string>(),
            $"lease-{operationTaskId}",
            DateTimeOffset.Parse("2026-05-19T00:00:00Z"),
            DateTimeOffset.Parse("2026-05-19T00:05:00Z"),
            1,
            3);
    }

    private sealed class EmptyDockerCli : IDockerCli
    {
        public Task<IReadOnlyList<DockerCliContainer>> ListContainersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DockerCliContainer>>([]);
        }

        public Task<DockerCliCommandResult> RestartContainerAsync(string containerName, int gracePeriodSeconds, TimeSpan commandTimeout, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SuccessfulRestartExecutor : IConnectorOperationExecutor
    {
        public bool CanExecute(OperationTaskDispatchItem task) => task.OperationCode == "lifecycle.restart";

        public Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
        {
            return Task.FromResult(ConnectorOperationExecution.Success(new Dictionary<string, string> { ["message"] = "restart accepted" }));
        }
    }

    private sealed class ThrowingThenSuccessfulExecutor : IConnectorOperationExecutor
    {
        public bool CanExecute(OperationTaskDispatchItem task) => task.OperationCode == "lifecycle.restart";

        public Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
        {
            if (task.InstanceKey == "throwing-instance")
            {
                throw new InvalidOperationException("restart command failed");
            }

            return Task.FromResult(ConnectorOperationExecution.Success(new Dictionary<string, string>()));
        }
    }

    private sealed class RecordingOpsClient(IReadOnlyList<OperationTaskDispatchItem> pendingTasks) : IOpsClient
    {
        public List<OperationResult> Results { get; } = [];
        public Queue<Exception> SendFailures { get; } = [];
        public int PendingCalls { get; private set; }

        public Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default)
        {
            return ClaimOperationTasksAsync(new ClaimOperationTasksRequest(organizationId, environmentId, connectorHostId, take), cancellationToken);
        }

        public Task<PendingOperationTasksResponse> ClaimOperationTasksAsync(ClaimOperationTasksRequest request, CancellationToken cancellationToken = default)
        {
            PendingCalls++;
            return Task.FromResult(new PendingOperationTasksResponse(pendingTasks.Select(task => task with
            {
                OrganizationId = request.OrganizationId,
                EnvironmentId = request.EnvironmentId,
                ConnectorHostId = request.ConnectorHostId
            }).ToList()));
        }

        public Task<OperationTaskResponse> AbandonOperationTaskLeaseAsync(string operationTaskId, AbandonOperationTaskLeaseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> HeartbeatOperationTaskLeaseAsync(string operationTaskId, HeartbeatOperationTaskLeaseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default)
        {
            if (SendFailures.Count > 0)
            {
                throw SendFailures.Dequeue();
            }

            Results.Add(result);
            return Task.CompletedTask;
        }
    }
}
