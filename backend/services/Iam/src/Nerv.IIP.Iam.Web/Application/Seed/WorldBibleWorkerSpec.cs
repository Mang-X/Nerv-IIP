namespace Nerv.IIP.Iam.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§5 的 IAM 侧固定形状：58 名在册员工（工号 <c>EMP-001..EMP-058</c>、
/// 中文姓名池确定性生成、部门按设定集人数分布）。
///
/// <para>
/// <b>权威源（2026-07-26 修订）</b>：员工档案（工号 / 姓名 / 部门 / 岗位 / 在岗状态）的业务权威是
/// MasterData 的 <c>Worker</c> 聚合，不是 IAM——人员档案属于业务主数据，IAM 只负责登录身份，
/// 且车间存在「有工号无账号」的工人。本文件早期注释曾声明「IAM 是全平台唯一的人权威来源」，该声明已撤销。
/// </para>
/// <para>
/// 这里的 58 条 IAM 用户仍然保留：它们提供可登录账号，<c>users</c> 表上的
/// <c>DisplayName</c> / <c>EmployeeNo</c> / <c>DepartmentName</c> 三列降级为**账号展示冗余**，
/// 只用于 IAM 自身的用户列表，不作为业务读面的来源。业务侧一律读 MasterData 员工目录。
/// </para>
/// <para>
/// 两侧按同一份 <c>user-emp-0xx</c> 字面量与同一生成算法重复声明，各自有黄金向量测试防止漂移。
/// </para>
/// </summary>
public static class WorldBibleWorkerSpec
{
    public const string EmailDomain = "ninghu-damper.local";

    private static readonly string[] Surnames =
    [
        "王", "李", "张", "刘", "陈", "杨", "赵", "黄", "周", "吴",
        "徐", "孙", "胡", "朱", "高", "林", "何", "郭", "马", "罗",
    ];

    private static readonly string[] GivenNames =
    [
        "建国", "秀英", "志强", "桂芳", "海涛", "丽娟", "文斌", "春梅", "国庆", "晓东",
        "淑芬", "永强", "秀兰", "俊杰", "玉兰", "小磊", "凤霞", "明辉", "雅琴", "浩然",
        "美玲", "立新", "婷婷", "德华", "红梅", "天宇", "金花", "伟东", "雪梅", "宏伟",
    ];

    /// <summary>设定集 §5 的部门 → 岗位 → 人数分布（合计 58）。</summary>
    private static readonly (string DepartmentCode, string DepartmentName, string RoleName, int Count)[] HeadcountPlan =
    [
        ("DEPT-PROD", "生产部", "车间主任", 3),
        ("DEPT-PROD", "生产部", "班组长", 6),
        ("DEPT-PROD", "生产部", "操作工", 19),
        ("DEPT-PLAN", "计划部", "计划主管", 1),
        ("DEPT-PLAN", "计划部", "计划员", 3),
        ("DEPT-QA", "质量部", "质量主管", 1),
        ("DEPT-QA", "质量部", "检验员", 6),
        ("DEPT-QA", "质量部", "质量工程师", 2),
        ("DEPT-EQ", "设备部", "设备主管", 1),
        ("DEPT-EQ", "设备部", "维修技师", 4),
        ("DEPT-EQ", "设备部", "点检员", 1),
        ("DEPT-WH", "仓储部", "仓储主管", 1),
        ("DEPT-WH", "仓储部", "库管", 4),
        ("DEPT-WH", "仓储部", "配送叉车工", 2),
        ("DEPT-BIZ", "经营部", "销售", 2),
        ("DEPT-BIZ", "经营部", "采购", 2),
    ];

    public static readonly IReadOnlyList<WorldBibleWorker> Workers = BuildWorkers();

    private static IReadOnlyList<WorldBibleWorker> BuildWorkers()
    {
        var workers = new List<WorldBibleWorker>(58);
        var ordinal = 0;
        foreach (var (departmentCode, departmentName, roleName, count) in HeadcountPlan)
        {
            for (var index = 0; index < count; index++)
            {
                workers.Add(new WorldBibleWorker(
                    UserId: $"user-emp-{ordinal + 1:D3}",
                    EmployeeNo: $"EMP-{ordinal + 1:D3}",
                    DisplayName: $"{Surnames[ordinal % Surnames.Length]}{GivenNames[(ordinal * 7) % GivenNames.Length]}",
                    LoginName: $"emp{ordinal + 1:D3}",
                    Email: $"emp{ordinal + 1:D3}@{EmailDomain}",
                    DepartmentCode: departmentCode,
                    DepartmentName: departmentName,
                    RoleName: roleName));
                ordinal++;
            }
        }

        return workers;
    }
}

public sealed record WorldBibleWorker(
    string UserId,
    string EmployeeNo,
    string DisplayName,
    string LoginName,
    string Email,
    string DepartmentCode,
    string DepartmentName,
    string RoleName);
