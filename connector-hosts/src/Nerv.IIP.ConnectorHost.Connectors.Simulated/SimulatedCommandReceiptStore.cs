using System.Collections.ObjectModel;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated;

public sealed class SimulatedCommandReceiptStore
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Dictionary<string, ConnectorOperationExecution> _executions =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();

    public SimulatedCommandReceiptStore(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _executions.Count;
            }
        }
    }

    public IReadOnlyList<string> OperationTaskIds
    {
        get
        {
            lock (_gate)
            {
                return _insertionOrder.ToArray();
            }
        }
    }

    public bool TryGet(
        string operationTaskId,
        out ConnectorOperationExecution execution)
    {
        lock (_gate)
        {
            return _executions.TryGetValue(operationTaskId, out execution!);
        }
    }

    public ConnectorOperationExecution Store(
        string operationTaskId,
        ConnectorOperationExecution execution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationTaskId);
        ArgumentNullException.ThrowIfNull(execution);
        lock (_gate)
        {
            if (_executions.TryGetValue(operationTaskId, out var existing))
            {
                return existing;
            }

            if (_executions.Count == _capacity)
            {
                var oldest = _insertionOrder.Dequeue();
                _executions.Remove(oldest);
            }

            var immutable = execution with
            {
                Output = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(
                        execution.Output,
                        StringComparer.Ordinal))
            };
            _executions.Add(operationTaskId, immutable);
            _insertionOrder.Enqueue(operationTaskId);
            return immutable;
        }
    }
}
