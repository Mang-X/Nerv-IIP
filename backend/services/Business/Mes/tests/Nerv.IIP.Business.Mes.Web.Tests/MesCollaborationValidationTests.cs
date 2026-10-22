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

    [Fact]
    public void Dispatch_participant_collection_rejections_have_exact_simplified_chinese_error_sets()
    {
        var validator = new AssignDispatchTaskCommandValidator();
        var duplicate = validator.Validate(CreateCommand(
            new DispatchParticipantInput("worker-a", "Alice", 50m),
            new DispatchParticipantInput("WORKER-A", "Alice", 50m)));
        var excessivePrecision = validator.Validate(CreateCommand(
            new DispatchParticipantInput("worker-a", "Alice", 50.00001m),
            new DispatchParticipantInput("worker-b", "Bob", 49.99999m)));
        var twentyOne = Enumerable.Range(1, 21)
            .Select(index => new DispatchParticipantInput(
                $"worker-{index:00}",
                $"Worker {index:00}",
                index == 21 ? 10m : 4.5m))
            .ToArray();

        AssertExactMessages(
            duplicate.Errors.Select(x => x.ErrorMessage),
            "工序任务参与者的人员 ID 必须唯一，且占比合计必须为 100%。");
        AssertExactMessages(
            excessivePrecision.Errors.Select(x => x.ErrorMessage),
            "参与者工时占比最多保留四位小数。",
            "参与者工时占比最多保留四位小数。");
        AssertExactMessages(
            validator.Validate(CreateCommand(twentyOne)).Errors.Select(x => x.ErrorMessage),
            "提供参与者时，工序任务必须登记 1 至 20 名参与者。");
    }

    [Fact]
    public void Every_dispatch_scope_rejection_has_the_exact_simplified_chinese_message()
    {
        var validator = new AssignDispatchTaskCommandValidator();
        var cases = new[]
        {
            (CreateCommand("", "env-dev", "OP-10"), "组织 ID 不能为空。"),
            (CreateCommand(new string('O', 101), "env-dev", "OP-10"), "组织 ID 长度不能超过 100 个字符。"),
            (CreateCommand("org-001", "", "OP-10"), "环境 ID 不能为空。"),
            (CreateCommand("org-001", new string('E', 101), "OP-10"), "环境 ID 长度不能超过 100 个字符。"),
            (CreateCommand("org-001", "env-dev", ""), "工序任务 ID 不能为空。"),
            (CreateCommand("org-001", "env-dev", new string('T', 101)), "工序任务 ID 长度不能超过 100 个字符。")
        };

        foreach (var (command, expectedMessage) in cases)
        {
            var result = validator.Validate(command);

            AssertExactMessages(result.Errors.Select(x => x.ErrorMessage), expectedMessage);
        }
    }

    [Fact]
    public void Every_dispatch_participant_field_rejection_has_the_exact_simplified_chinese_error_set()
    {
        var validator = new AssignDispatchTaskCommandValidator();
        var cases = new[]
        {
            (CreateCommand(new DispatchParticipantInput("", "Alice", 100m)), new[]
            {
                "参与者人员 ID 不能为空。",
                "工序任务参与者的人员 ID 必须唯一，且占比合计必须为 100%。"
            }),
            (CreateCommand(new DispatchParticipantInput(new string('W', 101), "Alice", 100m)), new[]
            {
                "参与者人员 ID 长度不能超过 100 个字符。"
            }),
            (CreateCommand(new DispatchParticipantInput("worker-a", new string('名', 201), 100m)), new[]
            {
                "参与者姓名长度不能超过 200 个字符。"
            }),
            (CreateCommand(new DispatchParticipantInput("worker-a", "Alice", 0m)), new[]
            {
                "参与者工时占比必须大于 0。",
                "工序任务参与者的人员 ID 必须唯一，且占比合计必须为 100%。"
            }),
            (CreateCommand(new DispatchParticipantInput("worker-a", "Alice", 101m)), new[]
            {
                "参与者工时占比不能超过 100。",
                "工序任务参与者的人员 ID 必须唯一，且占比合计必须为 100%。"
            })
        };

        foreach (var (command, expectedMessages) in cases)
        {
            var result = validator.Validate(command);

            AssertExactMessages(result.Errors.Select(x => x.ErrorMessage), expectedMessages);
        }
    }

    private static void AssertExactMessages(IEnumerable<string> actualMessages, params string[] expectedMessages) =>
        Assert.Equal(
            expectedMessages.Order(StringComparer.Ordinal),
            actualMessages.Order(StringComparer.Ordinal));

    private static AssignDispatchTaskCommand CreateCommand(
        string organizationId,
        string environmentId,
        string operationTaskId) =>
        new(
            organizationId,
            environmentId,
            operationTaskId,
            "worker-a",
            null,
            "shift-a",
            DateTimeOffset.Parse("2026-08-25T08:00:00Z"),
            Participants: null);

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
