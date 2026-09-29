using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// #3924：成员账号目录的 EF 查询（成员关系子查询 + 启用过滤 + ID 批量 + 关键字 + 分页）只有真库才翻译执行；
/// WebApplicationFactory 默认走 InMemory 服务，不经过这个仓储，所以组织/环境过滤只能由这里钉住。
/// </summary>
public sealed class IamMemberAccountDirectoryPostgresTests
{
    [IamMemberAccountPostgresFact]
    public async Task Production_planner_query_filters_role_scope_and_active_accounts_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(postgresConnectionString, "nerv_iam_member_accounts");
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", database.ConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        database.AssertOwns(Environment.GetEnvironmentVariable("ConnectionStrings__IamDb"));
        await using var factory = new WebApplicationFactory<Program>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            database.AssertOwns(db.Database.GetConnectionString());
            await scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>().MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IamSeedService>().SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadAsync<AuthResponse>(login)).AccessToken);

        var planner = await CreateAsync(client, "planner-valid", member: true, roleId: "role-production-planner");
        await CreateAsync(client, "planner-nonmember", member: true);
        var disabled = await CreateAsync(client, "planner-disabled", member: true, roleId: "role-production-planner");
        await CreateAsync(client, "planner-expired", member: true, roleId: "role-production-planner", expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/iam/v1/users/{disabled}/disable", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "local-internal-service-token");

        var page = await ReadAsync<MemberIdsPage>(await client.GetAsync(
            "/internal/iam/v1/production-planner-members?organizationId=org-001&environmentId=env-dev&pageIndex=1&pageSize=1"));
        Assert.Equal(1, page.TotalCount);
        Assert.Equal([planner], page.Items);

        var otherScope = await ReadAsync<MemberIdsPage>(await client.GetAsync(
            "/internal/iam/v1/production-planner-members?organizationId=org-other&environmentId=env-dev"));
        Assert.Empty(otherScope.Items);
    }

    [IamMemberAccountPostgresFact]
    public async Task Member_account_directory_lists_only_enabled_members_of_the_organization_environment_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(postgresConnectionString, "nerv_iam_member_accounts");
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState
            .SetEnvironmentVariable("Persistence__Provider", "PostgreSQL")
            .SetEnvironmentVariable("ConnectionStrings__IamDb", database.ConnectionString)
            .SetEnvironmentVariable("Iam__Seed__Enabled", "true")
            .SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!")
            .SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        database.AssertOwns(Environment.GetEnvironmentVariable("ConnectionStrings__IamDb"));
        await using var factory = new WebApplicationFactory<Program>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            database.AssertOwns(db.Database.GetConnectionString());
            await scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>().MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IamSeedService>().SeedAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadAsync<AuthResponse>(login)).AccessToken);

        var member = await CreateAsync(client, "acct-member", member: true);
        var outsider = await CreateAsync(client, "acct-outsider", member: false);
        var disabledMember = await CreateAsync(client, "acct-disabled", member: true);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/iam/v1/users/{disabledMember}/disable", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "local-internal-service-token");

        // 关键字大小写不敏感；默认只列启用的成员账号。
        var enabledOnly = await ListAsync(client, "organizationId=org-001&environmentId=env-dev&keyword=ACCT-&pageIndex=1&pageSize=10");
        Assert.Equal(1, enabledOnly.TotalCount);
        Assert.Equal([member], enabledOnly.Items.Select(x => x.UserId));

        var batch = await ListAsync(
            client,
            $"organizationId=org-001&environmentId=env-dev&includeDisabled=true&userIds={member}&userIds={outsider}&userIds={disabledMember}&pageIndex=1&pageSize=10");
        Assert.Equal(
            new[] { member, disabledMember }.Order(StringComparer.Ordinal),
            batch.Items.Select(x => x.UserId).Order(StringComparer.Ordinal));
        Assert.False(batch.Items.Single(x => x.UserId == disabledMember).Enabled);

        // 同一环境 ID、不同组织，以及同一组织、不同环境：都不得看到 org-001/env-dev 的成员。
        foreach (var otherScope in new[] { "organizationId=org-other&environmentId=env-dev", "organizationId=org-001&environmentId=env-other" })
        {
            var other = await ListAsync(client, $"{otherScope}&includeDisabled=true&pageIndex=1&pageSize=10");
            Assert.Empty(other.Items);
            Assert.Equal(0, other.TotalCount);
        }
    }

    private static async Task<string> CreateAsync(
        HttpClient client,
        string loginName,
        bool member,
        string roleId = "role-erp-sales",
        DateTimeOffset? expiresAt = null)
    {
        var create = await client.PostAsJsonAsync(
            "/api/iam/v1/users",
            new { loginName, email = $"{loginName}@nerv-iip.local", password = "Operator123!", accountExpiresAtUtc = expiresAt });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var userId = (await ReadAsync<CreatedUser>(create)).UserId;
        if (member)
        {
            var assign = await client.PutAsJsonAsync($"/api/iam/v1/users/{userId}/membership", new { roleIds = new[] { roleId } });
            assign.EnsureSuccessStatusCode();
        }

        return userId;
    }

    private static async Task<MemberAccountPage> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/internal/iam/v1/member-accounts?{query}");
        response.EnsureSuccessStatusCode();
        return await ReadAsync<MemberAccountPage>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        where T : class
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }

    private sealed record AuthResponse(string AccessToken);
    private sealed record CreatedUser(string UserId);
    private sealed record MemberAccount(string UserId, string LoginName, string? DisplayName, bool Enabled);
    private sealed record MemberAccountPage(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<MemberAccount> Items);
    private sealed record MemberIdsPage(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<string> Items);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}

internal sealed class IamMemberAccountPostgresFactAttribute : FactAttribute
{
    public IamMemberAccountPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL IAM member-account directory proof.";
        }
    }
}
