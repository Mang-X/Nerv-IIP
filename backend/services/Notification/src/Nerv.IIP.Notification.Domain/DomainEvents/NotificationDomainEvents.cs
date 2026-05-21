using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Notification.Domain.DomainEvents;

public sealed record NotificationIntentSubmittedDomainEvent(NotificationIntent Intent) : IDomainEvent;
public sealed record NotificationMessageReadDomainEvent(NotificationIntent Intent, NotificationMessage Message) : IDomainEvent;
