using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesCollaborationValidationTests
{
    [Fact]
    public void Dispatch_participants_require_unique_workers_and_exactly_one_hundred_percent()
    {
        var validator = new AssignDispatchTaskCommandValidator();
        var duplicate = CreateCommand(
            new DispatchParticipantInput("worker-a", "Alice", 50m),
            new DispatchParticipantInput("WORKER-A", "Alice", 50m));
        var underAllocated = CreateCommand(
            new DispatchParticipantInput("worker-a", "Alice", 60m),
            new DispatchParticipantInput("worker-b", "Bob", 30m));
        var valid = CreateCommand(
            new DispatchParticipantInput("worker-a", "Alice", 60m),
            new DispatchParticipantInput("worker-b", "Bob", 40m));
        var excessivePrecision = CreateCommand(
            new DispatchParticipantInput("worker-a", "Alice", 50.00001m),
            new DispatchParticipantInput("worker-b", "Bob", 49.99999m));
        var explicitlyEmpty = CreateCommand();

        Assert.False(validator.Validate(duplicate).IsValid);
        Assert.False(validator.Validate(underAllocated).IsValid);
        Assert.False(validator.Validate(excessivePrecision).IsValid);
        Assert.False(validator.Validate(explicitlyEmpty).IsValid);
        Assert.True(validator.Validate(valid).IsValid);
    }

    [Fact]
    public void Dispatch_participants_accept_twenty_workers_but_reject_twenty_one()
    {
        var validator = new AssignDispatchTaskCommandValidator();
        var twenty = Enumerable.Range(1, 20)
            .Select(index => new DispatchParticipantInput($"worker-{index:00}", $"Worker {index:00}", 5m))
            .ToArray();
        var twentyOne = Enumerable.Range(1, 21)
            .Select(index => new DispatchParticipantInput(
                $"worker-{index:00}",
                $"Worker {index:00}",
                index == 21 ? 10m : 4.5m))
            .ToArray();

        Assert.True(validator.Validate(CreateCommand(twenty)).IsValid);
        Assert.False(validator.Validate(CreateCommand(twentyOne)).IsValid);
    }

    private static AssignDispatchTaskCommand CreateCommand(params DispatchParticipantInput[] participants) =>
        new(
            "org-001",
            "env-dev",
            "OP-10",
            "worker-a",
            null,
            "shift-a",
            DateTimeOffset.Parse("2026-08-25T08:00:00Z"),
            Participants: participants);
}
