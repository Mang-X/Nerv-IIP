using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Nerv.IIP.Business.Acceptance.Tests;

// NERV-688 拆解③：跨业务 Acceptance 项目里的 PostgreSQL 用例统一使用 lane runner 注入的成员数据库
// （NERV_IIP_TEST_POSTGRES），不再各自 CREATE DATABASE 自建内层数据库——内层数据库既不能被外层失败诊断
// 读取，也不能被外层 finally 清理证明。
//
// 与单服务的 lane database 帮助类不同，这个项目的 postgres 用例天然跨服务：同一条用例会在同一个成员数据库
// 里迁移多个服务各自的 schema（例如 RuntimeHoursMaintenancePostgresAcceptanceTests 同时用到
// industrial_telemetry 与 maintenance；WmsInventoryRpcIdempotencyAcceptanceTests 同时用到 wms 与
// inventory）。因此 ResetSchemaAsync 接受调用方声明的 schema 列表，而不是像单服务帮助类那样固定一个 schema。
//
// 迁移历史表必须落在各自服务的 schema 内才能被这里的 DROP SCHEMA 清理干净：调用方必须显式传入
// MigrationsHistoryTable("__EFMigrationsHistory", <该服务的 Schema>)，否则 EF 会把 __EFMigrationsHistory
// 落在 Npgsql 默认的 public schema——那张表不会被 DROP SCHEMA 删掉，于是下一个用例的 MigrateAsync 会看到
// "历史记录说全部已应用"而直接跳过迁移，而目标 schema 里其实什么表都没有，报 42P01 relation does not exist。
//
// 涉及的两个测试类共用 [Collection(CollectionName)]（本文件同时声明该 collection，
// DisableParallelization = true），因此同一成员数据库不会被并发访问。
internal static class AcceptancePostgresLaneDatabase
{
    internal const string CollectionName = "Acceptance PostgreSQL lane";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES must be set for Acceptance PostgreSQL lane tests.");

    /// <summary>删除调用方声明的每个 schema（含其中的 <c>__EFMigrationsHistory</c>），让调用方从干净状态迁移。</summary>
    internal static async Task ResetSchemaAsync(params string[] schemas)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var schema in schemas)
        {
            var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }

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

[CollectionDefinition(AcceptancePostgresLaneDatabase.CollectionName, DisableParallelization = true)]
public sealed class AcceptancePostgresLaneCollection;
