using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class PlanningSuggestionAcceptedConsumerTests
{
    [Fact]
    public async Task PlanningSuggestionAcceptedHandler_CreatesPurchaseRequisitionOnceAfterDuplicateDelivery()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"erp-planning-accepted-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = PlanningSuggestionAcceptedEvent();

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = CreateHandler(dbContext);
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = CreateHandler(dbContext);
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var requisition = Assert.Single(assertionDbContext.PurchaseRequisitions);
        Assert.Equal(integrationEvent.Payload.SuggestionId, requisition.SuggestionId);
        Assert.Matches("^PR-[0-9]{8}-[0-9]{6}$", requisition.RequisitionNo);
        var processed = Assert.Single(assertionDbContext.ProcessedIntegrationEvents);
        Assert.Equal(PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition.ConsumerName, processed.ConsumerName);
        Assert.Equal(integrationEvent.IdempotencyKey, processed.IdempotencyKey);
    }

    [Theory]
    [InlineData("BusinessMes", "WorkOrder")]
    [InlineData("BusinessErp", "PurchaseOrder")]
    public async Task PlanningSuggestionAcceptedHandler_IgnoresUnsupportedDownstreamTargets(string downstreamService, string downstreamDocumentType)
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(PlanningSuggestionAcceptedEvent(downstreamService: downstreamService, downstreamDocumentType: downstreamDocumentType), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(dbContext.PurchaseRequisitions);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [Fact]
    public async Task PlanningSuggestionAcceptedHandler_DeadLettersUnexpectedSourceServiceWithoutCreatingRequisition()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(PlanningSuggestionAcceptedEvent() with { SourceService = "business-mes" }, CancellationToken.None);

        Assert.Empty(dbContext.PurchaseRequisitions);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unexpected-source-service", deadLetter.FailureCode);
        Assert.Equal("business-mes", deadLetter.SourceService);
    }

    [Theory]
    [InlineData(2, "evt-planning-accepted-001", "unsupported-version")]
    [InlineData(1, "", "missing-envelope-field")]
    public async Task PlanningSuggestionAcceptedHandler_DeadLettersInvalidEnvelopeWithoutCreatingRequisition(
        int eventVersion,
        string eventId,
        string expectedFailureCode)
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);

        await handler.HandleAsync(PlanningSuggestionAcceptedEvent(eventVersion: eventVersion) with { EventId = eventId }, CancellationToken.None);

        Assert.Empty(dbContext.PurchaseRequisitions);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal(expectedFailureCode, deadLetter.FailureCode);
    }

    [Fact]
    public async Task PlanningSuggestionAcceptedHandler_DeadLettersUnsupportedSuggestionTypeForErpTarget()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var baseEvent = PlanningSuggestionAcceptedEvent();

        await handler.HandleAsync(
            baseEvent with { Payload = baseEvent.Payload with { SuggestionType = "planned-work-order" } },
            CancellationToken.None);

        Assert.Empty(dbContext.PurchaseRequisitions);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-suggestion-type", deadLetter.FailureCode);
    }

    [Fact]
    public async Task PlanningSuggestionAcceptedHandler_DeadLettersMissingPurchaseFactsForErpTarget()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(dbContext, deadLetters);
        var baseEvent = PlanningSuggestionAcceptedEvent();

        await handler.HandleAsync(
            baseEvent with { Payload = baseEvent.Payload with { SkuCode = string.Empty } },
            CancellationToken.None);

        Assert.Empty(dbContext.PurchaseRequisitions);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("missing-payload-field", deadLetter.FailureCode);
    }

    private static PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition CreateHandler(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore? deadLetterStore = null)
    {
        return new PlanningSuggestionAcceptedIntegrationEventHandlerForCreatePurchaseRequisition(
            dbContext,
            new CreatePurchaseRequisitionFromSuggestionCommandHandler(dbContext),
            deadLetterStore ?? new InMemoryIntegrationEventDeadLetterStore());
    }

    private static PlanningSuggestionAcceptedIntegrationEvent PlanningSuggestionAcceptedEvent(
        string downstreamService = "BusinessErp",
        string downstreamDocumentType = "PurchaseRequisition",
        int eventVersion = DemandPlanningIntegrationEventVersions.V1)
    {
        return new PlanningSuggestionAcceptedIntegrationEvent(
            "evt-planning-accepted-001",
            DemandPlanningIntegrationEventTypes.PlanningSuggestionAccepted,
            eventVersion,
            DateTimeOffset.Parse("2026-06-25T06:00:00Z"),
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:demand-planning",
            "demand-planning:planning-suggestion-accepted:org-001:env-dev:suggestion-001",
            new PlanningSuggestionAcceptedPayload(
                "suggestion-001",
                "mrp-run-001",
                "planned-purchase",
                "SKU-RM-1000",
                "kg",
                "SITE-01",
                19m,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 5, 27),
                "DEMAND-001",
                null,
                downstreamService,
                downstreamDocumentType,
                null));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        return CreateDbContext(CreateDbContextOptions($"erp-planning-accepted-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot()));
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbContextOptions<ApplicationDbContext> CreateDbContextOptions(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
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
