using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using Npgsql;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

// NERV-688 拆解③：IndustrialTelemetry 的 PostgreSQL profile 用例统一使用 lane runner 注入的成员数据库
// （NERV_IIP_TEST_POSTGRES），不再自建内层数据库——内层数据库既不能被外层失败诊断读取，也不能被外层
// finally 清理证明。每个用例先删除 industrial_telemetry schema 再迁移，因此同一成员数据库内的用例之间
// 没有残留。IndustrialTelemetry 的业务表与 CAP 的 CAPPublishedMessage/CAPReceivedMessage/CAPLock 表经
// 实测（migrate 后查 pg_namespace/pg_class）确认全部落在同一个 industrial_telemetry schema，因此只需声
// 明一个诊断 schema。
//
// CollectionName 复用 WebApplicationFactoryCollection 而不是新建一个 collection：
// IndustrialTelemetryIdempotentConcurrencyTests 已经因 FastEndpoints 8.1.0 的静态进程状态被绑定到
// WebApplicationFactoryCollection（一个类只能属于一个 collection），如果给它换成独立的 PostgresLane
// collection，该类就会与仍留在 WebApplicationFactoryCollection 的其它类并行，破坏原有的同进程串行前提。
// WebApplicationFactoryCollection 本已 DisableParallelization = true，把 ReadFace/Historian/OeeQuery
// 三个类也并入其中，既不引入新的并行冲突，又能让四个类天然互斥地共用同一个成员数据库。
internal static class IndustrialTelemetryPostgresLaneDatabase
{
    internal const string CollectionName = WebApplicationFactoryCollection.Name;

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES must be set for IndustrialTelemetry PostgreSQL profile tests.");

    /// <summary>删除 industrial_telemetry schema（含 CAP 表与 __EFMigrationsHistory），让调用方从干净状态迁移。</summary>
    internal static async Task ResetSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(IndustrialTelemetryFacts.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
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
