using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain;
using Npgsql;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// NERV-688 拆解③：Scheduling 的 PostgreSQL profile 用例统一使用 lane runner 注入的成员数据库
// （NERV_IIP_TEST_POSTGRES），不再自建内层数据库——内层数据库既不能被外层失败诊断读取，也不能
// 被外层 finally 清理证明。每个用例先删除 scheduling schema 再迁移，因此同一成员数据库内的用例
// 之间没有残留；这些类共用 SchedulingPostgresLaneCollection，xUnit 因而串行执行它们。
internal static class SchedulingPostgresLaneDatabase
{
    internal const string CollectionName = "SchedulingPostgresLane";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES must be set for Scheduling PostgreSQL profile tests.");

    /// <summary>删除 scheduling schema（含 <c>__EFMigrationsHistory</c>），让调用方从干净状态迁移。</summary>
    internal static async Task ResetSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(SchedulingFacts.Schema);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>证明用例跑在 lane 治理的成员数据库上，而不是某个自建的内层数据库。</summary>
    internal static void AssertUsesGovernedDatabase(DbContext dbContext)
    {
        var governed = new NpgsqlConnectionStringBuilder(ConnectionString);
        Assert.Equal(governed.Database, dbContext.Database.GetDbConnection().Database);
    }
}

[CollectionDefinition(SchedulingPostgresLaneDatabase.CollectionName, DisableParallelization = true)]
public sealed class SchedulingPostgresLaneCollection;
