using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

public interface IInventoryMovementClient
{
    Task<PostInventoryMovementResult> PostMovementAsync(PostInventoryMovementRequest request, CancellationToken cancellationToken);
}

public sealed record PostInventoryMovementRequest(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity);

public sealed record PostInventoryMovementResult(string InventoryMovementId);

public sealed class NoopInventoryMovementClient : IInventoryMovementClient
{
    public Task<PostInventoryMovementResult> PostMovementAsync(PostInventoryMovementRequest request, CancellationToken _)
    {
        return Task.FromResult(new PostInventoryMovementResult($"pending-{request.MovementType}-{request.IdempotencyKey}"));
    }
}

public static class InventoryMovementRequestMapping
{
    public static PostInventoryMovementRequest ToInventoryPostRequest(this InventoryMovementRequest request)
    {
        return new PostInventoryMovementRequest(
            request.OrganizationId,
            request.EnvironmentId,
            request.MovementType,
            "wms",
            request.SourceDocumentId,
            request.SourceDocumentLineId,
            request.IdempotencyKey,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            request.QualityStatus,
            request.OwnerType,
            request.OwnerId,
            request.Quantity);
    }
}
