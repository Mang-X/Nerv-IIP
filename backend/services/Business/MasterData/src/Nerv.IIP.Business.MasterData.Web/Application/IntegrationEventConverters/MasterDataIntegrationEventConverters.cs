using Nerv.IIP.Business.MasterData.Domain.DomainEvents;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEvents;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public sealed class SkuChangedIntegrationEventConverter
    : IIntegrationEventConverter<SkuChangedDomainEvent, SkuChangedIntegrationEvent>
{
    public SkuChangedIntegrationEvent Convert(SkuChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new SkuChangedIntegrationEvent(
            EventIds.New(),
            "masterData.SkuChanged",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            domainEvent.Code,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("sku-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("sku", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class SkuDisabledIntegrationEventConverter
    : IIntegrationEventConverter<SkuDisabledDomainEvent, SkuDisabledIntegrationEvent>
{
    public SkuDisabledIntegrationEvent Convert(SkuDisabledDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new SkuDisabledIntegrationEvent(
            EventIds.New(),
            "masterData.SkuDisabled",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            domainEvent.Code,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("sku-disabled", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataDisabledPayload("sku", domainEvent.Code, "disabled", domainEvent.Reason, occurredAtUtc));
    }
}

public sealed class UnitOfMeasureChangedIntegrationEventConverter
    : IIntegrationEventConverter<UnitOfMeasureChangedDomainEvent, UnitOfMeasureChangedIntegrationEvent>
{
    public UnitOfMeasureChangedIntegrationEvent Convert(UnitOfMeasureChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new UnitOfMeasureChangedIntegrationEvent(
            EventIds.New(),
            "masterData.UnitOfMeasureChanged",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            domainEvent.Code,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("uom-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("unit-of-measure", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class BusinessPartnerChangedIntegrationEventConverter
    : IIntegrationEventConverter<BusinessPartnerChangedDomainEvent, BusinessPartnerChangedIntegrationEvent>
{
    public BusinessPartnerChangedIntegrationEvent Convert(BusinessPartnerChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new BusinessPartnerChangedIntegrationEvent(
            EventIds.New(),
            "masterData.BusinessPartnerChanged",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            domainEvent.Code,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("partner-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("business-partner", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class ResourceChangedIntegrationEventConverter
    : IIntegrationEventConverter<ResourceChangedDomainEvent, ResourceChangedIntegrationEvent>
{
    public ResourceChangedIntegrationEvent Convert(ResourceChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new ResourceChangedIntegrationEvent(
            EventIds.New(),
            "masterData.ResourceChanged",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            domainEvent.Code,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("resource-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.ResourceType, domainEvent.Code),
            new ResourceChangedPayload(domainEvent.ResourceType, domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class WorkCalendarChangedIntegrationEventConverter
    : IIntegrationEventConverter<WorkCalendarChangedDomainEvent, WorkCalendarChangedIntegrationEvent>
{
    public WorkCalendarChangedIntegrationEvent Convert(WorkCalendarChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new WorkCalendarChangedIntegrationEvent(
            EventIds.New(),
            "masterData.WorkCalendarChanged",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            domainEvent.Code,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("work-calendar-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("work-calendar", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class DeviceAssetChangedIntegrationEventConverter
    : IIntegrationEventConverter<DeviceAssetChangedDomainEvent, DeviceAssetChangedIntegrationEvent>
{
    public DeviceAssetChangedIntegrationEvent Convert(DeviceAssetChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new DeviceAssetChangedIntegrationEvent(
            EventIds.New(),
            "masterData.DeviceAssetChanged",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            domainEvent.Code,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("device-asset-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("device-asset", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class ReferenceDataCodeChangedIntegrationEventConverter
    : IIntegrationEventConverter<ReferenceDataCodeChangedDomainEvent, ReferenceDataCodeChangedIntegrationEvent>
{
    public ReferenceDataCodeChangedIntegrationEvent Convert(ReferenceDataCodeChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new ReferenceDataCodeChangedIntegrationEvent(
            EventIds.New(),
            "masterData.ReferenceDataCodeChanged",
            1,
            occurredAtUtc,
            "business-masterdata",
            string.Empty,
            $"{domainEvent.CodeSet}:{domainEvent.Code}",
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "business-masterdata",
            EventIds.Idempotency("reference-data-code-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.CodeSet, domainEvent.Code),
            new ReferenceDataChangedPayload(domainEvent.CodeSet, domainEvent.Code, "active", occurredAtUtc));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"masterdata:{string.Join(':', parts)}";
}
