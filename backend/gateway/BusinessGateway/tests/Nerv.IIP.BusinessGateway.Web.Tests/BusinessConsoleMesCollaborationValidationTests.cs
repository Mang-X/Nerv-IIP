using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleMesCollaborationValidationTests
{
    [Fact]
    public void Every_dispatch_participant_field_rejection_uses_simplified_chinese()
    {
        var validator = new BusinessConsoleMesAssignDispatchTaskRequestValidator();
        var cases = new[]
        {
            (Request(new BusinessConsoleMesDispatchParticipantRequest("", 100m)), "参与者人员 ID 不能为空。"),
            (Request(new BusinessConsoleMesDispatchParticipantRequest(new string('W', 101), 100m)), "参与者人员 ID 长度不能超过 100 个字符。"),
            (Request(new BusinessConsoleMesDispatchParticipantRequest("worker-a", 0m)), "工时占比必须大于 0。"),
            (Request(new BusinessConsoleMesDispatchParticipantRequest("worker-a", 101m)), "工时占比不能超过 100。")
        };

        foreach (var (request, expectedMessage) in cases)
        {
            var result = validator.Validate(request);

            Assert.Contains(expectedMessage, result.Errors.Select(x => x.ErrorMessage));
            Assert.All(result.Errors, error => Assert.Matches("[\u4e00-\u9fff]", error.ErrorMessage));
        }
    }

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
