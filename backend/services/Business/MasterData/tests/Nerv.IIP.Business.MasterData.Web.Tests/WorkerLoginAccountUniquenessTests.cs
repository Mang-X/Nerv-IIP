using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// 员工 ↔ 登录账号一对一（#3924）：命令预检之外，并发提交撞上
/// <c>(organization_id, environment_id, user_id)</c> 唯一索引时必须转成中文业务错误，不能以 500 漏出。
/// 这里绕开命令预检直接落两条同账号员工，模拟「两个请求都通过了预检」的并发窗口。
/// </summary>
public sealed class WorkerLoginAccountUniquenessTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
    private const string ConflictMessage = "所选登录账号已关联其他员工，一个账号只能关联一名员工。";

    [Fact]
    public async Task Sqlite_second_worker_with_same_login_account_is_rejected_as_known_exception()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        using (var setupScope = provider.CreateScope())
        {
            var setup = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setup.Database.EnsureCreatedAsync();
            setup.Workers.Add(NewWorker("EMP-A", "user-linked"));
            await setup.SaveChangesAsync();
        }

        using var loserScope = provider.CreateScope();
        var loser = loserScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        loser.Workers.Add(NewWorker("EMP-B", "user-linked"));

        var error = await Assert.ThrowsAsync<KnownException>(() => loser.SaveChangesAsync());
        Assert.Equal(ConflictMessage, error.Message);

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(["EMP-A"], await observer.Workers.AsNoTracking().Select(x => x.Code).ToListAsync());
    }

    [Fact]
    public async Task Sqlite_other_unique_violations_are_not_reported_as_login_account_conflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        using (var setupScope = provider.CreateScope())
        {
            var setup = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setup.Database.EnsureCreatedAsync();
            setup.Workers.Add(NewWorker("EMP-A", "user-a"));
            await setup.SaveChangesAsync();
        }

        using var loserScope = provider.CreateScope();
        var loser = loserScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        loser.Workers.Add(NewWorker("EMP-A", "user-b"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => loser.SaveChangesAsync());
        Assert.DoesNotContain(ConflictMessage, error.Message, StringComparison.Ordinal);
    }

    private static Worker NewWorker(string code, string userId) =>
        Worker.Create(OrganizationId, EnvironmentId, code, $"员工{code}", userId, null, null, Worker.StatusActive, null);
}
