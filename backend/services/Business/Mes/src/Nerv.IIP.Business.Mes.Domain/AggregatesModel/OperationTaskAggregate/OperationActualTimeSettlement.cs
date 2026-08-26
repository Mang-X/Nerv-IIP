using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

public partial record OperationActualTimeSettlementId : IGuidStronglyTypedId;
public partial record OperationActualTimeSettlementReportId : IGuidStronglyTypedId;

public sealed class OperationActualTimeSettlement : Entity<OperationActualTimeSettlementId>
{
    private readonly List<OperationActualTimeSettlementReport> _coveredReports = [];

    private OperationActualTimeSettlement()
    {
    }

    private OperationActualTimeSettlement(OperationActualTimeSettlementSnapshot snapshot)
    {
        OrganizationId = snapshot.OrganizationId;
        EnvironmentId = snapshot.EnvironmentId;
        WorkOrderId = snapshot.WorkOrderId;
        OperationTaskId = snapshot.OperationTaskId;
        WorkCenterId = snapshot.WorkCenterId;
        Revision = snapshot.SettlementRevision;
        CompletedAtUtc = snapshot.CompletedAtUtc;
        ActualLaborTicks = snapshot.ActualLaborTicks;
        ActualMachineTicks = snapshot.ActualMachineTicks;
        _coveredReports.AddRange(snapshot.CoveredProductionReportNos.Select(reportNo =>
            OperationActualTimeSettlementReport.Create(
                snapshot.OrganizationId,
                snapshot.EnvironmentId,
                snapshot.WorkOrderId,
                snapshot.OperationTaskId,
                reportNo)));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public long Revision { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public long ActualLaborTicks { get; private set; }
    public long ActualMachineTicks { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public IReadOnlyCollection<OperationActualTimeSettlementReport> CoveredReports => _coveredReports;

    public static OperationActualTimeSettlement Capture(OperationActualTimeSettlementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SettlementRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Settlement revision must be positive.");
        }

        return new OperationActualTimeSettlement(snapshot);
    }

    public OperationActualTimeSettlementSnapshot Snapshot() =>
        new(
            OrganizationId,
            EnvironmentId,
            WorkOrderId,
            OperationTaskId,
            WorkCenterId,
            Revision,
            CompletedAtUtc,
            ActualLaborTicks,
            ActualMachineTicks,
            _coveredReports.Select(x => x.ReportNo).Order(StringComparer.Ordinal).ToArray());

    public void Void(DateTimeOffset voidedAtUtc)
    {
        if (VoidedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Actual-time settlement is already voided.");
        }

        if (voidedAtUtc < CompletedAtUtc)
        {
            throw new InvalidOperationException("Actual-time settlement cannot be voided before its completion time.");
        }

        VoidedAtUtc = voidedAtUtc;
    }
}

public sealed class OperationActualTimeSettlementReport : Entity<OperationActualTimeSettlementReportId>
{
    private OperationActualTimeSettlementReport()
    {
    }

    private OperationActualTimeSettlementReport(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string reportNo)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = DomainGuard.Required(operationTaskId, nameof(operationTaskId));
        ReportNo = DomainGuard.Required(reportNo, nameof(reportNo));
    }

    public OperationActualTimeSettlementId SettlementId { get; private set; } = default!;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskId { get; private set; } = string.Empty;
    public string ReportNo { get; private set; } = string.Empty;

    internal static OperationActualTimeSettlementReport Create(
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string reportNo) =>
        new(organizationId, environmentId, workOrderId, operationTaskId, reportNo);
}
