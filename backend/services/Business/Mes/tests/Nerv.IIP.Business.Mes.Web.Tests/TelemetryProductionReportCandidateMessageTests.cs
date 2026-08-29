using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class TelemetryProductionReportCandidateMessageTests
{
    [Fact]
    public async Task Candidate_public_reads_and_actions_localize_cross_scope_not_found()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-candidate-message-{Guid.CreateVersion7():N}")
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var candidate = TelemetryProductionReportCandidate.CreateDraft(
            "org-002",
            "env-dev",
            "source-message-001",
            "DEV-01",
            "count",
            2m,
            DateTimeOffset.Parse("2026-07-12T01:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T01:01:00Z"),
            "WC-01",
            "WO-01",
            "OP-10");
        db.TelemetryProductionReportCandidates.Add(candidate);
        await db.SaveChangesAsync();

        var queryException = await Assert.ThrowsAsync<KnownException>(() =>
            new GetTelemetryProductionReportCandidateQueryHandler(db).Handle(
                new("org-001", "env-dev", candidate.Id),
                CancellationToken.None));
        var promoteException = await Assert.ThrowsAsync<KnownException>(() =>
            new PromoteTelemetryProductionReportCandidateCommandHandler(db, new ThrowingSender()).Handle(
                new("org-001", "env-dev", candidate.Id, "WO-01", "OP-10", "operator:message", DateTimeOffset.UtcNow),
                CancellationToken.None));
        var dismissException = await Assert.ThrowsAsync<KnownException>(() =>
            new DismissTelemetryProductionReportCandidateCommandHandler(db).Handle(
                new("org-001", "env-dev", candidate.Id, "not found", "operator:message", DateTimeOffset.UtcNow),
                CancellationToken.None));

        Assert.Equal("未找到遥测报工候选。", queryException.Message);
        Assert.Equal("未找到遥测报工候选。", promoteException.Message);
        Assert.Equal("未找到遥测报工候选。", dismissException.Message);
    }

    // 验收 1（#1948）：遥测计数没有提交人，确认晋升的操作人就是这条报工的责任人。
    [Fact]
    public async Task Promoting_a_telemetry_candidate_reports_the_confirming_operator()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-candidate-operator-{Guid.CreateVersion7():N}")
            .Options;
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var candidate = TelemetryProductionReportCandidate.CreateDraft(
            "org-001",
            "env-dev",
            "source-operator-001",
            "DEV-01",
            "count",
            2m,
            DateTimeOffset.Parse("2026-07-12T01:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T01:01:00Z"),
            "WC-01",
            "WO-01",
            "OP-10");
        db.TelemetryProductionReportCandidates.Add(candidate);
        await db.SaveChangesAsync();

        var sender = new RecordingSender();
        await new PromoteTelemetryProductionReportCandidateCommandHandler(db, sender).Handle(
            new("org-001", "env-dev", candidate.Id, "WO-01", "OP-10", "user:supervisor", DateTimeOffset.Parse("2026-07-12T02:00:00Z")),
            CancellationToken.None);

        Assert.Equal("user:supervisor", sender.Command?.ReportedBy);
    }

    private sealed class RecordingSender : ISender
    {
        public RecordProductionReportCommand? Command { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Command = Assert.IsType<RecordProductionReportCommand>(request);
            return Task.FromResult((TResponse)(object)new ProductionReportCommandResult(new ProductionReportId(Guid.CreateVersion7()), "PRPT-TELEMETRY-001"));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException("Confirmed replay must not invoke RecordProductionReportCommand.");
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
