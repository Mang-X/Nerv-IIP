using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Web.Application.Seed;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》§5 的 IAM 侧黄金向量：58 名员工、工号 EMP-001..EMP-058、
/// 部门人数分布，以及与 MasterData 侧一致的 <c>user-emp-0xx</c> 标识。
/// </summary>
public sealed class WorldBibleWorkerSpecTests
{
    [Fact]
    public void Roster_matches_the_world_bible_headcount()
    {
        var workers = WorldBibleWorkerSpec.Workers;

        Assert.Equal(58, workers.Count);
        Assert.Equal(28, workers.Count(x => x.DepartmentCode == "DEPT-PROD"));
        Assert.Equal(4, workers.Count(x => x.DepartmentCode == "DEPT-PLAN"));
        Assert.Equal(9, workers.Count(x => x.DepartmentCode == "DEPT-QA"));
        Assert.Equal(6, workers.Count(x => x.DepartmentCode == "DEPT-EQ"));
        Assert.Equal(7, workers.Count(x => x.DepartmentCode == "DEPT-WH"));
        Assert.Equal(4, workers.Count(x => x.DepartmentCode == "DEPT-BIZ"));
    }

    [Fact]
    public void Identities_are_unique_deterministic_and_chinese()
    {
        var workers = WorldBibleWorkerSpec.Workers;

        Assert.Equal(58, workers.Select(x => x.UserId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, workers.Select(x => x.EmployeeNo).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, workers.Select(x => x.LoginName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, workers.Select(x => x.Email).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(58, workers.Select(x => x.DisplayName).Distinct(StringComparer.Ordinal).Count());

        // 与 MasterData 侧 WorldBibleSpec.Employees 共用的字面量契约。
        Assert.Equal("user-emp-001", workers[0].UserId);
        Assert.Equal("EMP-001", workers[0].EmployeeNo);
        Assert.Equal("user-emp-058", workers[^1].UserId);
        Assert.Equal("EMP-058", workers[^1].EmployeeNo);

        Assert.All(workers, worker =>
        {
            Assert.Matches("^EMP-0[0-5][0-9]$", worker.EmployeeNo);
            Assert.All(worker.DisplayName, character => Assert.InRange(character, '一', '鿿'));
            Assert.EndsWith($"@{WorldBibleWorkerSpec.EmailDomain}", worker.Email, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Worker_profile_is_normalized_and_optional()
    {
        var user = new User(
            new UserId("user-emp-001"),
            "emp001",
            "emp001@ninghu-damper.local",
            "hash",
            enabled: true,
            "stamp",
            permissionVersion: 1,
            displayName: "  张伟  ",
            employeeNo: "EMP-001",
            departmentName: "   ");

        Assert.Equal("张伟", user.DisplayName);
        Assert.Equal("EMP-001", user.EmployeeNo);
        Assert.Null(user.DepartmentName);

        user.SetWorkerProfile(null, null, "生产部");
        Assert.Null(user.DisplayName);
        Assert.Null(user.EmployeeNo);
        Assert.Equal("生产部", user.DepartmentName);
    }
}
