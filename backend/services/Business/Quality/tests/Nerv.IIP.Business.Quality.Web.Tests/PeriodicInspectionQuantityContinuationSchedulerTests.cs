using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Scheduling;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class PeriodicInspectionQuantityContinuationSchedulerTests
{
    [Fact]
    public async Task Poison_candidate_is_persistently_deferred_before_the_next_candidate_runs()
    {
        var start = DateTimeOffset.Parse("2026-08-25T01:00:00Z");
        var clock = new TimerRegistrationObservingTimeProvider(start);
        var sender = new CapturingSender();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var scheduler = new PeriodicInspectionQuantityContinuationScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PeriodicInspectionQuantityContinuationScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await Eventually.WaitAsync(
            "quantity continuation scheduler defers the poison candidate and runs the sibling",
            _ => ValueTask.FromResult((Generations: sender.GenerationCommands.Count, Deferrals: sender.DeferCommands.Count)),
            counts => counts.Generations == 2 && counts.Deferrals == 1,
            counts => $"generations={counts.Generations}; deferrals={counts.Deferrals}",
            new EventuallyOptions(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), []));
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(["OP-001", "OP-002"], sender.GenerationCommands.Select(command => command.OperationId).ToArray());
        var deferred = Assert.Single(sender.DeferCommands);
        Assert.Equal("OP-001", deferred.OperationId);
        Assert.Equal(start.UtcDateTime, deferred.ObservedNextAttemptAtUtc);
        Assert.Equal(start.AddMinutes(1).UtcDateTime, deferred.NextAttemptAtUtc);
    }

    private sealed class CapturingSender : ISender
    {
        private int generationCount;

        public List<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand> GenerationCommands { get; } = [];
        public List<DeferPeriodicInspectionQuantityContinuationCommand> DeferCommands { get; } = [];

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is ListPendingPeriodicInspectionQuantityContextsQuery query)
            {
                IReadOnlyList<PendingPeriodicInspectionQuantityContext> candidates =
                [
                    Candidate("WO-001", "OP-001", query.NowUtc),
                    Candidate("WO-002", "OP-002", query.NowUtc),
                ];
                return Task.FromResult((TResponse)(object)candidates);
            }

            var command = Assert.IsType<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand>(request);
            GenerationCommands.Add(command);
            if (Interlocked.Increment(ref generationCount) == 1)
            {
                throw new InvalidOperationException("poison candidate");
            }

            return Task.FromResult((TResponse)(object)1);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeferCommands.Add(Assert.IsType<DeferPeriodicInspectionQuantityContinuationCommand>(request));
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static PendingPeriodicInspectionQuantityContext Candidate(
            string workOrderId,
            string operationId,
            DateTime observedNextAttemptAtUtc) => new(
                "org-001",
                "env-dev",
                workOrderId,
                operationId,
                new PeriodicInspectionRuntimeContextId(Guid.CreateVersion7()),
                observedNextAttemptAtUtc);
    }
}
