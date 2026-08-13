using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Erp.Web.Tests;

// NERV-688 拆解③：ERP 的 PostgreSQL acceptance 用例统一使用 lane runner 注入的成员数据库
// （NERV_IIP_TEST_POSTGRES），不再各自 CREATE DATABASE 自建内层数据库——内层数据库既不能被外层
// 失败诊断读取，也不能被外层 finally 清理证明。每个用例先删除 erp schema 再迁移，因此同一成员数据库内
// 的用例之间没有残留。ERP 的持久化扩展（AddErpPostgreSqlPersistence）不装配 CAP，业务表与迁移历史表
// 全部落在 ErpFacts.Schema（"erp"），不像 MES/DemandPlanning 那样存在独立的 cap schema，因此只需声明
// 并清理这一个 schema。
//
// 迁移历史表必须落在 erp schema 内才能被这里的 DROP SCHEMA 清理干净：生产路径
// （AddErpPostgreSqlPersistence）已经把 MigrationsHistoryTable 显式配到 ErpFacts.Schema；直接
// `new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(...)`（不走该扩展方法）的调用方若不
// 重复这行配置，EF 会把 __EFMigrationsHistory 落在 Npgsql 默认的 public schema——那张表不会被这里的
// DROP SCHEMA erp 删掉，于是下一个用例的 MigrateAsync 会看到"历史记录说全部已应用"而直接跳过迁移，
// 而 erp schema 里其实什么表都没有，报 42P01 relation does not exist。因此本文件里直接建 DbContext 的
// 调用方都必须同样传入 MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema)。
internal static class ErpPostgresLaneDatabase
{
    internal const string CollectionName = "ERP PostgreSQL acceptance";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES must be set for ERP PostgreSQL lane tests.");

    /// <summary>删除 erp schema（含其中的 <c>__EFMigrationsHistory</c>），让调用方从干净状态迁移。</summary>
    internal static async Task ResetSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 直接构造 <see cref="ApplicationDbContext"/>（不走 <c>AddErpPostgreSqlPersistence</c>）的调用方用这个方法，
    /// 显式把迁移历史表配到 erp schema，与生产路径一致，也与 <see cref="ResetSchemaAsync"/> 的清理范围一致。
    /// </summary>
    internal static DbContextOptions<ApplicationDbContext> CreateOptions(string? connectionString = null) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString ?? ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;

    /// <summary>证明用例跑在 lane 治理的成员数据库上，而不是某个自建的内层数据库。</summary>
    internal static void AssertUsesGovernedDatabase(DbContext dbContext)
    {
        var governed = new NpgsqlConnectionStringBuilder(ConnectionString);
        var observed = new NpgsqlConnectionStringBuilder(dbContext.Database.GetDbConnection().ConnectionString);
        // 只比库名不足以证明"跑在受治理的成员库上"：同名库可能在另一台主机或另一个端口。
        Assert.Equal(
            (governed.Host, governed.Port, governed.Database),
            (observed.Host, observed.Port, observed.Database));
    }
}
