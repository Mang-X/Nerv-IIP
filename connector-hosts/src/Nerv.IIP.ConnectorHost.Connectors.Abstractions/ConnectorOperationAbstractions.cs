using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

public interface IConnectorOperationExecutor
{
    bool CanExecute(OperationTaskDispatchItem task);
    Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken);
}

public sealed record ConnectorOperationExecution(
    bool Succeeded,
    string? FailureCode,
    string? FailureMessage,
    string? FailureCategory,
    bool Retryable,
    IReadOnlyDictionary<string, string> Output)
{
    public static ConnectorOperationExecution Success(IReadOnlyDictionary<string, string> output) => new(true, null, null, null, false, output);
    public static ConnectorOperationExecution Failed(string code, string message, string category, bool retryable, IReadOnlyDictionary<string, string> output) => new(false, code, message, category, retryable, output);
}
