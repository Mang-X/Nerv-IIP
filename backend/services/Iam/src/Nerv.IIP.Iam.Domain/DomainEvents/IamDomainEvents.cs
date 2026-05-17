using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.DomainEvents;

public record UserLoggedInDomainEvent(string UserId, DateTimeOffset LoggedInAtUtc) : IDomainEvent;
public record UserSessionCreatedDomainEvent(string SessionId, string UserId, DateTimeOffset IssuedAtUtc) : IDomainEvent;
public record UserSessionRevokedDomainEvent(string SessionId, string UserId, DateTimeOffset RevokedAtUtc, string Reason) : IDomainEvent;
