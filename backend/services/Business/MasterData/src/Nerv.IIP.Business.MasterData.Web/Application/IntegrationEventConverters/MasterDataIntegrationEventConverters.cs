using Nerv.IIP.Business.MasterData.Domain.DomainEvents;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

public sealed class SkuChangedIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<SkuChangedDomainEvent, SkuChangedIntegrationEvent>
{
    public SkuChangedIntegrationEvent Convert(SkuChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new SkuChangedIntegrationEvent(
            EventIds.New(),
            MasterDataIntegrationEventTypes.SkuChanged,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("sku-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("sku", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class SkuDisabledIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<SkuDisabledDomainEvent, SkuDisabledIntegrationEvent>
{
    public SkuDisabledIntegrationEvent Convert(SkuDisabledDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new SkuDisabledIntegrationEvent(
            EventIds.New(),
            MasterDataIntegrationEventTypes.SkuDisabled,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("sku-disabled", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code, domainEvent.OperationId ?? context.IdempotencyKey ?? context.CorrelationId),
            new MasterDataDisabledPayload("sku", domainEvent.Code, "disabled", domainEvent.Reason, occurredAtUtc));
    }
}

public sealed class UnitOfMeasureChangedIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<UnitOfMeasureChangedDomainEvent, UnitOfMeasureChangedIntegrationEvent>
{
    public UnitOfMeasureChangedIntegrationEvent Convert(UnitOfMeasureChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new UnitOfMeasureChangedIntegrationEvent(
            EventIds.New(),
            MasterDataIntegrationEventTypes.UnitOfMeasureChanged,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("uom-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("unit-of-measure", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class BusinessPartnerChangedIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<BusinessPartnerChangedDomainEvent, BusinessPartnerChangedIntegrationEvent>
{
    public BusinessPartnerChangedIntegrationEvent Convert(BusinessPartnerChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        var eventId = EventIds.New();
        return new BusinessPartnerChangedIntegrationEvent(
            eventId,
            MasterDataIntegrationEventTypes.BusinessPartnerChanged,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("partner-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code, eventId),
            new MasterDataChangedPayload("business-partner", domainEvent.Code, domainEvent.Status, occurredAtUtc));
    }
}

public sealed class ResourceChangedIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<ResourceChangedDomainEvent, ResourceChangedIntegrationEvent>
{
    public ResourceChangedIntegrationEvent Convert(ResourceChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new ResourceChangedIntegrationEvent(
            EventIds.New(),
            MasterDataIntegrationEventTypes.ResourceChanged,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("resource-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.ResourceType, domainEvent.Code),
            new ResourceChangedPayload(domainEvent.ResourceType, domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class WorkCalendarChangedIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<WorkCalendarChangedDomainEvent, WorkCalendarChangedIntegrationEvent>
{
    public WorkCalendarChangedIntegrationEvent Convert(WorkCalendarChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new WorkCalendarChangedIntegrationEvent(
            EventIds.New(),
            MasterDataIntegrationEventTypes.WorkCalendarChanged,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("work-calendar-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("work-calendar", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class DeviceAssetChangedIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<DeviceAssetChangedDomainEvent, DeviceAssetChangedIntegrationEvent>
{
    public DeviceAssetChangedIntegrationEvent Convert(DeviceAssetChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new DeviceAssetChangedIntegrationEvent(
            EventIds.New(),
            MasterDataIntegrationEventTypes.DeviceAssetChanged,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("device-asset-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.Code),
            new MasterDataChangedPayload("device-asset", domainEvent.Code, "active", occurredAtUtc));
    }
}

public sealed class ReferenceDataCodeChangedIntegrationEventConverter(IMasterDataIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<ReferenceDataCodeChangedDomainEvent, ReferenceDataCodeChangedIntegrationEvent>
{
    public ReferenceDataCodeChangedIntegrationEvent Convert(ReferenceDataCodeChangedDomainEvent domainEvent)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new ReferenceDataCodeChangedIntegrationEvent(
            EventIds.New(),
            MasterDataIntegrationEventTypes.ReferenceDataCodeChanged,
            MasterDataIntegrationEventVersions.V1,
            occurredAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            context.CorrelationId,
            context.CausationId,
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("reference-data-code-changed", domainEvent.OrganizationId, domainEvent.EnvironmentId, domainEvent.CodeSet, domainEvent.Code),
            new ReferenceDataChangedPayload(domainEvent.CodeSet, domainEvent.Code, "active", occurredAtUtc));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"masterdata:{string.Join(':', parts)}";
}
