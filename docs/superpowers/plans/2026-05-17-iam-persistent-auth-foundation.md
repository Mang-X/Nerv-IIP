# IAM Persistent Auth Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the existing IAM in-memory skeleton into a persistent backend authentication foundation with PostgreSQL migrations, seed behavior, JWT access tokens, refresh-token rotation, schema convention tests and documentation.

**Architecture:** Keep IAM as a CleanDDD-style three-project service. Preserve the current InMemory profile for earlier verification scripts, and add a PostgreSQL profile with `iam` schema ownership, service-schema EF migrations history, entity configurations, migration runner and focused Web/Application services for login, refresh, revoke, `/me` and Connector Host credential validation.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core 10.0.8, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1, netcorepal repository/unit-of-work primitives, ASP.NET Core `PasswordHasher<T>`, JWT Bearer primitives, xUnit, PowerShell.

---

## Completion Record

This plan starts from commit `c707269 docs: design iam persistent auth foundation` on branch `codex/iam-persistent-auth-foundation`.

Known handoff note: `skills-lock.json` is dirty before this plan begins, with no text diff reported in prior audits. Do not stage or modify it unless the user explicitly asks.

Post-merge audit note: implementation landed through `8c6bcde Merge pull request #12 from Mang-X/codex/iam-persistent-auth-foundation`. The original checkbox tracking below was not updated during the branch, so the boxes are stale historical tracking rather than an accurate status signal. A follow-up audit tightened PostgreSQL IAM management endpoints so user/role/session management routes reject anonymous callers before touching persistence; user/role write management remains intentionally unproductized and returns 501 only after permission checks pass.

## Boundaries

1. Do not implement Gateway-wide bearer authorization or permission policies.
2. Do not add console login UI, routes, navigation, styles, design tokens or component library changes.
3. Do not implement OAuth/OIDC, SSO, MFA, WebAuthn, ABAC, delegation or third-party consent flows.
4. Do not create customer-release migration bundles, installers, backup scripts or restore rehearsals.
5. Do not validate GaussDB, DMDB or other provider profiles.
6. Keep the current InMemory IAM profile available unless a targeted test proves a compatibility issue.
7. Do not stage unrelated `skills-lock.json` changes.

## File Structure Map

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

## Task 1: Add Persistent Auth Failing Tests

**Files:**

- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj`
- Create: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`

- [ ] **Step 1: Add required test project references**

Modify the test project references so PostgreSQL tests can inspect IAM infrastructure and schema conventions:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Nerv.IIP.Iam.Web\Nerv.IIP.Iam.Web.csproj" />
    <ProjectReference Include="..\..\..\..\common\Testing\Nerv.IIP.Testing\Nerv.IIP.Testing.csproj" />
  </ItemGroup>
```

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

Expected: existing tests still compile. If this fails before new tests are added, stop and inspect the project reference path.

- [ ] **Step 2: Add a failing PostgreSQL login/refresh/revoke test**

Create `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`:

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

- [ ] **Step 3: Run the new test and verify the expected red state**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter FullyQualifiedName~IamPostgresProfileTests
```

Expected: compile failure because `ApplicationDbContext`, `IamDatabaseMigrationRunner`, `IamSeedService`, and PostgreSQL IAM DbSets do not exist yet.

## Task 2: Add IAM Schema Convention Failing Test

**Files:**

- Create: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamSchemaConventionTests.cs`

- [ ] **Step 1: Write the schema convention test**

Create `IamSchemaConventionTests.cs`:

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

- [ ] **Step 2: Run schema test and verify the expected red state**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter FullyQualifiedName~IamSchemaConventionTests
```

Expected: compile failure because IAM aggregate types and `AddIamPersistence` do not exist yet.

## Task 3: Add IAM Domain Model

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/Nerv.IIP.Iam.Domain.csproj`
- Modify or keep: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/IamFacts.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/OrganizationAggregate/Organization.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/UserAggregate/User.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/RoleAggregate/Role.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/MembershipAggregate/Membership.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/UserSessionAggregate/UserSession.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/ConnectorHostCredentialAggregate/ConnectorHostCredential.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/SeedAggregate/SeedManifest.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/DomainEvents/IamDomainEvents.cs`

- [ ] **Step 1: Add netcorepal domain reference**

Modify the IAM Domain project:

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

- [ ] **Step 2: Add Organization and Environment aggregate file**

Create `OrganizationAggregate/Organization.cs`:

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

- [ ] **Step 3: Add User aggregate file**

Create `UserAggregate/User.cs`:

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

- [ ] **Step 4: Add Role and Membership aggregate files**

Create `RoleAggregate/Role.cs`:

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

Create `MembershipAggregate/Membership.cs`:

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

- [ ] **Step 5: Add UserSession and Connector credential aggregate files**

Create `UserSessionAggregate/UserSession.cs`:

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

Create `ConnectorHostCredentialAggregate/ConnectorHostCredential.cs`:

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

- [ ] **Step 6: Add SeedManifest and domain events**

Create `SeedAggregate/SeedManifest.cs`:

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

Create `DomainEvents/IamDomainEvents.cs`:

```csharp
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.DomainEvents;

public sealed record UserLoggedInDomainEvent(string UserId, DateTimeOffset LoggedInAtUtc) : IDomainEvent;
public sealed record UserSessionCreatedDomainEvent(string SessionId, string UserId, DateTimeOffset IssuedAtUtc) : IDomainEvent;
public sealed record UserSessionRevokedDomainEvent(string SessionId, string UserId, DateTimeOffset RevokedAtUtc, string Reason) : IDomainEvent;
```

- [ ] **Step 7: Run compile and inspect errors**

Run:

```powershell
dotnet build backend/services/Iam/src/Nerv.IIP.Iam.Domain/Nerv.IIP.Iam.Domain.csproj
```

Expected: domain project builds. If strongly typed ID source generation requires additional package references, compare AppHub/Ops project files and add only the missing netcorepal package reference.

## Task 4: Add IAM Persistence Profile

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Nerv.IIP.Iam.Infrastructure.csproj`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/IamPersistenceServiceCollectionExtensions.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/IamDatabaseMigrationRunner.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs`

- [ ] **Step 1: Add infrastructure package references**

Modify IAM Infrastructure project:

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

- [ ] **Step 2: Add DbContext**

Create `ApplicationDbContext.cs` with DbSets for every IAM persistent entity. Follow AppHub/Ops default schema style:

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

- [ ] **Step 3: Add persistence extension and migration runner**

Create `IamPersistenceServiceCollectionExtensions.cs`:

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

Create `IamDatabaseMigrationRunner.cs`:

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

- [ ] **Step 4: Add entity configurations with comments and indexes**

Create one configuration file per aggregate area. Use these exact table names and required conventions:

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

Repeat the same explicit-ID/comment pattern for:

1. `organizations`: `Id`, `Name`, `Status`, `Deleted`, `RowVersion`.
2. `environments`: `Id`, `OrganizationId`, `Name`, `Status`, `Deleted`, `RowVersion`, unique index on `{ OrganizationId, Id }`.
3. `roles`: `Id`, `RoleName`, `Deleted`, `RowVersion`, unique role name.
4. `role_permissions`: `Id`, `RoleId`, `PermissionCode`, unique index on `{ RoleId, PermissionCode }`.
5. `memberships`: `Id`, `UserId`, `OrganizationId`, `EnvironmentId`, unique index on `{ UserId, OrganizationId, EnvironmentId }`.
6. `membership_roles`: `Id`, `MembershipId`, `RoleId`, unique index on `{ MembershipId, RoleId }`.
7. `user_sessions`: `Id`, `UserId`, `RefreshTokenHash`, `IssuedAtUtc`, `ExpiresAtUtc`, `RevokedAtUtc`, `RevokedReason`, `PermissionVersion`, `ClientInfo`, `IpAddress`, indexes on `RefreshTokenHash` and `{ UserId, RevokedAtUtc }`.
8. `connector_host_credentials`: `Id`, `ConnectorHostId`, `OrganizationId`, `EnvironmentId`, `SecretHash`, `ValidFromUtc`, `ValidToUtc`, unique connector host id.
9. `connector_host_credential_capabilities`: `Id`, `ConnectorHostCredentialId`, `CapabilityCode`.
10. `seed_manifests`: `Id`, `SeedName`, `SeedVersion`, `OwnerService`, `AppliedAtUtc`, unique index on `{ SeedName, SeedVersion }`.

- [ ] **Step 5: Run schema convention test and verify meaningful failures**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter FullyQualifiedName~IamSchemaConventionTests
```

Expected: the test compiles. It may fail with specific missing comments, missing max lengths or missing migrations history schema. Fix entity configuration until the test passes without requiring a live database.

## Task 5: Add IAM Auth, Token and Seed Services

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthModels.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamPasswordService.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamTokenService.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedOptions.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Program.cs`

- [ ] **Step 1: Add Web package references**

Modify IAM Web project to include JWT and password hashing packages available through the shared framework. If an explicit package reference is required by the compiler, add:

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" VersionOverride="10.0.0" />
```

If Central Package Management rejects `VersionOverride`, add `Microsoft.AspNetCore.Authentication.JwtBearer` to `backend/Directory.Packages.props` with version `10.0.0` and use a normal package reference. Keep this change scoped to the package reference needed for JWT Bearer.

- [ ] **Step 2: Add auth DTOs**

Create `Application/Auth/IamAuthModels.cs`:

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

- [ ] **Step 3: Add password and token services**

Create `IamPasswordService.cs`:

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

Create `IamTokenService.cs` with HMAC signing from configuration key `Iam:Jwt:SigningKey`. The implementation must create JWTs with `sub`, `sessionId`, `principalType`, `securityStamp`, `permissionVersion`, `iat` and `jti` claims. It must expose:

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

When implementing `CreateAccessToken` and `TryReadPrincipal`, use `System.IdentityModel.Tokens.Jwt`, `Microsoft.IdentityModel.Tokens`, `ClaimsIdentity`, `JwtSecurityTokenHandler`, and `SymmetricSecurityKey`. Use `ValidateLifetime=true` and a default 15-minute lifetime if `Iam:Jwt:AccessTokenMinutes` is not configured.

- [ ] **Step 4: Add seed options and seed service**

Create `Application/Seed/IamSeedOptions.cs`:

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

Create `IamSeedService.cs`. It must:

1. read `IOptions<IamSeedOptions>`;
2. no-op when `Enabled` is false;
3. require non-empty `AdminPassword` and `ConnectorHostSecret` when enabled;
4. upsert organization, environment, admin role, admin user, membership, connector credential and seed manifest by stable IDs/business keys;
5. use `NervIipSeedPermissions.All` for role permissions and connector `connectors.*` capability scope;
6. save changes through `ApplicationDbContext`.

- [ ] **Step 5: Add auth service**

Create `IamAuthService.cs` with these public methods:

```csharp
public Task<AuthResponse> LoginAsync(string loginName, string password, string? clientInfo, string? ipAddress, CancellationToken cancellationToken);
public Task<AuthResponse> RefreshAsync(string refreshToken, string? clientInfo, string? ipAddress, CancellationToken cancellationToken);
public Task RevokeSessionAsync(string sessionId, string reason, CancellationToken cancellationToken);
public Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken);
public Task<ConnectorPrincipalResponse> ValidateConnectorCredentialAsync(string connectorHostId, string secret, CancellationToken cancellationToken);
```

Implementation requirements:

1. `LoginAsync` finds enabled user by login name, verifies password, records success/failure, creates `UserSession`, and returns token pair.
2. `RefreshAsync` hashes the submitted refresh token, finds an active session, verifies the user is enabled, revokes the old session with reason `refresh-rotated`, creates a new session and returns token pair.
3. `RevokeSessionAsync` is idempotent.
4. `GetCurrentPrincipalAsync` validates JWT and then verifies persisted session, user enabled state, security stamp and permission version.
5. `ValidateConnectorCredentialAsync` hashes the submitted secret and checks credential validity window.
6. All unauthorized paths throw `UnauthorizedAccessException` with generic messages.

- [ ] **Step 6: Wire services in Program**

Modify `Program.cs`:

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

## Task 6: Update IAM Endpoints

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Auth/AuthEndpoints.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Users/UserEndpoints.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Roles/RoleEndpoints.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Sessions/SessionEndpoints.cs`

- [ ] **Step 1: Replace Auth endpoints with auth service**

Modify Auth endpoints so PostgreSQL mode uses `IamAuthService`. Keep support for InMemory mode by either:

1. wrapping `InMemoryIamStore` behind `IamAuthService` when no `ApplicationDbContext` exists; or
2. registering a common `IIamAuthService` interface with separate in-memory and PostgreSQL implementations.

Endpoint behavior must match:

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

`WriteAuthResultAsync` should support `Func<Task<T>>`, catch `UnauthorizedAccessException`, set status `401`, and write `{ title, detail, status }`.

- [ ] **Step 2: Make `/me` persisted-session aware**

`GET /api/iam/v1/me` must call `IamAuthService.GetCurrentPrincipalAsync(HttpContext, ct)`. If it returns null, call `Send.UnauthorizedAsync(ct)`. Otherwise return `CurrentPrincipalResponse`.

- [ ] **Step 3: Update users/roles/sessions read endpoints**

For PostgreSQL mode, read from `ApplicationDbContext` with `AsNoTracking()` and return minimal DTOs:

```csharp
await Send.OkAsync(await db.Users
    .AsNoTracking()
    .OrderBy(x => x.LoginName)
    .Select(x => new { x.UserId, x.LoginName, x.Email, x.Enabled })
    .ToListAsync(ct), ct);
```

Adjust property names to the actual strongly typed ID property (`x.Id.Id`) when implementing. Keep InMemory fallback for early scripts by resolving optional services carefully:

```csharp
var db = Resolve<ApplicationDbContext?>();
if (db is null)
{
    var store = Resolve<InMemoryIamStore>();
    await HttpContext.Response.WriteAsJsonAsync(store.Users.Select(x => new { x.UserId, x.LoginName, x.Email, x.Enabled }), ct);
    return;
}
```

- [ ] **Step 4: Keep placeholder write endpoints honest**

For `POST /users`, `PATCH /users/{userId}`, `POST /users/{userId}/disable`, `POST /roles`, and `PATCH /roles/{roleId}/permissions`, either implement real persisted commands or return `501 Not Implemented` with a problem response. Do not return fake placeholder IDs in PostgreSQL mode.

- [ ] **Step 5: Run IAM web tests**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

Expected: existing in-memory test passes, schema convention test passes, PostgreSQL test is skipped when `NERV_IIP_TEST_POSTGRES` is unset.

## Task 7: Generate IAM Migration and Verification Script

**Files:**

- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Migrations/*`
- Create: `scripts/verify-iam-persistent-auth-foundation.ps1`

- [ ] **Step 1: Restore EF tool**

Run:

```powershell
dotnet tool restore
```

Expected: exit 0.

- [ ] **Step 2: Generate IAM initial migration**

Run:

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

Expected: migration files are created under IAM Infrastructure `Migrations/`.

- [ ] **Step 3: Inspect migration**

Open the generated migration and verify:

1. schema `iam` is created;
2. all business tables have comments;
3. all business columns have comments;
4. string IDs have bounded lengths;
5. no password, refresh token or connector secret appears as clear text;
6. no data seed with secrets is embedded in the migration.

- [ ] **Step 4: Add verification script**

Create `scripts/verify-iam-persistent-auth-foundation.ps1`:

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

- [ ] **Step 5: Run targeted verification**

Run:

```powershell
pwsh scripts/verify-iam-persistent-auth-foundation.ps1
```

Expected: exit 0 and final output `IAM persistent auth foundation verified.`

## Task 8: Update Documentation

**Files:**

- Modify: `README.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/iam-authentication-baseline.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/database-schema-conventions.md`

- [ ] **Step 1: Update README status**

Update the current status to add a seventh stage after schema governance:

```markdown
第七阶段 IAM Persistent Auth Foundation 已规划/落地：IAM 在保留 InMemory profile 的同时新增 PostgreSQL `iam` schema、EF migrations、schema convention tests、idempotent seed、JWT access token、refresh token rotation、session revoke 和 Connector Host credential validation 的持久化后端基线。
```

Use `已规划` while only planning, and change to `已落地` only after implementation verification passes.

- [ ] **Step 2: Update implementation readiness**

Add a new section `### 第七迭代已完成范围` after the sixth iteration once implemented:

```markdown
### 第七迭代已完成范围

1. IAM 保留 InMemory profile，并新增 PostgreSQL profile，默认 schema 为 `iam`。
2. IAM 已有 `users`、`roles`、`role_permissions`、`memberships`、`user_sessions`、`connector_host_credentials` 和 seed manifest 等首批持久化表。
3. IAM 登录、refresh token rotation、logout/session revoke、`/me` 和 Connector Host credential validation 已可在 PostgreSQL profile 下运行。
4. IAM 初始 admin、platform admin role、seed permissions、membership 和 local Connector Host credential seed 具备幂等执行语义。
5. IAM schema convention tests 与 PostgreSQL profile tests 已作为后续 IAM 持久化变更门禁。
6. Gateway 全面鉴权、Console 登录 UI、OAuth/OIDC、SSO、MFA、ABAC 和客户发布 bundle 仍属于后续阶段。
```

- [ ] **Step 3: Update IAM authentication baseline**

Add an implementation status section that distinguishes implemented foundation from future work:

```markdown
## 当前实现状态

IAM Persistent Auth Foundation 已覆盖后端持久化登录基线：PostgreSQL `iam` schema、初始 admin seed、JWT access token、refresh token hash + rotation、session revoke、`/me` 和 Connector Host credential validation。Gateway-wide permission enforcement、Console 登录 UI、OAuth/OIDC、SSO、MFA 和复杂 ABAC 不属于本阶段。
```

- [ ] **Step 4: Update schema catalog**

Add an `IAM Schema` section with rows for actual tables created by the migration. Include known gaps:

```markdown
Known gaps:

1. Gateway-wide permission enforcement is not wired yet.
2. User/role write management endpoints are not product-complete unless implemented in this phase.
3. Customer release seed input and migration bundle remain later release work.
```

- [ ] **Step 5: Update schema conventions**

Update the `Schema Convention Tests` section to include IAM after tests pass:

```markdown
AppHub/Ops/IAM 已通过 `Nerv.IIP.Testing` 中的 schema convention helper 覆盖 business table comment、business column comment、string ID 约束和 service-schema `__EFMigrationsHistory`。
```

## Task 9: Final Verification and Commit

**Files:**

- All implementation files from Tasks 1-8.

- [ ] **Step 1: Run IAM targeted tests**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

Expected: exit 0. PostgreSQL test may skip when `NERV_IIP_TEST_POSTGRES` is not set.

- [ ] **Step 2: Run full backend tests**

Run:

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
```

Expected: exit 0.

- [ ] **Step 3: Run connector-host tests if auth SDK or connector contracts changed**

Run:

```powershell
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

Expected: exit 0.

- [ ] **Step 4: Run IAM PostgreSQL verification script**

Run:

```powershell
pwsh scripts/verify-iam-persistent-auth-foundation.ps1
```

Expected: exit 0 and final output `IAM persistent auth foundation verified.`

- [ ] **Step 5: Run diff hygiene**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` exit 0. `git status --short` must not include unrelated staged changes. If `skills-lock.json` remains dirty, leave it unstaged and mention it in the final response.

- [ ] **Step 6: Commit implementation**

Stage only files changed for this plan:

```powershell
git add README.md docs/architecture/implementation-readiness.md docs/architecture/iam-authentication-baseline.md docs/architecture/database-schema-catalog.md docs/architecture/database-schema-conventions.md docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md scripts/verify-iam-persistent-auth-foundation.ps1 backend/services/Iam
git commit -m "feat: add iam persistent auth foundation"
```

Expected: commit succeeds. Do not stage `skills-lock.json`.
