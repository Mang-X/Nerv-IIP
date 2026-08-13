using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// NERV-688 拆解③：MES 的 PostgreSQL profile / CAP 用例统一使用 lane runner 注入的成员数据库
// （NERV_IIP_TEST_POSTGRES），不再自建内层数据库——内层数据库既不能被外层失败诊断读取，也不能
// 被外层 finally 清理证明。每个用例先删除 mes 与 cap 两个 schema 再迁移/初始化 CAP 存储，因此同一
// 成员数据库内的用例之间没有残留。MES 业务表落在 MesFacts.Schema（"mes"）；CAP 出站/入站表由
// DotNetCore.CAP 的 EF 存储在独立的 "cap" schema 自建（不进 EF Core 迁移，只在宿主/IStorageInitializer
// 初始化时按需建表），因此两个 schema 都要声明并清理，不能只删 mes。
//
// 迁移历史表必须落在 mes schema 内才能被这里的 DROP SCHEMA 清理干净：生产路径
// （AddMesPostgreSqlPersistence）已经把 MigrationsHistoryTable 显式配到 MesFacts.Schema；直接
// `new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(...)`（不走该扩展方法）的调用方若不
// 重复这行配置，EF 会把 __EFMigrationsHistory 落在 Npgsql 默认的 public schema——那张表不会被这里的
// DROP SCHEMA mes 删掉，于是下一个用例的 MigrateAsync 会看到"历史记录说全部已应用"而直接跳过迁移，
// 而 mes schema 里其实什么表都没有，报 42P01 relation does not exist。这不是并发 bug（用 depth 探针验证过
// ResetSchemaAsync 从未重入），是纯粹的"迁移历史表跟丢了"。因此本文件里直接建 DbContext 的调用方都必须
// 同样传入 MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema)。
//
// 复用既有的 WebApplicationFactoryCollection（而不是新建一个 collection）：MesCapSubscriptionTests 与
// RushWorkOrderHttpPostgresTests 已经因为 FastEndpoints 8.1.0 进程静态状态的并发构建风险加入该 collection；
// xUnit 的 [Collection] 每个类只能归属一个，若为本次改造另起 collection，两组串行化保护会互不知晓，导致
// 跨 collection 并发命中同一张成员数据库（DROP SCHEMA 与另一个 collection 里仍在跑的迁移/断言竞态）。
// 因此本批全部六个类改用同一个 collection，让"同一成员数据库不并发访问"与"FastEndpoints 静态状态不并发写"
// 共用同一把串行化锁。
internal static class MesPostgresLaneDatabase
{
    internal const string CollectionName = WebApplicationFactoryCollection.Name;

    private const string CapSchema = "cap";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES must be set for MES PostgreSQL lane tests.");

    /// <summary>删除 mes 与 cap 两个 schema（含 mes 侧的 <c>__EFMigrationsHistory</c>），让调用方从干净状态迁移/初始化。</summary>
    internal static async Task ResetSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var schema in new[] { MesFacts.Schema, CapSchema })
        {
            var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 直接构造 <see cref="ApplicationDbContext"/>（不走 <c>AddMesPostgreSqlPersistence</c>）的调用方用这个方法，
    /// 显式把迁移历史表配到 mes schema，与生产路径一致，也与 <see cref="ResetSchemaAsync"/> 的清理范围一致。
    /// </summary>
    internal static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema))
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
