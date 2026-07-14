namespace Nerv.IIP.Business.MasterData.Domain.DomainEvents;

public record MasterDataAggregateCreatedDomainEvent(
    string AggregateName,
    string OrganizationId,
    string EnvironmentId,
    string Code) : IDomainEvent;

public record MasterDataAggregateUpdatedDomainEvent(
    string AggregateName,
    string OrganizationId,
    string EnvironmentId,
    string Code) : IDomainEvent;

public record MasterDataAggregateDisabledDomainEvent(
    string AggregateName,
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Reason) : IDomainEvent;

public record SkuChangedDomainEvent(string OrganizationId, string EnvironmentId, string Code) : IDomainEvent;

public record SkuDisabledDomainEvent(string OrganizationId, string EnvironmentId, string Code, string Reason, string? OperationId = null) : IDomainEvent;

public record UnitOfMeasureChangedDomainEvent(string OrganizationId, string EnvironmentId, string Code) : IDomainEvent;

public record BusinessPartnerChangedDomainEvent(string OrganizationId, string EnvironmentId, string Code, string Status) : IDomainEvent;

public record ResourceChangedDomainEvent(string ResourceType, string OrganizationId, string EnvironmentId, string Code) : IDomainEvent;

public record WorkCalendarChangedDomainEvent(string OrganizationId, string EnvironmentId, string Code) : IDomainEvent;

public record DeviceAssetChangedDomainEvent(string OrganizationId, string EnvironmentId, string Code, string Status = "active") : IDomainEvent;

public record ReferenceDataCodeChangedDomainEvent(string OrganizationId, string EnvironmentId, string CodeSet, string Code) : IDomainEvent;
