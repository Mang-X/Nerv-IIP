namespace Nerv.IIP.Notification.Domain.ObservabilityAlerts;

public sealed record DatabaseWatermarkReadRequest(
    string ConnectionStringName,
    string MetricName,
    double? CapacityMegabytes);

public interface IDatabaseWatermarkReader
{
    Task<double?> ReadPercentAsync(DatabaseWatermarkReadRequest request, CancellationToken cancellationToken);
}

public sealed class DatabaseWatermarkReadException(string message, Exception? innerException = null)
    : Exception(message, innerException);
