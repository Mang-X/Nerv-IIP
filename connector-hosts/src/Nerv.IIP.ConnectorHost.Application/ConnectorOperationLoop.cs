using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Ops;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed class ConnectorOperationLoop(
    IReadOnlyList<IConnectorOperationExecutor> executors,
    IOpsClient opsClient,
    ConnectorHostRuntimeContext runtimeContext)
{
    private readonly object _gate = new();
    private readonly List<OperationResult> _unsentResults = [];

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (await FlushUnsentResultsAsync(cancellationToken))
        {
            return;
        }

        var pending = await opsClient.ClaimOperationTasksAsync(
            new ClaimOperationTasksRequest(
                runtimeContext.OrganizationId,
                runtimeContext.EnvironmentId,
                runtimeContext.ConnectorHostId,
                10),
            cancellationToken);
        foreach (var task in pending.Items)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var execution = await ExecuteAsync(task, cancellationToken);
            var finishedAt = DateTimeOffset.UtcNow;
            var context = new ConnectorRequestContext(runtimeContext.ProtocolVersion, runtimeContext.SdkVersion, task.CorrelationId, finishedAt, task.OrganizationId, task.EnvironmentId, runtimeContext.ConnectorHostId);
            var failure = execution.Succeeded ? null : new FailureReason(execution.FailureCode ?? "operation.failed", execution.FailureMessage ?? "Operation failed.", execution.FailureCategory ?? "runtime", execution.Retryable, new Dictionary<string, string>());
            var result = new OperationResult(context, task.OperationTaskId, task.AttemptId, task.InstanceKey, task.OperationCode, startedAt, finishedAt, execution.Succeeded ? "succeeded" : "failed", failure, execution.Output);
            await SendOrQueueAsync(result, cancellationToken);
        }
    }

    private async Task<bool> FlushUnsentResultsAsync(CancellationToken cancellationToken)
    {
        OperationResult[] results;
        lock (_gate)
        {
            if (_unsentResults.Count == 0)
            {
                return false;
            }

            results = _unsentResults.ToArray();
        }

        foreach (var result in results)
        {
            await opsClient.SendOperationResultAsync(result, cancellationToken);
            lock (_gate)
            {
                _unsentResults.Remove(result);
            }
        }

        return true;
    }

    private async Task SendOrQueueAsync(OperationResult result, CancellationToken cancellationToken)
    {
        try
        {
            await opsClient.SendOperationResultAsync(result, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            lock (_gate)
            {
                _unsentResults.Add(result);
            }

            throw;
        }
    }

    private async Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
    {
        var executor = executors.FirstOrDefault(x => x.CanExecute(task));
        if (executor is null)
        {
            return ConnectorOperationExecution.Failed("operation.unsupported", $"No connector can execute {task.OperationCode} for {task.InstanceKey}.", "validation", false, new Dictionary<string, string>());
        }

        try
        {
            return await executor.ExecuteAsync(task, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ConnectorOperationExecution.Failed("operation.execution_exception", ex.Message, "runtime", true, new Dictionary<string, string>());
        }
    }
}
