using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

public partial record MaterialIssueRequestId : IGuidStronglyTypedId;

public sealed class MaterialIssueRequest : Entity<MaterialIssueRequestId>, IAggregateRoot
{
    public const string UnspecifiedUomCode = "UNSPECIFIED";
    public const string RequestedStatus = "Requested";
    public const string PartiallyReceivedStatus = "PartiallyReceived";
    public const string ReceivedStatus = "Received";

    private MaterialIssueRequest()
    {
    }

    private MaterialIssueRequest(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string? operationTaskId,
        string materialId,
        string uomCode,
        decimal requestedQuantity,
        DateTimeOffset requestedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        RequestNo = DomainGuard.Required(requestNo, nameof(requestNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = string.IsNullOrWhiteSpace(operationTaskId) ? null : operationTaskId.Trim();
        MaterialId = DomainGuard.Required(materialId, nameof(materialId));
        UomCode = DomainGuard.Required(uomCode, nameof(uomCode));
        RequestedQuantity = DomainGuard.Positive(requestedQuantity, nameof(requestedQuantity));
        ReceivedQuantity = 0m;
        Status = RequestedStatus;
        RequestedAtUtc = requestedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RequestNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string? OperationTaskId { get; private set; }
    public string MaterialId { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string? MaterialLotId { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }

    public static MaterialIssueRequest Create(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string? operationTaskId,
        string materialId,
        string uomCode,
        decimal requestedQuantity,
        DateTimeOffset requestedAtUtc)
    {
        var request = new MaterialIssueRequest(
            organizationId,
            environmentId,
            requestNo,
            workOrderId,
            operationTaskId,
            materialId,
            uomCode,
            requestedQuantity,
            requestedAtUtc);
        return request;
    }

    public void ConfirmLineSideReceipt(DateTimeOffset receivedAtUtc, decimal? receivedQuantity = null, string? materialLotId = null)
    {
        var quantity = receivedQuantity ?? RequestedQuantity - ReceivedQuantity;
        DomainGuard.Positive(quantity, nameof(receivedQuantity));
        if (ReceivedQuantity + quantity > RequestedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedQuantity), "Received quantity cannot exceed requested quantity.");
        }

        var normalizedMaterialLotId = string.IsNullOrWhiteSpace(materialLotId) ? null : materialLotId.Trim();
        if (!string.IsNullOrWhiteSpace(MaterialLotId) &&
            !string.IsNullOrWhiteSpace(normalizedMaterialLotId) &&
            !string.Equals(MaterialLotId, normalizedMaterialLotId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("同一领料申请不能混用多个物料批次。");
        }

        MaterialLotId = normalizedMaterialLotId ?? MaterialLotId;
        ReceivedQuantity += quantity;
        ReceivedAtUtc = receivedAtUtc;
        Status = ReceivedQuantity >= RequestedQuantity ? ReceivedStatus : PartiallyReceivedStatus;
        AddDomainEvent(new MaterialIssueRequestedDomainEvent(this, quantity));
        AddDomainEvent(new MaterialLineSideReceiptConfirmedDomainEvent(this, quantity));
    }
}
