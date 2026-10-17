namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

/// <summary>Identity from the engineering snapshot captured when an operation task is created.</summary>
public sealed record OperationTaskRoutingIdentity
{
    public OperationTaskRoutingIdentity(string productionVersionId, string routingVersionId, string routingCode, string routingRevision)
    {
        ProductionVersionId = DomainGuard.Required(productionVersionId, nameof(productionVersionId));
        RoutingVersionId = DomainGuard.Required(routingVersionId, nameof(routingVersionId));
        RoutingCode = DomainGuard.Required(routingCode, nameof(routingCode));
        RoutingRevision = DomainGuard.Required(routingRevision, nameof(routingRevision));
    }

    public string ProductionVersionId { get; }
    public string RoutingVersionId { get; }
    public string RoutingCode { get; }
    public string RoutingRevision { get; }
}
