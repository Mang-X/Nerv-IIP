using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;

public partial record DefectRecordId : IGuidStronglyTypedId;

public sealed class DefectRecord : Entity<DefectRecordId>, IAggregateRoot
{
    public const string OpenStatus = "Open";
    public const string ReworkPendingStatus = "ReworkPending";
    public const string ScrapAcceptedStatus = "ScrapAccepted";
    public const string ReturnAcceptedStatus = "ReturnAccepted";
    public const string DispositionAcceptedStatus = "DispositionAccepted";
    // Keep these wire values in sync with Nerv.IIP.Contracts.Quality.QualityNcrDispositionTypes.
    private const string ReworkDispositionType = "rework";
    private const string ScrapDispositionType = "scrap";
    private const string ReturnToSupplierDispositionType = "return-to-supplier";
    private const string ConditionalReleaseDispositionType = "conditional-release";
    private const string SortAndScreenDispositionType = "sort-and-screen";

    private DefectRecord()
    {
    }

    private DefectRecord(
        string organizationId,
        string environmentId,
        string defectNo,
        string workOrderId,
        string? operationTaskId,
        string defectCode,
        decimal quantity,
        DateTimeOffset recordedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        DefectNo = DomainGuard.Required(defectNo, nameof(defectNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = string.IsNullOrWhiteSpace(operationTaskId) ? null : operationTaskId.Trim();
        DefectCode = DomainGuard.Required(defectCode, nameof(defectCode));
        Quantity = DomainGuard.Positive(quantity, nameof(quantity));
        Status = OpenStatus;
        RecordedAtUtc = recordedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DefectNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string? OperationTaskId { get; private set; }
    public string DefectCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public string? NcrId { get; private set; }
    public string? NcrCode { get; private set; }
    public string? DispositionType { get; private set; }
    public string? DispositionReferenceId { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public static DefectRecord Create(
        string organizationId,
        string environmentId,
        string defectNo,
        string workOrderId,
        string? operationTaskId,
        string defectCode,
        decimal quantity,
        DateTimeOffset recordedAtUtc)
    {
        var defect = new DefectRecord(
            organizationId,
            environmentId,
            defectNo,
            workOrderId,
            operationTaskId,
            defectCode,
            quantity,
            recordedAtUtc);
        defect.AddDomainEvent(new DefectRaisedDomainEvent(defect));
        return defect;
    }

    /// <summary>
    /// 接受 Quality 的处置结论。
    /// </summary>
    /// <param name="dispositionReferenceId">
    /// 下游处置引用（返修工单号 / 报废流水号 / 退供单号），**原样落库，本方法不再归一化**。
    ///
    /// #3318：这一列的长度守卫在调用方（<c>NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect</c>）
    /// 落库**之前**判长并走死信。守卫的前提是「**量的那个字符串就是落库的那个字符串**」。
    /// 此前调用方 Trim 一次、这里再 Trim 一次，是两处归一化：今天因为 Trim 幂等所以等价，
    /// 但只要哪一侧的归一化将来分叉（比如再去零宽字符），守卫量的与落库的就不是同一个串了，
    /// 而**没有任何东西会红**。因此归一化只留调用方那一处，这里原样赋值——
    /// 于是「守卫量的 == 落库的」不再依赖任何幂等约定，而是构造上成立。
    /// 这条「本方法不再归一化」由 <c>MesDefectDispositionReferenceIdLengthContractTests</c> 写成断言，
    /// 谁把 Trim 加回来就会红。
    /// </param>
    public void AcceptDisposition(
        string ncrId,
        string ncrCode,
        string dispositionType,
        string? dispositionReferenceId,
        DateTimeOffset changedAtUtc)
    {
        NcrId = DomainGuard.Required(ncrId, nameof(ncrId));
        NcrCode = DomainGuard.Required(ncrCode, nameof(ncrCode));
        DispositionType = DomainGuard.Required(dispositionType, nameof(dispositionType));
        DispositionReferenceId = dispositionReferenceId;
        Status = DispositionType.Trim().ToLowerInvariant() switch
        {
            ReworkDispositionType => ReworkPendingStatus,
            ScrapDispositionType => ScrapAcceptedStatus,
            ReturnToSupplierDispositionType => ReturnAcceptedStatus,
            ConditionalReleaseDispositionType or SortAndScreenDispositionType => DispositionAcceptedStatus,
            _ => DispositionAcceptedStatus,
        };
        ClosedAtUtc = Status == ReworkPendingStatus ? null : changedAtUtc;
    }
}
