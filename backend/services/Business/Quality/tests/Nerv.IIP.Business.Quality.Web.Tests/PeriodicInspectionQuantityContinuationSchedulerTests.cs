using System.Collections.Concurrent;
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
    public async Task Slow_candidate_does_not_block_an_independent_sibling()
    {
        var start = DateTimeOffset.Parse("2026-08-25T01:00:00Z");
        var clock = new TimerRegistrationObservingTimeProvider(start);
        var sender = new FirstCandidateBlockingSender();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var scheduler = new PeriodicInspectionQuantityContinuationScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PeriodicInspectionQuantityContinuationScheduler>.Instance,
            clock);

        await scheduler.TryContinueAsync(CancellationToken.None);

        Assert.True(sender.FirstCandidateStarted.Task.IsCompletedSuccessfully);
        Assert.True(sender.SecondCandidateCompleted.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Candidate_execution_is_bounded_to_four_independent_scopes()
    {
        var start = DateTimeOffset.Parse("2026-08-25T01:00:00Z");
        var clock = new TimerRegistrationObservingTimeProvider(start);
        var sender = new CapturingBatchSender();
        var executor = new DeterministicCandidateExecutor();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var scheduler = new PeriodicInspectionQuantityContinuationScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PeriodicInspectionQuantityContinuationScheduler>.Instance,
            clock,
            executor);

        await scheduler.TryContinueAsync(CancellationToken.None);

        Assert.Equal(4, executor.PeakConcurrency);
        Assert.Equal(5, sender.GenerationCommands.Count);
    }

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

        await scheduler.TryContinueAsync(CancellationToken.None);

        Assert.Equal(
            ["OP-001", "OP-002"],
            sender.GenerationCommands.Select(command => command.OperationId).Order().ToArray());
        var deferred = Assert.Single(sender.DeferCommands);
        Assert.Equal("OP-001", deferred.OperationId);
        Assert.Equal(start.UtcDateTime, deferred.ObservedNextAttemptAtUtc);
        Assert.Equal(start.AddMinutes(1).UtcDateTime, deferred.NextAttemptAtUtc);
    }

    private sealed class CapturingSender : ISender
    {
        public ConcurrentQueue<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand> GenerationCommands { get; } = [];
        public ConcurrentQueue<DeferPeriodicInspectionQuantityContinuationCommand> DeferCommands { get; } = [];

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
            GenerationCommands.Enqueue(command);
            if (command.OperationId == "OP-001")
            {
                throw new InvalidOperationException("poison candidate");
            }

            return Task.FromResult((TResponse)(object)1);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeferCommands.Enqueue(Assert.IsType<DeferPeriodicInspectionQuantityContinuationCommand>(request));
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

    private sealed class FirstCandidateBlockingSender : ISender
    {
        public TaskCompletionSource FirstCandidateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCandidateCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TResponse> Send<TResponse>(
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
                return (TResponse)(object)candidates;
            }

            var command = Assert.IsType<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand>(request);
            if (command.OperationId == "OP-001")
            {
                FirstCandidateStarted.TrySetResult();
                await SecondCandidateCompleted.Task.WaitAsync(cancellationToken);
            }
            else
            {
                SecondCandidateCompleted.TrySetResult();
            }

            return (TResponse)(object)1;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

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

    private sealed class CapturingBatchSender : ISender
    {
        public ConcurrentQueue<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand> GenerationCommands { get; } = [];

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is ListPendingPeriodicInspectionQuantityContextsQuery query)
            {
                IReadOnlyList<PendingPeriodicInspectionQuantityContext> candidates = Enumerable.Range(1, 5)
                    .Select(index => new PendingPeriodicInspectionQuantityContext(
                        "org-001",
                        "env-dev",
                        $"WO-{index:000}",
                        $"OP-{index:000}",
                        new PeriodicInspectionRuntimeContextId(Guid.CreateVersion7()),
                        query.NowUtc))
                    .ToArray();
                return (TResponse)(object)candidates;
            }

            GenerationCommands.Enqueue(Assert.IsType<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand>(request));
            return (TResponse)(object)1;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DeterministicCandidateExecutor : IPeriodicInspectionCandidateExecutor
    {
        public int PeakConcurrency { get; private set; }

        public async Task ExecuteAsync<TCandidate>(
            IReadOnlyList<TCandidate> candidates,
            int maxConcurrency,
            Func<TCandidate, CancellationToken, Task> executeCandidate,
            CancellationToken cancellationToken)
        {
            foreach (var batch in candidates.Chunk(maxConcurrency))
            {
                PeakConcurrency = Math.Max(PeakConcurrency, batch.Length);
                var executions = batch.Select(candidate => executeCandidate(candidate, cancellationToken)).ToArray();
                await Task.WhenAll(executions);
            }
        }
    }
}
