using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class TelemetryProductionCountTests
{
    [Fact]
    public async Task Record_sample_for_posted_production_count_tag_after_baseline_raises_delta_event()
    {
        await using var dbContext = CreateDbContext(nameof(Record_sample_for_posted_production_count_tag_after_baseline_raises_delta_event));
        dbContext.TelemetryTags.Add(TelemetryTag.Create(
            "org-001",
            "env-dev",
            "DEV-PACK-01",
            "parts_count",
            "production-count-posted",
            "PCS",
            "60s"));
        await dbContext.SaveChangesAsync();

        var handler = new RecordTelemetrySampleCommandHandler(dbContext);
        await handler.Handle(CreateCommand("seq-001", 100m, "2026-07-11T08:00:00Z"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await handler.Handle(CreateCommand("seq-002", 103m, "2026-07-11T08:01:00Z"), CancellationToken.None);

        var secondSummary = dbContext.TelemetrySummaries.Local.Single(x => x.SourceSequence == "seq-002");
        var delta = Assert.IsType<TelemetryProductionCountDeltaDomainEvent>(
            secondSummary.GetDomainEvents().Single(x => x is TelemetryProductionCountDeltaDomainEvent));
        Assert.Equal(3m, delta.DeltaQuantity);
        Assert.Equal("posted", delta.ReportingMode);
        Assert.False(delta.HasActiveAlarm);
    }

    [Fact]
    public async Task Record_delayed_production_count_bucket_after_newer_bucket_does_not_raise_duplicate_delta_event()
    {
        await using var dbContext = CreateDbContext(nameof(Record_delayed_production_count_bucket_after_newer_bucket_does_not_raise_duplicate_delta_event));
        dbContext.TelemetryTags.Add(TelemetryTag.Create(
            "org-001",
            "env-dev",
            "DEV-PACK-01",
            "parts_count",
            "production-count-posted",
            "PCS",
            "60s"));
        await dbContext.SaveChangesAsync();

        var handler = new RecordTelemetrySampleCommandHandler(dbContext);
        await handler.Handle(CreateCommand("seq-001", 100m, "2026-07-11T08:00:00Z"), CancellationToken.None);
        await handler.Handle(CreateCommand("seq-003", 105m, "2026-07-11T08:02:00Z"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await handler.Handle(CreateCommand("seq-002", 103m, "2026-07-11T08:01:00Z"), CancellationToken.None);

        var delayedSummary = dbContext.TelemetrySummaries.Local.Single(x => x.SourceSequence == "seq-002");
        Assert.DoesNotContain(delayedSummary.GetDomainEvents(), x => x is TelemetryProductionCountDeltaDomainEvent);
    }

    private static RecordTelemetrySampleCommand CreateCommand(string sourceSequence, decimal count, string bucketStartUtc)
    {
        var start = DateTimeOffset.Parse(bucketStartUtc);
        return new RecordTelemetrySampleCommand(
        "org-001",
        "env-dev",
        "DEV-PACK-01",
        "parts_count",
        start,
        start.AddMinutes(1),
        1,
        count,
        count,
        count,
        sourceSequence,
        "opcua",
        "opcua-cell-01",
        count,
        count);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
