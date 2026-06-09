using MediatR;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;

namespace Nerv.IIP.Business.Wms.Web.Tests;

internal sealed class NoopMediator : IMediator
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Test mediator cannot send requests.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        throw new NotSupportedException("Test mediator cannot send requests.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Test mediator cannot send requests.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Test mediator cannot stream requests.");
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Test mediator cannot stream requests.");
    }
}

internal static class DomainWmsFactory
{
    public static InboundOrder InboundOrder()
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate.InboundOrder.Create(
            "org-001",
            "env-dev",
            "IN-001",
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
    }

    public static OutboundOrder OutboundOrder()
    {
        return Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate.OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-001",
            "sales-delivery",
            "SO-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
    }
}
