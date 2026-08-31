using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleMesCollaborationValidationTests
{
    [Fact]
    public void Dispatch_participant_collection_rejections_have_exact_simplified_chinese_error_sets()
    {
        var validator = new BusinessConsoleMesAssignDispatchTaskRequestValidator();
        var duplicate = validator.Validate(Request(
            new BusinessConsoleMesDispatchParticipantRequest("worker-a", 50m),
            new BusinessConsoleMesDispatchParticipantRequest("WORKER-A", 50m)));
        var excessivePrecision = validator.Validate(Request(
            new BusinessConsoleMesDispatchParticipantRequest("worker-a", 50.00001m),
            new BusinessConsoleMesDispatchParticipantRequest("worker-b", 49.99999m)));
        var twentyOne = Enumerable.Range(1, 21)
            .Select(index => new BusinessConsoleMesDispatchParticipantRequest(
                $"worker-{index:00}",
                index == 21 ? 10m : 4.5m))
            .ToArray();

        AssertExactMessages(
            duplicate.Errors.Select(x => x.ErrorMessage),
            "工序参与者必须唯一，且工时占比合计必须为 100%。");
        AssertExactMessages(
            excessivePrecision.Errors.Select(x => x.ErrorMessage),
            "工时占比最多保留四位小数。",
            "工时占比最多保留四位小数。");
        AssertExactMessages(
            validator.Validate(Request(twentyOne)).Errors.Select(x => x.ErrorMessage),
            "提供参与者列表时必须包含 1 至 20 人。");
    }

    [Fact]
    public void Every_dispatch_participant_field_rejection_has_the_exact_simplified_chinese_error_set()
    {
        var validator = new BusinessConsoleMesAssignDispatchTaskRequestValidator();
        var cases = new[]
        {
            (Request(new BusinessConsoleMesDispatchParticipantRequest("", 100m)), new[]
            {
                "参与者人员 ID 不能为空。",
                "工序参与者必须唯一，且工时占比合计必须为 100%。"
            }),
            (Request(new BusinessConsoleMesDispatchParticipantRequest(new string('W', 101), 100m)), new[]
            {
                "参与者人员 ID 长度不能超过 100 个字符。"
            }),
            (Request(new BusinessConsoleMesDispatchParticipantRequest("worker-a", 0m)), new[]
            {
                "工时占比必须大于 0。",
                "工序参与者必须唯一，且工时占比合计必须为 100%。"
            }),
            (Request(new BusinessConsoleMesDispatchParticipantRequest("worker-a", 101m)), new[]
            {
                "工时占比不能超过 100。",
                "工序参与者必须唯一，且工时占比合计必须为 100%。"
            })
        };

        foreach (var (request, expectedMessages) in cases)
        {
            var result = validator.Validate(request);

            AssertExactMessages(result.Errors.Select(x => x.ErrorMessage), expectedMessages);
        }
    }

    private static void AssertExactMessages(IEnumerable<string> actualMessages, params string[] expectedMessages) =>
        Assert.Equal(
            expectedMessages.Order(StringComparer.Ordinal),
            actualMessages.Order(StringComparer.Ordinal));

    private static BusinessConsoleMesAssignDispatchTaskRequest Request(
        params BusinessConsoleMesDispatchParticipantRequest[] participants) =>
        new(
            "OP-10",
            "org-001",
            "env-dev",
            "worker-a",
            null,
            "shift-a",
            "idem-001",
            participants);
}
