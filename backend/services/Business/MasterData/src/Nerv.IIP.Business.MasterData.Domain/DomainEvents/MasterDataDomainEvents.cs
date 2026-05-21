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
