namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

public partial record OperationTaskParticipantId : IGuidStronglyTypedId;

public sealed class OperationTaskParticipant : Entity<OperationTaskParticipantId>
{
    private OperationTaskParticipant()
    {
    }

    private OperationTaskParticipant(
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workerId,
        string? workerName,
        decimal sharePercent)
    {
        OrganizationId = RequiredWithin(organizationId, nameof(organizationId), 100);
        EnvironmentId = RequiredWithin(environmentId, nameof(environmentId), 100);
        OperationTaskId = RequiredWithin(operationTaskId, nameof(operationTaskId), 100);
        WorkerId = RequiredWithin(workerId, nameof(workerId), 100);
        WorkerName = OptionalWithin(workerName, nameof(workerName), 200);
        SharePercent = sharePercent is > 0m and <= 100m && decimal.Round(sharePercent, 4) == sharePercent
            ? sharePercent
            : throw new ArgumentOutOfRangeException(nameof(sharePercent), "Participant share must be greater than zero, no more than 100 percent, and use at most four decimal places.");
    }

    public string OrganizationId { get; private set; } = string.Empty;

    public string EnvironmentId { get; private set; } = string.Empty;

    public string OperationTaskId { get; private set; } = string.Empty;

    public string WorkerId { get; private set; } = string.Empty;

    public string? WorkerName { get; private set; }

    public decimal SharePercent { get; private set; }

    public static OperationTaskParticipant Register(
        string organizationId,
        string environmentId,
        string operationTaskId,
        string workerId,
        string? workerName,
        decimal sharePercent) =>
        new(organizationId, environmentId, operationTaskId, workerId, workerName, sharePercent);

    private static string RequiredWithin(string value, string parameterName, int maximumLength)
    {
        var normalized = DomainGuard.Required(value, parameterName);
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
    }

    private static string? OptionalWithin(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
    }
}
