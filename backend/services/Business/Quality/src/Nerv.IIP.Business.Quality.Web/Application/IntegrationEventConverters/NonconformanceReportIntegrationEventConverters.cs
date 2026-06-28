using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

public sealed class NcrOpenedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<NonconformanceReportOpenedDomainEvent, NcrOpenedIntegrationEvent>
{
    public NcrOpenedIntegrationEvent Convert(NonconformanceReportOpenedDomainEvent domainEvent)
    {
        var ncr = domainEvent.NonconformanceReport;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new NcrOpenedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.NcrOpened,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("ncr-opened", ncr.OrganizationId, ncr.EnvironmentId, ncr.NcrCode),
            new NcrOpenedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                ncr.SourceType,
                ncr.SourceDocumentId,
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.DefectReason,
                ncr.BatchNo,
                ncr.SerialNo,
                ncr.Status,
                occurredAtUtc));
    }
}

public sealed class NcrDispositionDecidedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<NonconformanceReportDispositionDecidedDomainEvent, NcrDispositionDecidedIntegrationEvent>
{
    public NcrDispositionDecidedIntegrationEvent Convert(NonconformanceReportDispositionDecidedDomainEvent domainEvent)
    {
        var ncr = domainEvent.NonconformanceReport;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new NcrDispositionDecidedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("ncr-disposition-decided", ncr.OrganizationId, ncr.EnvironmentId, ncr.NcrCode),
            new NcrDispositionDecidedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.DispositionType ?? string.Empty,
                ncr.DispositionApprovalChainId,
                ncr.ReworkWorkOrderId,
                ncr.ScrapMovementId,
                ncr.ReturnDocumentId,
                occurredAtUtc,
                ncr.MrbReviews.Select(x => new MrbReviewPayload(
                    x.ReviewerId,
                    x.Decision,
                    x.Comment,
                    x.ReviewedAtUtc)).ToArray())
            {
                SourceDocumentId = ncr.SourceDocumentId,
                LotNo = ncr.BatchNo,
                SerialNo = ncr.SerialNo,
                UomCode = ncr.UomCode,
                SiteCode = ncr.SiteCode,
                LocationCode = ncr.LocationCode,
                OwnerType = ncr.OwnerType,
                OwnerId = ncr.OwnerId
            });
    }
}

public sealed class NcrInventoryDispositionRequestedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<NonconformanceReportInventoryDispositionRequestedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(NonconformanceReportInventoryDispositionRequestedDomainEvent domainEvent)
    {
        var ncr = domainEvent.NonconformanceReport;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        var dispositionType = ncr.DispositionType ?? throw new InvalidOperationException("NCR disposition type is required for inventory disposition routing.");
        var movementType = dispositionType == QualityNcrDispositionTypes.Scrap
            ? InventoryMovementTypes.Adjustment
            : InventoryMovementRequestTypes.StatusTransfer;
        var idempotencyKey = EventIds.Idempotency(
            "ncr-inventory-disposition",
            ncr.OrganizationId,
            ncr.EnvironmentId,
            ncr.NcrCode,
            dispositionType);
        return new InventoryMovementRequestedIntegrationEvent(
            EventIds.New(),
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            occurredAtUtc,
            InventoryIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            context.Actor,
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                movementType,
                InventoryMovementSourceServices.Quality,
                ncr.Id.ToString(),
                ncr.NcrCode,
                idempotencyKey,
                ncr.SkuCode,
                Required(ncr.UomCode, nameof(ncr.UomCode)),
                Required(ncr.SiteCode, nameof(ncr.SiteCode)),
                Required(ncr.LocationCode, nameof(ncr.LocationCode)),
                ncr.BatchNo,
                ncr.SerialNo,
                InventoryQualityStatuses.Blocked,
                Required(ncr.OwnerType, nameof(ncr.OwnerType)),
                ncr.OwnerId,
                Quantity(ncr),
                occurredAtUtc,
                TargetQualityStatus: TargetQualityStatus(dispositionType)));
    }

    private static decimal Quantity(Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate.NonconformanceReport ncr)
    {
        return ncr.DispositionType == QualityNcrDispositionTypes.Scrap
            ? -Math.Abs(ncr.DefectQuantity)
            : Math.Abs(ncr.DefectQuantity);
    }

    private static string? TargetQualityStatus(string dispositionType)
    {
        return dispositionType switch
        {
            QualityNcrDispositionTypes.Rework or QualityNcrDispositionTypes.ConditionalRelease => InventoryQualityStatuses.Restricted,
            QualityNcrDispositionTypes.Scrap => null,
            _ => throw new InvalidOperationException($"Unsupported inventory NCR disposition '{dispositionType}'."),
        };
    }

    private static string Required(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is required for NCR inventory disposition routing.")
            : value;
    }
}

public sealed class NcrClosedIntegrationEventConverter(IQualityIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<NonconformanceReportClosedDomainEvent, NcrClosedIntegrationEvent>
{
    public NcrClosedIntegrationEvent Convert(NonconformanceReportClosedDomainEvent domainEvent)
    {
        var ncr = domainEvent.NonconformanceReport;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new NcrClosedIntegrationEvent(
            EventIds.New(),
            QualityIntegrationEventTypes.NcrClosed,
            QualityIntegrationEventVersions.V1,
            occurredAtUtc,
            QualityIntegrationEventSources.BusinessQuality,
            context.CorrelationId,
            context.CausationId,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("ncr-closed", ncr.OrganizationId, ncr.EnvironmentId, ncr.NcrCode),
            new NcrClosedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.DispositionType ?? string.Empty,
                ncr.ReworkWorkOrderId,
                ncr.ScrapMovementId,
                ncr.ReturnDocumentId,
                occurredAtUtc));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"quality:{string.Join(':', parts)}";
}
