using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§5 的 IAM 侧种子：登记 58 名在册员工的工人档案。
///
/// 安全边界：这些账号只用于「人员目录/班组/技能矩阵」的展示与引用，不是可登录账号——
/// 口令哈希取一次性随机值（任何人都不知道明文），并强制 <c>PasswordChangeRequired</c>，
/// 也不授予任何角色或成员资格。重复执行幂等：已存在的 userId 只补齐缺失的工人档案字段，
/// 不改口令、不改启用状态。
/// </summary>
public sealed class WorldBibleWorkerSeedService(IServiceProvider serviceProvider, IamPasswordService passwordService)
{
    /// <summary>每写入多少名员工落一次盘。</summary>
    private const int SaveBatchSize = 20;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // 与 IamSeedService 一致：ApplicationDbContext 只在 PostgreSQL profile 下注册，延迟解析。
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = 0;
        foreach (var worker in WorldBibleWorkerSpec.Workers)
        {
            var userId = new UserId(worker.UserId);
            var existing = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
            if (existing is null)
            {
                dbContext.Users.Add(new User(
                    userId,
                    worker.LoginName,
                    worker.Email,
                    passwordService.Hash(Guid.NewGuid().ToString("n")),
                    enabled: true,
                    Guid.NewGuid().ToString("n"),
                    permissionVersion: 1,
                    accountExpiresAtUtc: null,
                    passwordChangedAtUtc: null,
                    passwordExpiresAtUtc: null,
                    passwordChangeRequired: true,
                    displayName: worker.DisplayName,
                    employeeNo: worker.EmployeeNo,
                    departmentName: worker.DepartmentName));
            }
            else if (existing.EmployeeNo is null && existing.DisplayName is null)
            {
                // 账号已存在但没有工人档案：只补齐档案，不动口令/启用状态。
                existing.SetWorkerProfile(worker.DisplayName, worker.EmployeeNo, worker.DepartmentName);
            }

            if (++pending >= SaveBatchSize)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                pending = 0;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
