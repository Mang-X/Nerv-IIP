using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using Nerv.IIP.Contracts.ProductEngineering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.IntegrationEventConverters;

public sealed class EngineeringBomReleasedIntegrationEventConverter(IProductEngineeringIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<EngineeringBomReleasedDomainEvent, BomReleasedIntegrationEvent>
{
    public BomReleasedIntegrationEvent Convert(EngineeringBomReleasedDomainEvent domainEvent)
    {
        var bom = domainEvent.Bom;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new BomReleasedIntegrationEvent(
            EventIds.New(),
            ProductEngineeringIntegrationEventTypes.BomReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            occurredAtUtc,
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            context.CorrelationId,
            context.CausationId,
            bom.OrganizationId,
            bom.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("engineering-bom-released", bom.OrganizationId, bom.EnvironmentId, bom.BomCode, bom.Revision),
            new BomReleasedPayload(
                EventIds.AggregateId(bom.Id?.Id, bom.BomCode, bom.Revision),
                "engineering",
                bom.ParentItemCode,
                bom.Lines.Select(x => new BomReleasedLine(x.ChildItemCode, x.Quantity, x.UnitOfMeasureCode)).ToArray(),
                bom.EffectiveDate ?? DateOnly.FromDateTime(occurredAtUtc.UtcDateTime)));
    }
}

public sealed class ManufacturingBomReleasedIntegrationEventConverter(IProductEngineeringIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<ManufacturingBomReleasedDomainEvent, BomReleasedIntegrationEvent>
{
    public BomReleasedIntegrationEvent Convert(ManufacturingBomReleasedDomainEvent domainEvent)
    {
        var bom = domainEvent.Bom;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new BomReleasedIntegrationEvent(
            EventIds.New(),
            ProductEngineeringIntegrationEventTypes.BomReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            occurredAtUtc,
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            context.CorrelationId,
            context.CausationId,
            bom.OrganizationId,
            bom.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("manufacturing-bom-released", bom.OrganizationId, bom.EnvironmentId, bom.BomCode, bom.Revision),
            new BomReleasedPayload(
                EventIds.AggregateId(bom.Id?.Id, bom.BomCode, bom.Revision),
                "manufacturing",
                bom.SkuCode,
                bom.MaterialLines.Select(x => new BomReleasedLine(x.SkuCode, x.Quantity, x.UnitOfMeasureCode)).ToArray(),
                bom.EffectiveDate ?? DateOnly.FromDateTime(occurredAtUtc.UtcDateTime)));
    }
}

public sealed class RoutingReleasedIntegrationEventConverter(IProductEngineeringIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<RoutingReleasedDomainEvent, RoutingReleasedIntegrationEvent>
{
    public RoutingReleasedIntegrationEvent Convert(RoutingReleasedDomainEvent domainEvent)
    {
        var routing = domainEvent.Routing;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new RoutingReleasedIntegrationEvent(
            EventIds.New(),
            ProductEngineeringIntegrationEventTypes.RoutingReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            occurredAtUtc,
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            context.CorrelationId,
            context.CausationId,
            routing.OrganizationId,
            routing.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("routing-released", routing.OrganizationId, routing.EnvironmentId, routing.RoutingCode, routing.Revision),
            new RoutingReleasedPayload(
                EventIds.AggregateId(routing.Id?.Id, routing.RoutingCode, routing.Revision),
                routing.SkuCode,
                routing.Operations.Select(x => new RoutingReleasedOperation(x.Sequence, x.WorkCenterCode, x.OperationCode, x.OperationName, x.StandardMinutes)).ToArray(),
                routing.EffectiveDate ?? DateOnly.FromDateTime(occurredAtUtc.UtcDateTime)));
    }
}

public sealed class ProductionVersionCreatedIntegrationEventConverter(IProductEngineeringIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<ProductionVersionCreatedDomainEvent, ProductionVersionCreatedIntegrationEvent>
{
    public ProductionVersionCreatedIntegrationEvent Convert(ProductionVersionCreatedDomainEvent domainEvent)
    {
        var version = domainEvent.ProductionVersion;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new ProductionVersionCreatedIntegrationEvent(
            EventIds.New(),
            ProductEngineeringIntegrationEventTypes.ProductionVersionCreated,
            ProductEngineeringIntegrationEventVersions.V1,
            occurredAtUtc,
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            context.CorrelationId,
            context.CausationId,
            version.OrganizationId,
            version.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("production-version-created", version.OrganizationId, version.EnvironmentId, version.SkuCode, version.MbomVersionId, version.RoutingVersionId),
            new ProductionVersionCreatedPayload(
                EventIds.AggregateId(version.Id?.Id, version.SkuCode, version.MbomVersionId, version.RoutingVersionId),
                version.SkuCode,
                version.MbomVersionId,
                version.RoutingVersionId,
                version.ValidFrom,
                version.ValidTo));
    }
}

public sealed class EngineeringChangeReleasedIntegrationEventConverter(IProductEngineeringIntegrationEventContextAccessor contextAccessor)
    : IIntegrationEventConverter<EngineeringChangeReleasedDomainEvent, EngineeringChangeReleasedIntegrationEvent>
{
    public EngineeringChangeReleasedIntegrationEvent Convert(EngineeringChangeReleasedDomainEvent domainEvent)
    {
        var change = domainEvent.Change;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var context = contextAccessor.GetContext();
        return new EngineeringChangeReleasedIntegrationEvent(
            EventIds.New(),
            ProductEngineeringIntegrationEventTypes.EngineeringChangeReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            occurredAtUtc,
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            context.CorrelationId,
            context.CausationId,
            change.OrganizationId,
            change.EnvironmentId,
            context.Actor,
            EventIds.Idempotency("engineering-change-released", change.OrganizationId, change.EnvironmentId, change.ChangeNumber),
            new EngineeringChangeReleasedPayload(
                EventIds.AggregateId(change.Id?.Id, change.ChangeNumber),
                change.ChangeNumber,
                change.AffectedVersions.Select(x => x.VersionId).ToArray(),
                change.EffectiveDate ?? DateOnly.FromDateTime(occurredAtUtc.UtcDateTime)));
    }
}

internal static class EventIds
{
    public static string New() => $"evt-{Guid.CreateVersion7():N}";

    public static string Idempotency(params string[] parts) => $"product-engineering:{string.Join(':', parts)}";

    public static string AggregateId(Guid? id, params string[] businessParts)
    {
        // Strongly typed IDs are generated by EF on save, so release events raised before persistence use a stable business-key fallback.
        return id is null ? string.Join(':', businessParts) : id.Value.ToString("D");
    }
}
