using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

public partial record ProductionReportLaborAllocationId : IGuidStronglyTypedId;

public sealed class ProductionReportLaborAllocation : Entity<ProductionReportLaborAllocationId>
{
    private ProductionReportLaborAllocation()
    {
    }

    private ProductionReportLaborAllocation(
        string organizationId,
        string environmentId,
        string reportNo,
        string workOrderId,
        string operationTaskId,
        string workerId,
        string? workerName,
        decimal sharePercent,
        long allocatedLaborTicks)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ReportNo = reportNo;
        WorkOrderId = workOrderId;
        OperationTaskId = operationTaskId;
        WorkerId = workerId;
        WorkerName = workerName;
        SharePercent = sharePercent;
        AllocatedLaborTicks = allocatedLaborTicks;
    }

    public string OrganizationId { get; private set; } = string.Empty;

    public string EnvironmentId { get; private set; } = string.Empty;

    public string ReportNo { get; private set; } = string.Empty;

    public string WorkOrderId { get; private set; } = string.Empty;

    public string OperationTaskId { get; private set; } = string.Empty;

    public string WorkerId { get; private set; } = string.Empty;

    public string? WorkerName { get; private set; }

    public decimal SharePercent { get; private set; }

    public long AllocatedLaborTicks { get; private set; }

    public static IReadOnlyList<ProductionReportLaborAllocation> Allocate(
        string organizationId,
        string environmentId,
        string reportNo,
        string workOrderId,
        string operationTaskId,
        long laborTimeTicks,
        IReadOnlyCollection<OperationTaskParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (laborTimeTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(laborTimeTicks));
        }

        var ordered = participants
            .OrderBy(x => x.WorkerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        if (ordered.Select(x => x.WorkerId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Length)
        {
            throw new ArgumentException("Operation task participants must have unique worker ids.", nameof(participants));
        }

        if (ordered.Any(x =>
                !string.Equals(x.OrganizationId, organizationId, StringComparison.Ordinal) ||
                !string.Equals(x.EnvironmentId, environmentId, StringComparison.Ordinal) ||
                !string.Equals(x.OperationTaskId, operationTaskId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Operation task participants must belong to the reported operation scope.", nameof(participants));
        }

        if (ordered.Sum(x => x.SharePercent) != 100m)
        {
            throw new ArgumentException("Operation task participant shares must total 100 percent.", nameof(participants));
        }

        var proportionalShares = ordered
            .Select((participant, index) =>
            {
                var exactTicks = laborTimeTicks * participant.SharePercent / 100m;
                return new
                {
                    Participant = participant,
                    Index = index,
                    FloorTicks = decimal.ToInt64(decimal.Floor(exactTicks)),
                    Fraction = exactTicks - decimal.Floor(exactTicks),
                };
            })
            .ToArray();
        var remainingTicks = checked(laborTimeTicks - proportionalShares.Sum(x => x.FloorTicks));
        var bonusIndexes = proportionalShares
            .OrderByDescending(x => x.Fraction)
            .ThenBy(x => x.Participant.WorkerId, StringComparer.OrdinalIgnoreCase)
            .Take(checked((int)remainingTicks))
            .Select(x => x.Index)
            .ToHashSet();

        var result = new List<ProductionReportLaborAllocation>(ordered.Length);
        foreach (var share in proportionalShares)
        {
            var participantTicks = checked(share.FloorTicks + (bonusIndexes.Contains(share.Index) ? 1L : 0L));
            result.Add(new ProductionReportLaborAllocation(
                organizationId,
                environmentId,
                reportNo,
                workOrderId,
                operationTaskId,
                share.Participant.WorkerId,
                share.Participant.WorkerName,
                share.Participant.SharePercent,
                participantTicks));
        }

        return result;
    }
}
