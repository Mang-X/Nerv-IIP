using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

// NERV-688 拆解③：Quality 的 PostgreSQL profile 用例统一使用 lane runner 注入的成员数据库
// （NERV_IIP_TEST_POSTGRES），不再自建内层数据库——内层数据库既不能被外层失败诊断读取，也不能
// 被外层 finally 清理证明。每个用例先删除 quality schema 再迁移，因此同一成员数据库内的用例
// 之间没有残留；这些类共用 QualityPostgresLaneCollection，xUnit 因而串行执行它们。
//
// 注意：本仓库这批 Quality Postgres profile 用例都不经由 UseCap/AddCap 走真实 CAP 事务性
// outbox（都是用 stub IIntegrationEventPublisher 断言），因此实际落库的只有 quality schema，
// 不涉及 CAP 默认的 cap schema —— 与 MasterData 那批需要同时声明 cap schema 的先例不同。
internal static class QualityPostgresLaneDatabase
{
    internal const string CollectionName = "QualityPostgresLane";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
        ?? throw new InvalidOperationException("NERV_IIP_TEST_POSTGRES must be set for Quality PostgreSQL profile tests.");

    /// <summary>删除 quality schema（含 <c>__EFMigrationsHistory</c>），让调用方从干净状态迁移。</summary>
    internal static async Task ResetSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(QualityFacts.Schema);
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

[CollectionDefinition(QualityPostgresLaneDatabase.CollectionName, DisableParallelization = true)]
public sealed class QualityPostgresLaneCollection;
