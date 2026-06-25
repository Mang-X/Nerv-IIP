using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;
using Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class PlanningSuggestionToErpPurchaseRequisitionIntegrationTests
{
    [Fact]
    public async Task Accepted_purchase_suggestion_event_creates_queryable_erp_purchase_requisition_once()
    {
        var suggestion = PlanningSuggestion.Create(
            "org-001",
            "env-dev",
            new(Guid.CreateVersion7()),
            "planned-purchase",
            "SKU-RM-1000",
            "kg",
            "SITE-01",
            19m,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 5, 27),
            "MRP-001");
        suggestion.AddPeggingLink("demand", "DEMAND-001", "SKU-FG-1000", "SKU-RM-1000", 19m, null, null, null);
        suggestion.Accept("BusinessErp", "PurchaseRequisition", null);
        var integrationEvent = new PlanningSuggestionAcceptedIntegrationEventConverter()
            .Convert(new PlanningSuggestionAcceptedDomainEvent(suggestion));

        await using var erpDbContext = CreateErpDbContext();
        var handler = new PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition(
            erpDbContext,
            new CommandSender(erpDbContext),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await erpDbContext.SaveChangesAsync(CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await erpDbContext.SaveChangesAsync(CancellationToken.None);

        var requisition = Assert.Single(await erpDbContext.PurchaseRequisitions
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId)
            .ToArrayAsync(CancellationToken.None));
        Assert.Equal(integrationEvent.Payload.SuggestionId, requisition.SuggestionId);
        Assert.Equal(integrationEvent.Payload.SkuCode, requisition.SkuCode);
        Assert.Equal(integrationEvent.Payload.Quantity, requisition.Quantity);
        Assert.Matches("^PR-[0-9]{8}-[0-9]{6}$", requisition.RequisitionNo);
    }

    private static ApplicationDbContext CreateErpDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"acceptance-dp-erp-pr-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CommandSender(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return request switch
            {
                CreatePurchaseRequisitionFromSuggestionCommand command when typeof(TResponse) == typeof(Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate.PurchaseRequisitionId) =>
                    Cast<TResponse>(new CreatePurchaseRequisitionFromSuggestionCommandHandler(dbContext).Handle(command, cancellationToken)),
                _ => throw new NotSupportedException($"CommandSender cannot handle request type {request.GetType().Name}."),
            };
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("CommandSender only supports request/response commands.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("CommandSender only supports typed request/response commands.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("CommandSender cannot stream.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("CommandSender cannot stream.");
        }

        private static async Task<TResponse> Cast<TResponse>(Task<Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate.PurchaseRequisitionId> task)
        {
            var result = await task;
            return (TResponse)(object)result;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
