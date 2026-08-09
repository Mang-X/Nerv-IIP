# IAM 持久化认证基础实施计划

> **面向智能体执行者：** 必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 将现有 IAM 内存态骨架转化为持久化后端认证基础，包括 PostgreSQL 迁移、种子行为、JWT 访问令牌、刷新令牌轮换、schema 约定测试和文档。

**架构：** IAM 保持为 CleanDDD 风格的三项目服务。为较早的验证脚本保留当前 InMemory profile，并添加 PostgreSQL profile，其中包含 `iam` schema 所有权、服务 schema 的 EF 迁移历史、实体配置、迁移运行器，以及用于登录、刷新、吊销、`/me` 和 Connector Host 凭证验证的聚焦 Web/Application 服务。

**技术栈：** .NET 10、FastEndpoints、MediatR、EF Core 10.0.8、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1、netcorepal 仓库/工作单元原语、ASP.NET Core `PasswordHasher<T>`、JWT Bearer 原语、xUnit、PowerShell。

---

## 完成记录

本计划从提交 `c707269 docs: design iam persistent auth foundation` 开始，该提交位于分支 `codex/iam-persistent-auth-foundation` 上。

已知交接说明：本计划开始前 `skills-lock.json` 已处于脏状态，先前审核未报告文本差异。除非用户明确要求，否则不得暂存或修改该文件。

合并后审核说明：实现通过 `8c6bcde Merge pull request #12 from Mang-X/codex/iam-persistent-auth-foundation` 落地。下面的原始复选框跟踪在分支期间未更新，因此这些复选框是过时的历史记录，不是准确的状态信号。后续审核收紧了 PostgreSQL IAM 管理 endpoint，使用户/角色/会话管理路由在接触持久化之前拒绝匿名调用方；用户/角色写入管理仍有意不产品化，并且只有权限检查通过后才返回 501。

## 边界

1. 不得实施 Gateway 全局 bearer 授权或权限策略。
2. 不得添加控制台登录 UI、路由、导航、样式、设计令牌或组件库变更。
3. 不得实施 OAuth/OIDC、SSO、MFA、WebAuthn、ABAC、委托或第三方授权流程。
4. 不得创建客户发布迁移包、安装程序、备份脚本或恢复演练。
5. 不得验证 GaussDB、DMDB 或其他 provider profile。
6. 除非针对性测试证明存在兼容性问题，否则保持当前 InMemory IAM profile 可用。
7. 不得暂存无关的 `skills-lock.json` 变更。

## 文件结构图

```text
backend/services/Iam/src/Nerv.IIP.Iam.Domain/
  IamFacts.cs
  AggregatesModel/
    OrganizationAggregate/Organization.cs
    UserAggregate/User.cs
    RoleAggregate/Role.cs
    MembershipAggregate/Membership.cs
    UserSessionAggregate/UserSession.cs
    ConnectorHostCredentialAggregate/ConnectorHostCredential.cs
    SeedAggregate/SeedManifest.cs
  DomainEvents/IamDomainEvents.cs

backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/
  InMemoryIamStore.cs
  ApplicationDbContext.cs
  IamPersistenceServiceCollectionExtensions.cs
  IamDatabaseMigrationRunner.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Iam/src/Nerv.IIP.Iam.Web/
  Program.cs
  Application/Auth/IamAuthModels.cs
  Application/Auth/IamAuthService.cs
  Application/Auth/IamTokenService.cs
  Application/Auth/IamPasswordService.cs
  Application/Seed/IamSeedOptions.cs
  Application/Seed/IamSeedService.cs
  Endpoints/Auth/AuthEndpoints.cs
  Endpoints/Users/UserEndpoints.cs
  Endpoints/Roles/RoleEndpoints.cs
  Endpoints/Sessions/SessionEndpoints.cs

backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/
  Nerv.IIP.Iam.Web.Tests.csproj
  IamFoundationTests.cs
  IamPostgresProfileTests.cs
  IamSchemaConventionTests.cs

docs/architecture/
  database-schema-catalog.md
  database-schema-conventions.md
  iam-authentication-baseline.md
  implementation-readiness.md

README.md
scripts/verify-iam-persistent-auth-foundation.ps1
```

## 任务 1：添加预期失败的持久化认证测试

**文件：**

- 修改：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj`
- 创建：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`

- [ ] **步骤 1：添加必需的测试项目引用**

修改测试项目引用，使 PostgreSQL 测试可以检查 IAM 基础设施和 schema 约定：

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Nerv.IIP.Iam.Web\Nerv.IIP.Iam.Web.csproj" />
    <ProjectReference Include="..\..\..\..\common\Testing\Nerv.IIP.Testing\Nerv.IIP.Testing.csproj" />
  </ItemGroup>
```

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

预期结果：现有测试仍可编译。如果在添加新测试之前失败，停止工作并检查项目引用路径。

- [ ] **步骤 2：添加预期失败的 PostgreSQL 登录/刷新/吊销测试**

创建 `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Iam.Infrastructure;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamPostgresProfileTests
{
    [Fact]
    public async Task Postgres_profile_seeds_admin_and_persists_login_refresh_logout_and_connector_validation()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var previousProvider = Environment.GetEnvironmentVariable("Persistence__Provider");
        var previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__IamDb");
        var previousAutoSeed = Environment.GetEnvironmentVariable("Iam__Seed__Enabled");
        var previousAdminPassword = Environment.GetEnvironmentVariable("Iam__Seed__AdminPassword");
        var previousConnectorSecret = Environment.GetEnvironmentVariable("Iam__Seed__ConnectorHostSecret");

        Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
        Environment.SetEnvironmentVariable("ConnectionStrings__IamDb", connectionString);
        Environment.SetEnvironmentVariable("Iam__Seed__Enabled", "true");
        Environment.SetEnvironmentVariable("Iam__Seed__AdminPassword", "Admin123!");
        Environment.SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", "local-connector-secret");

        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureDeletedAsync();
                var migrationRunner = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
                await migrationRunner.MigrateAsync();
                var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
                await seed.SeedAsync(CancellationToken.None);
                await seed.SeedAsync(CancellationToken.None);
                await AssertMigrationsHistoryTableInSchemaAsync(db, "iam");
            }

            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
            login.EnsureSuccessStatusCode();
            var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(auth);
            Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
            Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
            Assert.False(string.IsNullOrWhiteSpace(auth.SessionId));

            client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
            var me = await client.GetFromJsonAsync<MeResponse>("/api/iam/v1/me");
            Assert.Equal("user-admin", me!.UserId);
            Assert.Equal("admin", me.LoginName);
            Assert.Equal("user", me.PrincipalType);

            var refresh = await client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
            refresh.EnsureSuccessStatusCode();
            var rotated = await refresh.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotEqual(auth.RefreshToken, rotated!.RefreshToken);

            var oldRefresh = await client.PostAsJsonAsync("/api/iam/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

            client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
            var logout = await client.PostAsJsonAsync("/api/iam/v1/auth/logout", new { sessionId = rotated.SessionId });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

            var meAfterLogout = await client.GetAsync("/api/iam/v1/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);

            var connector = await client.PostAsJsonAsync("/api/iam/v1/connectors/credentials/validate", new { connectorHostId = "connector-host-001", secret = "local-connector-secret" });
            connector.EnsureSuccessStatusCode();
            var principal = await connector.Content.ReadFromJsonAsync<ConnectorPrincipalResponse>();
            Assert.Equal("connector-host", principal!.PrincipalType);
            Assert.Equal("org-001", principal.OrganizationId);
            Assert.Equal("env-dev", principal.EnvironmentId);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await verifyDb.Users.CountAsync(x => x.LoginName == "admin"));
            Assert.Equal(1, await verifyDb.ConnectorHostCredentials.CountAsync(x => x.ConnectorHostId == "connector-host-001"));
            Assert.DoesNotContain("Admin123!", await verifyDb.Users.Select(x => x.PasswordHash).SingleAsync(x => true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", previousProvider);
            Environment.SetEnvironmentVariable("ConnectionStrings__IamDb", previousConnectionString);
            Environment.SetEnvironmentVariable("Iam__Seed__Enabled", previousAutoSeed);
            Environment.SetEnvironmentVariable("Iam__Seed__AdminPassword", previousAdminPassword);
            Environment.SetEnvironmentVariable("Iam__Seed__ConnectorHostSecret", previousConnectorSecret);
        }
    }

    private static async Task AssertMigrationsHistoryTableInSchemaAsync(ApplicationDbContext db, string schema)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = '__EFMigrationsHistory'
            )
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "schema";
        parameter.Value = schema;
        command.Parameters.Add(parameter);

        var exists = (bool?)await command.ExecuteScalarAsync() ?? false;
        Assert.True(exists, $"Expected EF migrations history table in schema '{schema}'.");
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId);
    private sealed record MeResponse(string UserId, string LoginName, string Email, string PrincipalType);
    private sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);
}
```

- [ ] **步骤 3：运行新测试并验证预期红灯状态**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter FullyQualifiedName~IamPostgresProfileTests
```

预期结果：编译失败，因为 `ApplicationDbContext`、`IamDatabaseMigrationRunner`、`IamSeedService` 和 PostgreSQL IAM DbSet 尚不存在。

## 任务 2：添加预期失败的 IAM Schema 约定测试

**文件：**

- 创建：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamSchemaConventionTests.cs`

- [ ] **步骤 1：编写 schema 约定测试**

创建 `IamSchemaConventionTests.cs`：

```csharp
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamSchemaConventionTests
{
    [Fact]
    public void Iam_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(Organization),
            typeof(IamEnvironment),
            typeof(User),
            typeof(Role),
            typeof(RolePermission),
            typeof(Membership),
            typeof(MembershipRole),
            typeof(UserSession),
            typeof(ConnectorHostCredential),
            typeof(ConnectorHostCredentialCapability),
            typeof(SeedManifest),
        };

        var stringKeys = new[]
        {
            new StringKeyRule(typeof(Organization), nameof(Organization.Id)),
            new StringKeyRule(typeof(IamEnvironment), nameof(IamEnvironment.Id)),
            new StringKeyRule(typeof(User), nameof(User.Id)),
            new StringKeyRule(typeof(Role), nameof(Role.Id)),
            new StringKeyRule(typeof(Membership), nameof(Membership.Id)),
            new StringKeyRule(typeof(UserSession), nameof(UserSession.Id)),
            new StringKeyRule(typeof(ConnectorHostCredential), nameof(ConnectorHostCredential.Id)),
            new StringKeyRule(typeof(SeedManifest), nameof(SeedManifest.Id)),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "IAM", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "IAM", businessEntities));
        failures.AddRange(SchemaConventionAssertions.StringStronglyTypedKeysAreExplicit(fixture.DbContext, "IAM", stringKeys));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "IAM", "iam"));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["ConnectionStrings:IamDb"] = "Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv",
            })
            .Build();
        services.AddIamPersistence(configuration);

        return new SchemaFixture(services.BuildServiceProvider());
    }

    private sealed class SchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public SchemaFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }
}
```

- [ ] **步骤 2：运行 schema 测试并验证预期红灯状态**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter FullyQualifiedName~IamSchemaConventionTests
```

预期结果：编译失败，因为 IAM 聚合类型和 `AddIamPersistence` 尚不存在。

## 任务 3：添加 IAM 领域模型

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/Nerv.IIP.Iam.Domain.csproj`
- 修改或保留：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/IamFacts.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/OrganizationAggregate/Organization.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/UserAggregate/User.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/RoleAggregate/Role.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/MembershipAggregate/Membership.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/UserSessionAggregate/UserSession.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/ConnectorHostCredentialAggregate/ConnectorHostCredential.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/SeedAggregate/SeedManifest.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/DomainEvents/IamDomainEvents.cs`

- [ ] **步骤 1：添加 netcorepal 领域引用**

修改 IAM Domain 项目：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="NetCorePal.Extensions.Domain.Abstractions" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

- [ ] **步骤 2：添加 Organization 和 Environment 聚合文件**

创建 `OrganizationAggregate/Organization.cs`：

```csharp
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;

public partial record OrganizationId : IStringStronglyTypedId;
public partial record IamEnvironmentId : IStringStronglyTypedId;

public sealed class Organization : Entity<OrganizationId>, IAggregateRoot
{
    private Organization()
    {
        Id = new OrganizationId(string.Empty);
    }

    public Organization(OrganizationId id, string name, string status)
    {
        Id = id;
        Name = name;
        Status = status;
    }

    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);
}

public sealed class IamEnvironment : Entity<IamEnvironmentId>, IAggregateRoot
{
    private IamEnvironment()
    {
        Id = new IamEnvironmentId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
    }

    public IamEnvironment(IamEnvironmentId id, OrganizationId organizationId, string name, string status)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Status = status;
    }

    public OrganizationId OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);
}
```

- [ ] **步骤 3：添加 User 聚合文件**

创建 `UserAggregate/User.cs`：

```csharp
using Nerv.IIP.Iam.Domain.DomainEvents;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;

public partial record UserId : IStringStronglyTypedId;

public sealed class User : Entity<UserId>, IAggregateRoot
{
    private User()
    {
        Id = new UserId(string.Empty);
    }

    public User(UserId id, string loginName, string email, string passwordHash, bool enabled, string securityStamp, int permissionVersion)
    {
        Id = id;
        LoginName = loginName;
        Email = email;
        PasswordHash = passwordHash;
        Enabled = enabled;
        SecurityStamp = securityStamp;
        PermissionVersion = permissionVersion;
    }

    public string LoginName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public int PermissionVersion { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public int FailedLoginCount { get; private set; }
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        LastLoginAtUtc = now;
        FailedLoginCount = 0;
        this.AddDomainEvent(new UserLoggedInDomainEvent(Id.Id, now));
    }

    public void RecordFailedLogin()
    {
        FailedLoginCount++;
    }

    public void Disable()
    {
        Enabled = false;
        SecurityStamp = Guid.NewGuid().ToString("n");
        PermissionVersion++;
    }

    public void UpdatePasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid().ToString("n");
        PermissionVersion++;
    }
}
```

- [ ] **步骤 4：添加 Role 和 Membership 聚合文件**

创建 `RoleAggregate/Role.cs`：

```csharp
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;

public partial record RoleId : IStringStronglyTypedId;
public partial record RolePermissionId : IStringStronglyTypedId;

public sealed class Role : Entity<RoleId>, IAggregateRoot
{
    private readonly List<RolePermission> permissions = [];

    private Role()
    {
        Id = new RoleId(string.Empty);
    }

    public Role(RoleId id, string roleName, IEnumerable<string> permissionCodes)
    {
        Id = id;
        RoleName = roleName;
        ReplacePermissions(permissionCodes);
    }

    public string RoleName { get; private set; } = string.Empty;
    public IReadOnlyCollection<RolePermission> Permissions => permissions;
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);

    public void ReplacePermissions(IEnumerable<string> permissionCodes)
    {
        permissions.Clear();
        foreach (var code in permissionCodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            permissions.Add(new RolePermission(new RolePermissionId($"{Id.Id}:{code}"), Id, code));
        }
    }
}

public sealed class RolePermission : Entity<RolePermissionId>
{
    private RolePermission()
    {
        Id = new RolePermissionId(string.Empty);
        RoleId = new RoleId(string.Empty);
    }

    internal RolePermission(RolePermissionId id, RoleId roleId, string permissionCode)
    {
        Id = id;
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public RoleId RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
}
```

创建 `MembershipAggregate/Membership.cs`：

```csharp
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;

public partial record MembershipId : IStringStronglyTypedId;
public partial record MembershipRoleId : IStringStronglyTypedId;

public sealed class Membership : Entity<MembershipId>, IAggregateRoot
{
    private readonly List<MembershipRole> roles = [];

    private Membership()
    {
        Id = new MembershipId(string.Empty);
        UserId = new UserId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
        EnvironmentId = new IamEnvironmentId(string.Empty);
    }

    public Membership(MembershipId id, UserId userId, OrganizationId organizationId, IamEnvironmentId environmentId, IEnumerable<RoleId> roleIds)
    {
        Id = id;
        UserId = userId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ReplaceRoles(roleIds);
    }

    public UserId UserId { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public IamEnvironmentId EnvironmentId { get; private set; }
    public IReadOnlyCollection<MembershipRole> Roles => roles;

    public void ReplaceRoles(IEnumerable<RoleId> roleIds)
    {
        roles.Clear();
        foreach (var roleId in roleIds.Distinct().OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            roles.Add(new MembershipRole(new MembershipRoleId($"{Id.Id}:{roleId.Id}"), Id, roleId));
        }
    }
}

public sealed class MembershipRole : Entity<MembershipRoleId>
{
    private MembershipRole()
    {
        Id = new MembershipRoleId(string.Empty);
        MembershipId = new MembershipId(string.Empty);
        RoleId = new RoleId(string.Empty);
    }

    internal MembershipRole(MembershipRoleId id, MembershipId membershipId, RoleId roleId)
    {
        Id = id;
        MembershipId = membershipId;
        RoleId = roleId;
    }

    public MembershipId MembershipId { get; private set; }
    public RoleId RoleId { get; private set; }
}
```

- [ ] **步骤 5：添加 UserSession 和 Connector 凭证聚合文件**

创建 `UserSessionAggregate/UserSession.cs`：

```csharp
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.DomainEvents;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;

public partial record UserSessionId : IStringStronglyTypedId;

public sealed class UserSession : Entity<UserSessionId>, IAggregateRoot
{
    private UserSession()
    {
        Id = new UserSessionId(string.Empty);
        UserId = new UserId(string.Empty);
    }

    public UserSession(UserSessionId id, UserId userId, string refreshTokenHash, DateTimeOffset issuedAtUtc, DateTimeOffset expiresAtUtc, int permissionVersion, string? clientInfo, string? ipAddress)
    {
        Id = id;
        UserId = userId;
        RefreshTokenHash = refreshTokenHash;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        PermissionVersion = permissionVersion;
        ClientInfo = clientInfo;
        IpAddress = ipAddress;
        this.AddDomainEvent(new UserSessionCreatedDomainEvent(Id.Id, UserId.Id, issuedAtUtc));
    }

    public UserId UserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public int PermissionVersion { get; private set; }
    public string? ClientInfo { get; private set; }
    public string? IpAddress { get; private set; }

    public bool CanRefresh(DateTimeOffset now) => RevokedAtUtc is null && ExpiresAtUtc > now;

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = now;
        RevokedReason = reason;
        this.AddDomainEvent(new UserSessionRevokedDomainEvent(Id.Id, UserId.Id, now, reason));
    }
}
```

创建 `ConnectorHostCredentialAggregate/ConnectorHostCredential.cs`：

```csharp
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;

public partial record ConnectorHostCredentialId : IStringStronglyTypedId;
public partial record ConnectorHostCredentialCapabilityId : IStringStronglyTypedId;

public sealed class ConnectorHostCredential : Entity<ConnectorHostCredentialId>, IAggregateRoot
{
    private readonly List<ConnectorHostCredentialCapability> capabilities = [];

    private ConnectorHostCredential()
    {
        Id = new ConnectorHostCredentialId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
        EnvironmentId = new IamEnvironmentId(string.Empty);
    }

    public ConnectorHostCredential(ConnectorHostCredentialId id, string connectorHostId, OrganizationId organizationId, IamEnvironmentId environmentId, string secretHash, DateTimeOffset validFromUtc, DateTimeOffset? validToUtc, IEnumerable<string> capabilityScope)
    {
        Id = id;
        ConnectorHostId = connectorHostId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        SecretHash = secretHash;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        ReplaceCapabilities(capabilityScope);
    }

    public string ConnectorHostId { get; private set; } = string.Empty;
    public OrganizationId OrganizationId { get; private set; }
    public IamEnvironmentId EnvironmentId { get; private set; }
    public string SecretHash { get; private set; } = string.Empty;
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidToUtc { get; private set; }
    public IReadOnlyCollection<ConnectorHostCredentialCapability> Capabilities => capabilities;

    public bool IsValidAt(DateTimeOffset now) => ValidFromUtc <= now && (ValidToUtc is null || ValidToUtc > now);

    public void ReplaceSecretHash(string secretHash)
    {
        SecretHash = secretHash;
    }

    public void ReplaceCapabilities(IEnumerable<string> capabilityScope)
    {
        capabilities.Clear();
        foreach (var capability in capabilityScope.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            capabilities.Add(new ConnectorHostCredentialCapability(new ConnectorHostCredentialCapabilityId($"{Id.Id}:{capability}"), Id, capability));
        }
    }
}

public sealed class ConnectorHostCredentialCapability : Entity<ConnectorHostCredentialCapabilityId>
{
    private ConnectorHostCredentialCapability()
    {
        Id = new ConnectorHostCredentialCapabilityId(string.Empty);
        ConnectorHostCredentialId = new ConnectorHostCredentialId(string.Empty);
    }

    internal ConnectorHostCredentialCapability(ConnectorHostCredentialCapabilityId id, ConnectorHostCredentialId connectorHostCredentialId, string capabilityCode)
    {
        Id = id;
        ConnectorHostCredentialId = connectorHostCredentialId;
        CapabilityCode = capabilityCode;
    }

    public ConnectorHostCredentialId ConnectorHostCredentialId { get; private set; }
    public string CapabilityCode { get; private set; } = string.Empty;
}
```

- [ ] **步骤 6：添加 SeedManifest 和领域事件**

创建 `SeedAggregate/SeedManifest.cs`：

```csharp
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;

public partial record SeedManifestId : IStringStronglyTypedId;

public sealed class SeedManifest : Entity<SeedManifestId>, IAggregateRoot
{
    private SeedManifest()
    {
        Id = new SeedManifestId(string.Empty);
    }

    public SeedManifest(SeedManifestId id, string seedName, string seedVersion, string ownerService, DateTimeOffset appliedAtUtc)
    {
        Id = id;
        SeedName = seedName;
        SeedVersion = seedVersion;
        OwnerService = ownerService;
        AppliedAtUtc = appliedAtUtc;
    }

    public string SeedName { get; private set; } = string.Empty;
    public string SeedVersion { get; private set; } = string.Empty;
    public string OwnerService { get; private set; } = string.Empty;
    public DateTimeOffset AppliedAtUtc { get; private set; }
}
```

创建 `DomainEvents/IamDomainEvents.cs`：

```csharp
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.DomainEvents;

public sealed record UserLoggedInDomainEvent(string UserId, DateTimeOffset LoggedInAtUtc) : IDomainEvent;
public sealed record UserSessionCreatedDomainEvent(string SessionId, string UserId, DateTimeOffset IssuedAtUtc) : IDomainEvent;
public sealed record UserSessionRevokedDomainEvent(string SessionId, string UserId, DateTimeOffset RevokedAtUtc, string Reason) : IDomainEvent;
```

- [ ] **步骤 7：运行编译并检查错误**

运行：

```powershell
dotnet build backend/services/Iam/src/Nerv.IIP.Iam.Domain/Nerv.IIP.Iam.Domain.csproj
```

预期结果：Domain 项目构建成功。如果强类型 ID 源生成需要其他包引用，对比 AppHub/Ops 项目文件，只添加缺失的 netcorepal 包引用。

## 任务 4：添加 IAM 持久化 Profile

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Nerv.IIP.Iam.Infrastructure.csproj`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/IamPersistenceServiceCollectionExtensions.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/IamDatabaseMigrationRunner.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs`

- [ ] **步骤 1：添加 Infrastructure 包引用**

修改 IAM Infrastructure 项目：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="NetCorePal.Extensions.Repository.EntityFrameworkCore" />
    <PackageReference Include="NetCorePal.Extensions.DistributedTransactions.CAP.PostgreSQL" />
    <ProjectReference Include="..\Nerv.IIP.Iam.Domain\Nerv.IIP.Iam.Domain.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

- [ ] **步骤 2：添加 DbContext**

创建 `ApplicationDbContext.cs`，为每个 IAM 持久化实体提供 DbSet。遵循 AppHub/Ops 默认 schema 风格：

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Iam.Infrastructure;

public sealed partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<IamEnvironment> Environments => Set<IamEnvironment>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<ConnectorHostCredential> ConnectorHostCredentials => Set<ConnectorHostCredential>();
    public DbSet<ConnectorHostCredentialCapability> ConnectorHostCredentialCapabilities => Set<ConnectorHostCredentialCapability>();
    public DbSet<SeedManifest> SeedManifests => Set<SeedManifest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("iam");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
```

- [ ] **步骤 3：添加持久化扩展和迁移运行器**

创建 `IamPersistenceServiceCollectionExtensions.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Iam.Infrastructure;

public static class IamPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddIamPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("IamDb")
                ?? throw new InvalidOperationException("Connection string 'IamDb' is required when IAM uses PostgreSQL persistence.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "iam")));
            services.AddRepositories(typeof(ApplicationDbContext).Assembly);
            services.AddUnitOfWork<ApplicationDbContext>();
            services.AddScoped<IamDatabaseMigrationRunner>();
            return services;
        }

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<InMemoryIamStore>();
            return services;
        }

        throw new NotSupportedException($"Persistence provider '{provider}' is not supported by IAM yet.");
    }
}
```

创建 `IamDatabaseMigrationRunner.cs`：

```csharp
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Iam.Infrastructure;

public sealed class IamDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
```

- [ ] **步骤 4：添加带注释和索引的实体配置**

为每个聚合区域创建一个配置文件。使用以下精确表名和必需约定：

```csharp
builder.ToTable("users", table => table.HasComment("IAM user login identities and security stamps."));
builder.Property(x => x.Id)
    .HasConversion(x => x.Id, x => new UserId(x))
    .ValueGeneratedNever()
    .HasMaxLength(64)
    .HasComment("IAM user identifier.");
builder.Property(x => x.LoginName).IsRequired().HasMaxLength(128).HasComment("Unique login name.");
builder.Property(x => x.Email).IsRequired().HasMaxLength(256).HasComment("User email address.");
builder.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512).HasComment("ASP.NET Core password hasher output.");
builder.Property(x => x.Enabled).HasComment("Whether this user can authenticate.");
builder.Property(x => x.SecurityStamp).IsRequired().HasMaxLength(128).HasComment("Security stamp used to invalidate access tokens.");
builder.Property(x => x.PermissionVersion).HasComment("Permission version used to invalidate stale authorization snapshots.");
builder.Property(x => x.LastLoginAtUtc).HasComment("Last successful login time in UTC.");
builder.Property(x => x.FailedLoginCount).HasComment("Count of failed login attempts.");
builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");
builder.HasIndex(x => x.LoginName).IsUnique();
builder.HasIndex(x => x.Email).IsUnique();
```

对以下对象重复相同的显式 ID/注释模式：

1. `organizations`：`Id`、`Name`、`Status`、`Deleted`、`RowVersion`。
2. `environments`：`Id`、`OrganizationId`、`Name`、`Status`、`Deleted`、`RowVersion`，在 `{ OrganizationId, Id }` 上建立唯一索引。
3. `roles`：`Id`、`RoleName`、`Deleted`、`RowVersion`，角色名称唯一。
4. `role_permissions`：`Id`、`RoleId`、`PermissionCode`，在 `{ RoleId, PermissionCode }` 上建立唯一索引。
5. `memberships`：`Id`、`UserId`、`OrganizationId`、`EnvironmentId`，在 `{ UserId, OrganizationId, EnvironmentId }` 上建立唯一索引。
6. `membership_roles`：`Id`、`MembershipId`、`RoleId`，在 `{ MembershipId, RoleId }` 上建立唯一索引。
7. `user_sessions`：`Id`、`UserId`、`RefreshTokenHash`、`IssuedAtUtc`、`ExpiresAtUtc`、`RevokedAtUtc`、`RevokedReason`、`PermissionVersion`、`ClientInfo`、`IpAddress`，在 `RefreshTokenHash` 和 `{ UserId, RevokedAtUtc }` 上建立索引。
8. `connector_host_credentials`：`Id`、`ConnectorHostId`、`OrganizationId`、`EnvironmentId`、`SecretHash`、`ValidFromUtc`、`ValidToUtc`，Connector Host id 唯一。
9. `connector_host_credential_capabilities`：`Id`、`ConnectorHostCredentialId`、`CapabilityCode`。
10. `seed_manifests`：`Id`、`SeedName`、`SeedVersion`、`OwnerService`、`AppliedAtUtc`，在 `{ SeedName, SeedVersion }` 上建立唯一索引。

- [ ] **步骤 5：运行 schema 约定测试并验证有意义的失败**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter FullyQualifiedName~IamSchemaConventionTests
```

预期结果：测试可编译。它可能因具体的注释缺失、最大长度缺失或迁移历史 schema 缺失而失败。修复实体配置，直到测试无需运行中数据库即可通过。

## 任务 5：添加 IAM 认证、令牌和种子服务

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthModels.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamPasswordService.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamTokenService.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedOptions.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Program.cs`

- [ ] **步骤 1：添加 Web 包引用**

修改 IAM Web 项目，以包含共享框架提供的 JWT 和密码哈希包。如果编译器要求显式包引用，添加：

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" VersionOverride="10.0.0" />
```

如果集中式包管理拒绝 `VersionOverride`，将 `Microsoft.AspNetCore.Authentication.JwtBearer` 添加到 `backend/Directory.Packages.props`，版本设为 `10.0.0`，并使用普通包引用。此变更严格限定为 JWT Bearer 所需的包引用。

- [ ] **步骤 2：添加认证 DTO**

创建 `Application/Auth/IamAuthModels.cs`：

```csharp
namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed record LoginRequest(string LoginName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string? SessionId);
public sealed record ValidateConnectorCredentialRequest(string ConnectorHostId, string Secret);
public sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId);
public sealed record CurrentPrincipalResponse(string UserId, string LoginName, string Email, string PrincipalType);
public sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);
```

- [ ] **步骤 3：添加密码和令牌服务**

创建 `IamPasswordService.cs`：

```csharp
using Microsoft.AspNetCore.Identity;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class IamPasswordService
{
    private readonly PasswordHasher<User> hasher = new();

    public string Hash(User user, string password) => hasher.HashPassword(user, password);

    public bool Verify(User user, string password)
    {
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
```

创建 `IamTokenService.cs`，使用配置键 `Iam:Jwt:SigningKey` 提供的 HMAC 签名。实现必须创建包含 `sub`、`sessionId`、`principalType`、`securityStamp`、`permissionVersion`、`iat` 和 `jti` claim 的 JWT。它必须公开：

```csharp
public sealed record AccessTokenPrincipal(string SessionId, string UserId, string SecurityStamp, int PermissionVersion);

public sealed class IamTokenService(IConfiguration configuration)
{
    public string CreateAccessToken(User user, UserSession session) { /* build signed JWT */ }
    public AccessTokenPrincipal? TryReadPrincipal(HttpContext httpContext) { /* validate bearer token */ }
    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string HashSecret(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
```

实施 `CreateAccessToken` 和 `TryReadPrincipal` 时，使用 `System.IdentityModel.Tokens.Jwt`、`Microsoft.IdentityModel.Tokens`、`ClaimsIdentity`、`JwtSecurityTokenHandler` 和 `SymmetricSecurityKey`。使用 `ValidateLifetime=true`；如果未配置 `Iam:Jwt:AccessTokenMinutes`，默认有效期为 15 分钟。

- [ ] **步骤 4：添加种子选项和种子服务**

创建 `Application/Seed/IamSeedOptions.cs`：

```csharp
namespace Nerv.IIP.Iam.Web.Application.Seed;

public sealed class IamSeedOptions
{
    public bool Enabled { get; init; }
    public string OrganizationId { get; init; } = "org-001";
    public string OrganizationName { get; init; } = "Nerv IIP";
    public string EnvironmentId { get; init; } = "env-dev";
    public string EnvironmentName { get; init; } = "Development";
    public string AdminUserId { get; init; } = "user-admin";
    public string AdminLoginName { get; init; } = "admin";
    public string AdminEmail { get; init; } = "admin@nerv-iip.local";
    public string AdminPassword { get; init; } = string.Empty;
    public string AdminRoleId { get; init; } = "role-platform-admin";
    public string ConnectorHostCredentialId { get; init; } = "credential-connector-host-001";
    public string ConnectorHostId { get; init; } = "connector-host-001";
    public string ConnectorHostSecret { get; init; } = string.Empty;
}
```

创建 `IamSeedService.cs`。它必须：

1. 读取 `IOptions<IamSeedOptions>`；
2. 当 `Enabled` 为 false 时不执行操作；
3. 启用时要求 `AdminPassword` 和 `ConnectorHostSecret` 非空；
4. 按稳定 ID/业务键 upsert 组织、环境、管理员角色、管理员用户、成员关系、Connector 凭证和种子清单；
5. 角色权限使用 `NervIipSeedPermissions.All`，Connector 能力范围使用 `connectors.*`；
6. 通过 `ApplicationDbContext` 保存变更。

- [ ] **步骤 5：添加认证服务**

创建 `IamAuthService.cs`，提供以下公开方法：

```csharp
public Task<AuthResponse> LoginAsync(string loginName, string password, string? clientInfo, string? ipAddress, CancellationToken cancellationToken);
public Task<AuthResponse> RefreshAsync(string refreshToken, string? clientInfo, string? ipAddress, CancellationToken cancellationToken);
public Task RevokeSessionAsync(string sessionId, string reason, CancellationToken cancellationToken);
public Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken);
public Task<ConnectorPrincipalResponse> ValidateConnectorCredentialAsync(string connectorHostId, string secret, CancellationToken cancellationToken);
```

实施要求：

1. `LoginAsync` 按登录名查找已启用用户、验证密码、记录成功/失败、创建 `UserSession` 并返回令牌对。
2. `RefreshAsync` 对提交的刷新令牌求哈希，查找活动会话，验证用户已启用，以原因 `refresh-rotated` 吊销旧会话，创建新会话并返回令牌对。
3. `RevokeSessionAsync` 是幂等的。
4. `GetCurrentPrincipalAsync` 验证 JWT，然后验证持久化会话、用户启用状态、security stamp 和权限版本。
5. `ValidateConnectorCredentialAsync` 对提交的 secret 求哈希，并检查凭证有效时间窗。
6. 所有未授权路径都以通用消息抛出 `UnauthorizedAccessException`。

- [ ] **步骤 6：在 Program 中接入服务**

修改 `Program.cs`：

```csharp
using FastEndpoints;
using Nerv.IIP.Caching;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
builder.Services.AddNervIipCaching(builder.Configuration, "iam");
builder.Services.AddNervIipObservability(builder.Configuration, "iam");
builder.Services.AddIamPersistence(builder.Configuration);
builder.Services.Configure<IamSeedOptions>(builder.Configuration.GetSection("Iam:Seed"));
builder.Services.AddScoped<IamPasswordService>();
builder.Services.AddScoped<IamTokenService>();
builder.Services.AddScoped<IamAuthService>();
builder.Services.AddScoped<IamSeedService>();

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();

if (string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase)
    && string.Equals(builder.Configuration["Persistence:AutoMigrate"], "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var migrationRunner = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
    await migrationRunner.MigrateAsync();
    var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
    await seed.SeedAsync();
}

app.Run();

public partial class Program;
```

## 任务 6：更新 IAM Endpoint

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Auth/AuthEndpoints.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Users/UserEndpoints.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Roles/RoleEndpoints.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Sessions/SessionEndpoints.cs`

- [ ] **步骤 1：使用认证服务替换 Auth endpoint**

修改 Auth endpoint，使 PostgreSQL 模式使用 `IamAuthService`。通过以下任一方式保留对 InMemory 模式的支持：

1. 将 `InMemoryIamStore` 包装在 `IamAuthService` 后，并在不存在 `ApplicationDbContext` 时使用它；或
2. 注册公共 `IIamAuthService` 接口，并提供独立的内存态和 PostgreSQL 实现。

Endpoint 行为必须符合：

```csharp
[HttpPost("/api/iam/v1/auth/login")]
[AllowAnonymous]
public sealed class LoginEndpoint(IamAuthService auth) : Endpoint<LoginRequest>
{
    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        await IamEndpointResults.WriteAuthResultAsync(
            HttpContext,
            () => auth.LoginAsync(req.LoginName, req.Password, HttpContext.Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct),
            ct);
    }
}
```

`WriteAuthResultAsync` 应支持 `Func<Task<T>>`，捕获 `UnauthorizedAccessException`，将状态设置为 `401`，并写入 `{ title, detail, status }`。

- [ ] **步骤 2：使 `/me` 感知持久化会话**

`GET /api/iam/v1/me` 必须调用 `IamAuthService.GetCurrentPrincipalAsync(HttpContext, ct)`。如果返回 null，调用 `Send.UnauthorizedAsync(ct)`；否则返回 `CurrentPrincipalResponse`。

- [ ] **步骤 3：更新用户/角色/会话读取端点**

在 PostgreSQL 模式下，从 `ApplicationDbContext` 读取，使用 `AsNoTracking()` 并返回最小 DTO：

```csharp
await Send.OkAsync(await db.Users
    .AsNoTracking()
    .OrderBy(x => x.LoginName)
    .Select(x => new { x.UserId, x.LoginName, x.Email, x.Enabled })
    .ToListAsync(ct), ct);
```

实施时，将属性名称调整为实际的强类型 ID 属性（`x.Id.Id`）。谨慎解析可选服务，为早期脚本保留 InMemory 回退：

```csharp
var db = Resolve<ApplicationDbContext?>();
if (db is null)
{
    var store = Resolve<InMemoryIamStore>();
    await HttpContext.Response.WriteAsJsonAsync(store.Users.Select(x => new { x.UserId, x.LoginName, x.Email, x.Enabled }), ct);
    return;
}
```

- [ ] **步骤 4：如实处理占位写入 endpoint**

对于 `POST /users`、`PATCH /users/{userId}`、`POST /users/{userId}/disable`、`POST /roles` 和 `PATCH /roles/{roleId}/permissions`，要么实施真实持久化命令，要么返回带 problem 响应的 `501 Not Implemented`。PostgreSQL 模式下不得返回虚假的占位 ID。

- [ ] **步骤 5：运行 IAM Web 测试**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

预期结果：现有内存态测试通过、schema 约定测试通过；未设置 `NERV_IIP_TEST_POSTGRES` 时跳过 PostgreSQL 测试。

## 任务 7：生成 IAM 迁移和验证脚本

**文件：**

- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Migrations/*`
- 创建：`scripts/verify-iam-persistent-auth-foundation.ps1`

- [ ] **步骤 1：还原 EF 工具**

运行：

```powershell
dotnet tool restore
```

预期结果：以 0 退出。

- [ ] **步骤 2：生成 IAM 初始迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__IamDb = "Host=localhost;Port=15432;Database=nerv_iip_iam_design_time;Username=nerv;Password=nerv"
dotnet tool run dotnet-ef migrations add InitialIamPersistentAuth `
  --project backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Nerv.IIP.Iam.Infrastructure.csproj `
  --startup-project backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj `
  --context Nerv.IIP.Iam.Infrastructure.ApplicationDbContext
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__IamDb -ErrorAction SilentlyContinue
```

预期结果：迁移文件创建在 IAM Infrastructure 的 `Migrations/` 下。

- [ ] **步骤 3：检查迁移**

打开生成的迁移并验证：

1. 已创建 schema `iam`；
2. 所有业务表都有注释；
3. 所有业务列都有注释；
4. 字符串 ID 长度有界；
5. 密码、刷新令牌或 Connector 密钥均不以明文出现；
6. 迁移中未嵌入包含 secret 的数据种子。

- [ ] **步骤 4：添加验证脚本**

创建 `scripts/verify-iam-persistent-auth-foundation.ps1`：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-TcpPort {
  param(
    [string]$HostName,
    [int]$Port,
    [int]$TimeoutSeconds = 90
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
      $connectTask = $client.ConnectAsync($HostName, $Port)
      if ($connectTask.Wait(1000) -and $client.Connected) {
        return
      }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
    finally {
      $client.Dispose()
    }

    Start-Sleep -Milliseconds 500
  } while ((Get-Date) -lt $deadline)

  throw "TCP port $HostName`:$Port did not become available within $TimeoutSeconds seconds."
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$env:NERV_IIP_POSTGRES_PORT = $postgresPort
$iamTests = Join-Path $root "backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify IAM persistent auth foundation."
}

docker compose -f $composeFile up -d postgres
Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort)

dotnet tool restore

$previous = $env:NERV_IIP_TEST_POSTGRES
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_iam_migration_verify;Username=nerv;Password=nerv"
try {
  dotnet test $iamTests --filter "FullyQualifiedName~IamPostgresProfileTests|FullyQualifiedName~IamSchemaConventionTests"
}
finally {
  $env:NERV_IIP_TEST_POSTGRES = $previous
}

dotnet test backend/Nerv.IIP.sln --no-restore

Write-Host "IAM persistent auth foundation verified."
```

- [ ] **步骤 5：运行针对性验证**

运行：

```powershell
pwsh scripts/verify-iam-persistent-auth-foundation.ps1
```

预期结果：以 0 退出，最终输出为 `IAM persistent auth foundation verified.`

## 任务 8：更新文档

**文件：**

- 修改：`README.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/architecture/iam-authentication-baseline.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/database-schema-conventions.md`

- [ ] **步骤 1：更新 README 状态**

更新当前状态，在 schema 治理之后添加第七阶段：

```markdown
第七阶段 IAM Persistent Auth Foundation 已规划/落地：IAM 在保留 InMemory profile 的同时新增 PostgreSQL `iam` schema、EF migrations、schema convention tests、idempotent seed、JWT access token、refresh token rotation、session revoke 和 Connector Host credential validation 的持久化后端基线。
```

仅规划时使用`已规划`，只有实施验证通过后才改为`已落地`。

- [ ] **步骤 2：更新实施就绪状态**

实施完成后，在第六次迭代之后添加新章节 `### 第七迭代已完成范围`：

```markdown
### 第七迭代已完成范围

1. IAM 保留 InMemory profile，并新增 PostgreSQL profile，默认 schema 为 `iam`。
2. IAM 已有 `users`、`roles`、`role_permissions`、`memberships`、`user_sessions`、`connector_host_credentials` 和 seed manifest 等首批持久化表。
3. IAM 登录、refresh token rotation、logout/session revoke、`/me` 和 Connector Host credential validation 已可在 PostgreSQL profile 下运行。
4. IAM 初始 admin、platform admin role、seed permissions、membership 和 local Connector Host credential seed 具备幂等执行语义。
5. IAM schema convention tests 与 PostgreSQL profile tests 已作为后续 IAM 持久化变更门禁。
6. Gateway 全面鉴权、Console 登录 UI、OAuth/OIDC、SSO、MFA、ABAC 和客户发布 bundle 仍属于后续阶段。
```

- [ ] **步骤 3：更新 IAM 认证基线**

添加实施状态章节，区分已实施基础和未来工作：

```markdown
## 当前实现状态

IAM Persistent Auth Foundation 已覆盖后端持久化登录基线：PostgreSQL `iam` schema、初始 admin seed、JWT access token、refresh token hash + rotation、session revoke、`/me` 和 Connector Host credential validation。Gateway-wide permission enforcement、Console 登录 UI、OAuth/OIDC、SSO、MFA 和复杂 ABAC 不属于本阶段。
```

- [ ] **步骤 4：更新 schema 目录**

添加 `IAM Schema` 章节，为迁移实际创建的表添加行。包括已知缺口：

```markdown
Known gaps:

1. Gateway-wide permission enforcement is not wired yet.
2. User/role write management endpoints are not product-complete unless implemented in this phase.
3. Customer release seed input and migration bundle remain later release work.
```

- [ ] **步骤 5：更新 schema 约定**

测试通过后，更新 `Schema Convention Tests` 章节以包含 IAM：

```markdown
AppHub/Ops/IAM 已通过 `Nerv.IIP.Testing` 中的 schema convention helper 覆盖 business table comment、business column comment、string ID 约束和 service-schema `__EFMigrationsHistory`。
```

## 任务 9：最终验证并提交

**文件：**

- 任务 1–8 的所有实施文件。

- [ ] **步骤 1：运行 IAM 针对性测试**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

预期结果：以 0 退出。未设置 `NERV_IIP_TEST_POSTGRES` 时可以跳过 PostgreSQL 测试。

- [ ] **步骤 2：运行完整后端测试**

运行：

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
```

预期结果：以 0 退出。

- [ ] **步骤 3：如果认证 SDK 或 Connector 契约发生变化，则运行 connector-host 测试**

运行：

```powershell
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

预期结果：以 0 退出。

- [ ] **步骤 4：运行 IAM PostgreSQL 验证脚本**

运行：

```powershell
pwsh scripts/verify-iam-persistent-auth-foundation.ps1
```

预期结果：以 0 退出，最终输出为 `IAM persistent auth foundation verified.`

- [ ] **步骤 5：运行 diff 卫生检查**

运行：

```powershell
git diff --check
git status --short
```

预期结果：`git diff --check` 以 0 退出。`git status --short` 不得包含无关的已暂存变更。如果 `skills-lock.json` 仍为脏状态，保持未暂存，并在最终响应中说明。

- [ ] **步骤 6：提交实现**

仅暂存本计划更改的文件：

```powershell
git add README.md docs/architecture/implementation-readiness.md docs/architecture/iam-authentication-baseline.md docs/architecture/database-schema-catalog.md docs/architecture/database-schema-conventions.md docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md scripts/verify-iam-persistent-auth-foundation.ps1 backend/services/Iam
git commit -m "feat: add iam persistent auth foundation"
```

预期结果：提交成功。不得暂存 `skills-lock.json`。
