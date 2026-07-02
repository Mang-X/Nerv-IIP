using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

public partial record MaterialIssueRequestId : IGuidStronglyTypedId;

public sealed class MaterialIssueRequest : Entity<MaterialIssueRequestId>, IAggregateRoot
{
    public const string UnspecifiedUomCode = "UNSPECIFIED";
    public const string RequestedStatus = "Requested";
    public const string PartiallyReceivedStatus = "PartiallyReceived";
    public const string ReceivedStatus = "Received";
    public const int FailureMessageMaxLength = 500;

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
    public string? InventoryPostingFailureCode { get; private set; }
    public string? InventoryPostingFailureMessage { get; private set; }
    public DateTimeOffset? InventoryPostingFailedAtUtc { get; private set; }
    public string? InventoryPostingRollbackKey { get; private set; }

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
        InventoryPostingFailureCode = null;
        InventoryPostingFailureMessage = null;
        InventoryPostingFailedAtUtc = null;
        InventoryPostingRollbackKey = null;
        AddDomainEvent(new MaterialIssueRequestedDomainEvent(this, quantity));
        AddDomainEvent(new MaterialLineSideReceiptConfirmedDomainEvent(this, quantity));
    }

    public void MarkInventoryPostingFailed(
        decimal rollbackQuantity,
        string failureCode,
        string failureMessage,
        DateTimeOffset failedAtUtc,
        string? rollbackKey = null)
    {
        if (rollbackQuantity > 0m &&
            ReceivedAtUtc is not null &&
            failedAtUtc < ReceivedAtUtc.Value)
        {
            return;
        }

        var normalizedRollbackKey = string.IsNullOrWhiteSpace(rollbackKey) ? null : rollbackKey.Trim();
        var shouldRollback = rollbackQuantity > 0m &&
            ReceivedQuantity > 0m &&
            (normalizedRollbackKey is null ||
                !string.Equals(InventoryPostingRollbackKey, normalizedRollbackKey, StringComparison.OrdinalIgnoreCase));

        if (shouldRollback)
        {
            ReceivedQuantity = Math.Max(0m, ReceivedQuantity - rollbackQuantity);
            if (ReceivedQuantity == 0m)
            {
                ReceivedAtUtc = null;
                MaterialLotId = null;
                Status = RequestedStatus;
            }
            else
            {
                Status = ReceivedQuantity >= RequestedQuantity ? ReceivedStatus : PartiallyReceivedStatus;
            }

            InventoryPostingRollbackKey = normalizedRollbackKey;
        }

        InventoryPostingFailureCode = DomainGuard.Required(failureCode, nameof(failureCode));
        InventoryPostingFailureMessage = NormalizeFailureMessage(failureMessage);
        InventoryPostingFailedAtUtc = failedAtUtc;
    }

    public void ReturnLineSideMaterial(DateTimeOffset returnedAtUtc, decimal returnedQuantity, decimal consumedQuantity = 0m)
    {
        DomainGuard.Positive(returnedQuantity, nameof(returnedQuantity));
        DomainGuard.NonNegative(consumedQuantity, nameof(consumedQuantity));

        if (string.IsNullOrWhiteSpace(MaterialLotId))
        {
            throw new InvalidOperationException("Line-side material return requires a received material lot.");
        }

        var returnableQuantity = Math.Max(0m, ReceivedQuantity - consumedQuantity);
        if (returnedQuantity > returnableQuantity)
        {
            throw new InvalidOperationException("退料数量不能超过当前线边可退数量。");
        }

        var returnedMaterialLotId = MaterialLotId;
        ReceivedQuantity -= returnedQuantity;
        if (ReceivedQuantity == 0m)
        {
            ReceivedAtUtc = null;
            MaterialLotId = null;
            Status = RequestedStatus;
        }
        else
        {
            ReceivedAtUtc = returnedAtUtc;
            Status = ReceivedQuantity >= RequestedQuantity ? ReceivedStatus : PartiallyReceivedStatus;
        }

        AddDomainEvent(new MaterialLineSideReturnRequestedDomainEvent(this, returnedQuantity, returnedMaterialLotId, returnedAtUtc));
        AddDomainEvent(new MaterialReturnedToWarehouseDomainEvent(this, returnedQuantity, returnedMaterialLotId, returnedAtUtc));
    }

    private static string NormalizeFailureMessage(string failureMessage)
    {
        var normalized = DomainGuard.Required(failureMessage, nameof(failureMessage));
        return normalized.Length <= FailureMessageMaxLength
            ? normalized
            : normalized[..FailureMessageMaxLength];
    }
}
