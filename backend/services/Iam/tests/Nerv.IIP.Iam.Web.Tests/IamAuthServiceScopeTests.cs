using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamAuthServiceScopeTests
{
    [Theory]
    [InlineData("self")]
    [InlineData("team")]
    [InlineData("work-center")]
    [InlineData("workshop")]
    [InlineData("organization")]
    public void Frontline_scope_kinds_are_governed_IAM_data_scope_types(string scopeKind)
    {
        var binding = DataScopeBinding.Normalize(new DataScopeBinding(scopeKind, "SCOPE-001"));

        Assert.Equal(scopeKind, binding.ScopeType);
        Assert.Equal("SCOPE-001", binding.ScopeCode);
    }

    [Fact]
    public async Task Principal_scope_grants_do_not_cross_permissions_between_roles()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-two-roles"),
            "two-roles",
            "two-roles@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var mesRole = new Role(
            new RoleId("role-mes"),
            "MES 操作工",
            ["business.mes.work-orders.read"]);
        mesRole.ReplaceDataScopes([new DataScopeBinding("workshop", "WS-MES")]);
        var qualityRole = new Role(
            new RoleId("role-quality"),
            "质检员",
            ["business.quality.inspection-records.read"]);
        qualityRole.ReplaceDataScopes([new DataScopeBinding("workshop", "WS-QA")]);
        var membership = new Membership(
            new MembershipId("membership-two-roles"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [mesRole.Id, qualityRole.Id]);

        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Nerv", "active"));
        db.Environments.Add(new IamEnvironment(
            new IamEnvironmentId("env-dev"),
            new OrganizationId("org-001"),
            "Dev",
            "active"));
        db.Roles.AddRange(mesRole, qualityRole);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            CreateTokenService(),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var principal = new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            "org-001",
            "env-dev",
            user.PermissionVersion,
            ["business.mes.work-orders.read", "business.quality.inspection-records.read"],
            ["role-mes", "role-quality"]);

        var result = await service.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.mes.work-orders.read",
            "mes-work-order",
            null,
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal(["WS-MES"], result.DataScope!.WorkshopCodes);
        var grant = Assert.Single(result.ScopeGrants!);
        Assert.Equal("role", grant.SourceKind);
        Assert.Equal("role-mes", grant.SourceId);
        Assert.Equal("workshop", grant.ScopeKind);
        Assert.Equal("WS-MES", grant.ScopeId);
        Assert.Equal(["business.mes.work-orders.read"], grant.ApplicablePermissionCodes);
    }

    [Fact]
    public async Task Permission_role_without_data_scope_does_not_invent_an_organization_grant()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-organization-role"),
            "organization-role",
            "organization-role@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var role = new Role(
            new RoleId("role-organization"),
            "全厂调度员",
            ["business.mes.work-orders.read"]);
        var membership = new Membership(
            new MembershipId("membership-organization-role"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [role.Id]);
        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Nerv", "active"));
        db.Environments.Add(new IamEnvironment(
            new IamEnvironmentId("env-dev"),
            new OrganizationId("org-001"),
            "Dev",
            "active"));
        db.Roles.Add(role);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            CreateTokenService(),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var principal = new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            "org-001",
            "env-dev",
            user.PermissionVersion,
            ["business.mes.work-orders.read"],
            ["role-organization"]);

        var result = await service.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.mes.work-orders.read",
            "mes-work-order",
            null,
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.NotNull(result.DataScope);
        Assert.False(result.DataScope!.HasRestrictions);
        Assert.Empty(result.ScopeGrants!);
    }

    [Theory]
    [InlineData("org-001", true)]
    [InlineData("org-other", false)]
    public async Task Explicit_organization_scope_is_only_wide_for_the_current_organization(
        string scopedOrganizationId,
        bool expectedAllowedScope)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId($"user-explicit-{scopedOrganizationId}"),
            $"explicit-{scopedOrganizationId}",
            $"explicit-{scopedOrganizationId}@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var role = new Role(
            new RoleId($"role-explicit-{scopedOrganizationId}"),
            "显式组织范围角色",
            ["business.mes.work-orders.read"]);
        role.ReplaceDataScopes([new DataScopeBinding("organization", scopedOrganizationId)]);
        var membership = new Membership(
            new MembershipId($"membership-explicit-{scopedOrganizationId}"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [role.Id]);
        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Nerv", "active"));
        db.Environments.Add(new IamEnvironment(
            new IamEnvironmentId("env-dev"),
            new OrganizationId("org-001"),
            "Dev",
            "active"));
        db.Roles.Add(role);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            CreateTokenService(),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var principal = new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            "org-001",
            "env-dev",
            user.PermissionVersion,
            ["business.mes.work-orders.read"],
            [role.Id.Id]);

        var result = await service.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.mes.work-orders.read",
            "mes-work-order",
            null,
            CancellationToken.None);

        Assert.True(result.Allowed);
        var grant = Assert.Single(result.ScopeGrants!);
        Assert.Equal("organization", grant.ScopeKind);
        Assert.Equal(scopedOrganizationId, grant.ScopeId);
        Assert.Equal(expectedAllowedScope, grant.OrganizationWide);
        if (expectedAllowedScope)
        {
            Assert.Null(result.DataScope);
        }
        else
        {
            Assert.True(result.DataScope!.DenyAll);
        }
    }

    [Fact]
    public async Task Organization_grant_cannot_hide_unknown_legacy_scope_for_the_same_permission()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-org-and-legacy"),
            "org-and-legacy",
            "org-and-legacy@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var organizationRole = new Role(
            new RoleId("role-org-wide"),
            "全组织角色",
            ["business.mes.work-orders.read"]);
        organizationRole.ReplaceDataScopes([new DataScopeBinding("organization", "org-001")]);
        var legacyRole = new Role(
            new RoleId("role-legacy-cell"),
            "存量单元角色",
            ["business.mes.work-orders.read"]);
        var membership = new Membership(
            new MembershipId("membership-org-and-legacy"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [organizationRole.Id, legacyRole.Id]);
        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Nerv", "active"));
        db.Environments.Add(new IamEnvironment(
            new IamEnvironmentId("env-dev"),
            new OrganizationId("org-001"),
            "Dev",
            "active"));
        db.Roles.AddRange(organizationRole, legacyRole);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO role_data_scopes (Id, RoleId, ScopeType, ScopeCode) VALUES ('role-legacy-cell:cell:CELL-A', 'role-legacy-cell', 'cell', 'CELL-A')");

        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            CreateTokenService(),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var principal = new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            "org-001",
            "env-dev",
            user.PermissionVersion,
            ["business.mes.work-orders.read"],
            ["role-org-wide", "role-legacy-cell"]);

        var result = await service.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.mes.work-orders.read",
            "mes-work-order",
            null,
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.NotNull(result.DataScope);
        Assert.True(result.DataScope!.DenyAll);
    }

    [Fact]
    public async Task Membership_scope_restricts_permission_when_granting_role_has_no_role_scope()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-membership-restricted"),
            "membership-restricted",
            "membership-restricted@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var role = new Role(
            new RoleId("role-no-scope"),
            "无角色范围",
            ["business.mes.work-orders.read"]);
        var membership = new Membership(
            new MembershipId("membership-workshop"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [role.Id]);
        membership.ReplaceDataScopes([new DataScopeBinding("workshop", "WS-MC")]);
        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Nerv", "active"));
        db.Environments.Add(new IamEnvironment(
            new IamEnvironmentId("env-dev"),
            new OrganizationId("org-001"),
            "Dev",
            "active"));
        db.Roles.Add(role);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            CreateTokenService(),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var principal = new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            "org-001",
            "env-dev",
            user.PermissionVersion,
            ["business.mes.work-orders.read"],
            ["role-no-scope"]);

        var result = await service.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.mes.work-orders.read",
            "mes-work-order",
            null,
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal(["WS-MC"], result.DataScope!.WorkshopCodes);
        Assert.DoesNotContain(result.ScopeGrants!, x => x.OrganizationWide);
        var grant = Assert.Single(result.ScopeGrants!);
        Assert.Equal("membership", grant.SourceKind);
        Assert.Equal("WS-MC", grant.ScopeId);
    }

    [Fact]
    public async Task PrincipalHasPermissionAsync_returns_effective_data_scope_and_records_audit()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-data-scope"),
            "scope-user",
            "scope-user@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var role = new Role(new RoleId("role-mes-workshop"), "MES Workshop", ["business.mes.work-orders.read"]);
        role.ReplaceDataScopes([new DataScopeBinding("workshop", "WS-A")]);
        var membership = new Membership(
            new MembershipId("membership-data-scope"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [role.Id]);
        membership.ReplaceDataScopes([new DataScopeBinding("production-line", "LINE-A")]);

        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Nerv", "active"));
        db.Environments.Add(new IamEnvironment(new IamEnvironmentId("env-dev"), new OrganizationId("org-001"), "Dev", "active"));
        db.Roles.Add(role);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var audit = new RecordingSecurityAuditRecorder();
        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            CreateTokenService(),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            audit,
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var principal = new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            "org-001",
            "env-dev",
            user.PermissionVersion,
            ["business.mes.work-orders.read"],
            []);

        var result = await service.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.mes.work-orders.read",
            "mes-work-order",
            null,
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.NotNull(result.DataScope);
        Assert.Equal(["WS-A"], result.DataScope!.WorkshopCodes);
        Assert.Equal(["LINE-A"], result.DataScope.ProductionLineCodes);
        Assert.Contains(audit.Records, record => record.Action == "iam.authorization.data-scope.matched");
    }

    [Fact]
    public async Task PrincipalHasPermissionAsync_returns_deny_all_scope_for_unknown_legacy_scope_type()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-legacy-data-scope"),
            "legacy-scope-user",
            "legacy-scope-user@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var role = new Role(new RoleId("role-legacy-scope"), "Legacy Scope", ["business.mes.work-orders.read"]);
        var membership = new Membership(
            new MembershipId("membership-legacy-scope"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [role.Id]);

        db.Users.Add(user);
        db.Organizations.Add(new Organization(new OrganizationId("org-001"), "Nerv", "active"));
        db.Environments.Add(new IamEnvironment(new IamEnvironmentId("env-dev"), new OrganizationId("org-001"), "Dev", "active"));
        db.Roles.Add(role);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO role_data_scopes (Id, RoleId, ScopeType, ScopeCode) VALUES ('role-legacy-scope:cell:CELL-A', 'role-legacy-scope', 'cell', 'CELL-A')");

        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            CreateTokenService(),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
        var principal = new CurrentPrincipalResponse(
            user.Id.Id,
            user.LoginName,
            user.Email,
            "user",
            "org-001",
            "env-dev",
            user.PermissionVersion,
            ["business.mes.work-orders.read"],
            []);

        var result = await service.PrincipalHasPermissionAsync(
            principal,
            "org-001",
            "env-dev",
            "business.mes.work-orders.read",
            "mes-work-order",
            null,
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.NotNull(result.DataScope);
        Assert.True(result.DataScope!.DenyAll);
        Assert.True(result.DataScope.HasRestrictions);
    }

    [Fact]
    public async Task GetCurrentPrincipalAsync_uses_access_token_membership_scope()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-multi-scope"),
            "multi",
            "multi@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var session = new UserSession(
            new UserSessionId("session-multi-scope"),
            user.Id,
            "refresh-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(14),
            user.PermissionVersion,
            null,
            null);

        db.Users.Add(user);
        db.UserSessions.Add(session);
        db.Organizations.AddRange(
            new Organization(new OrganizationId("org-aaa"), "A", "active"),
            new Organization(new OrganizationId("org-zzz"), "Z", "active"));
        db.Environments.AddRange(
            new IamEnvironment(new IamEnvironmentId("env-dev"), new OrganizationId("org-aaa"), "Dev", "active"),
            new IamEnvironment(new IamEnvironmentId("env-prod"), new OrganizationId("org-zzz"), "Prod", "active"));
        db.Roles.AddRange(
            new Role(new RoleId("role-empty"), "Empty", []),
            new Role(new RoleId("role-ops"), "Ops", ["ops.tasks.create"]));
        db.Memberships.AddRange(
            new Membership(
                new MembershipId("membership-aaa"),
                user.Id,
                new OrganizationId("org-aaa"),
                new IamEnvironmentId("env-dev"),
                [new RoleId("role-empty")]),
            new Membership(
                new MembershipId("membership-zzz"),
                user.Id,
                new OrganizationId("org-zzz"),
                new IamEnvironmentId("env-prod"),
                [new RoleId("role-ops")]));
        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var service = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new ScopedMembershipRepository(user.Id),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            tokenService,
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());

        var principalAaa = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session, "org-aaa", "env-dev")),
            CancellationToken.None);
        var principalZzz = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session, "org-zzz", "env-prod")),
            CancellationToken.None);

        Assert.NotNull(principalAaa);
        Assert.Equal("org-aaa", principalAaa.OrganizationId);
        Assert.Equal("env-dev", principalAaa.EnvironmentId);
        Assert.Empty(principalAaa.PermissionCodes);
        Assert.Equal(["role-empty"], principalAaa.RoleIds);

        Assert.NotNull(principalZzz);
        Assert.Equal("org-zzz", principalZzz.OrganizationId);
        Assert.Equal("env-prod", principalZzz.EnvironmentId);
        Assert.Equal(["ops.tasks.create"], principalZzz.PermissionCodes);
        Assert.Equal(["role-ops"], principalZzz.RoleIds);
    }

    [Fact]
    public async Task GetCurrentPrincipalAsync_rejects_token_scope_without_membership()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-invalid-scope"),
            "invalid-scope",
            "invalid-scope@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var session = new UserSession(
            new UserSessionId("session-invalid-scope"),
            user.Id,
            "refresh-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(14),
            user.PermissionVersion,
            null,
            null);

        db.Users.Add(user);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var service = CreateAuthService(db, passwordService, tokenService, user.Id);
        var principal = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session, "org-forged", "env-dev")),
            CancellationToken.None);

        Assert.Null(principal);
    }

    [Fact]
    public async Task GetCurrentPrincipalAsync_without_token_scope_uses_legacy_first_membership()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        await db.Database.EnsureCreatedAsync();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-legacy-scope"),
            "legacy-scope",
            "legacy-scope@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        var session = new UserSession(
            new UserSessionId("session-legacy-scope"),
            user.Id,
            "refresh-hash",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(14),
            user.PermissionVersion,
            null,
            null);

        db.Users.Add(user);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var service = CreateAuthService(db, passwordService, tokenService, user.Id);
        var principal = await service.GetCurrentPrincipalAsync(
            CreateHttpContext(tokenService.CreateAccessToken(user, session)),
            CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal("org-aaa", principal.OrganizationId);
        Assert.Equal("env-dev", principal.EnvironmentId);
    }

    [Fact]
    public async Task PostgreSql_auth_service_rejects_enterprise_identity_stubs_outside_development()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDbContext(connection);
        var mfaChallenges = new InMemoryMfaChallengeStore();
        var service = CreateAuthService(
            db,
            new IamPasswordService(),
            CreateTokenService(),
            new UserId("user-production-stub"),
            new TestWebHostEnvironment { EnvironmentName = "Production" },
            mfaChallenges);

        var oidcRequest = new OidcLoginCallbackRequest(
            "prod-demo",
            "entra-user-admin",
            "admin@nerv-iip.local",
            "org-001",
            "env-dev",
            "oidc-callback-secret");
        var challengeId = mfaChallenges.Create(new MfaChallengeContext(
            "user-production-stub",
            "prod-demo",
            "entra-user-admin",
            "org-001",
            "env-dev",
            DateTimeOffset.UtcNow.AddMinutes(5)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.HandleOidcCallbackAsync(oidcRequest, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.VerifyMfaChallengeAsync(challengeId, "654321", null, null, CancellationToken.None));
    }

    private static ApplicationDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static IamTokenService CreateTokenService()
    {
        var configuration = new ConfigurationBuilder().Build();
        return new IamTokenService(configuration, new TestWebHostEnvironment());
    }

    private static PostgreSqlIamAuthService CreateAuthService(
        ApplicationDbContext db,
        IamPasswordService passwordService,
        IamTokenService tokenService,
        UserId userId,
        TestWebHostEnvironment? environment = null,
        IMfaChallengeStore? mfaChallenges = null)
    {
        return new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new ScopedMembershipRepository(userId),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            tokenService,
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            mfaChallenges ?? new InMemoryMfaChallengeStore(),
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            environment ?? new TestWebHostEnvironment());
    }

    private static HttpContext CreateHttpContext(string accessToken)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {accessToken}";
        return httpContext;
    }

    private sealed class ScopedMembershipRepository(UserId userId) : IMembershipRepository
    {
        private readonly Membership firstMembership = new(
            new MembershipId("membership-first"),
            userId,
            new OrganizationId("org-aaa"),
            new IamEnvironmentId("env-dev"),
            [new RoleId("role-empty")]);
        private readonly Membership scopedMembership = new(
            new MembershipId("membership-scoped"),
            userId,
            new OrganizationId("org-zzz"),
            new IamEnvironmentId("env-prod"),
            [new RoleId("role-ops")]);

        public Task<Membership?> GetFirstByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = cancellationToken;
            return Task.FromResult<Membership?>(firstMembership);
        }

        public Task<Membership?> GetByUserIdAndOrgEnvAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = cancellationToken;
            var membership = organizationId.Id switch
            {
                "org-aaa" when environmentId.Id == "env-dev" => firstMembership,
                "org-zzz" when environmentId.Id == "env-prod" => scopedMembership,
                _ => null
            };
            return Task.FromResult<Membership?>(membership);
        }

        public Task<bool> UserHasPermissionAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = environmentId;
            _ = cancellationToken;
            return Task.FromResult(organizationId.Id == "org-zzz" && permissionCode == "ops.tasks.create");
        }

        public Task<IReadOnlyList<string>> ListPermissionCodesAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = environmentId;
            _ = cancellationToken;
            IReadOnlyList<string> permissions = organizationId.Id == "org-zzz"
                ? ["ops.tasks.create"]
                : [];
            return Task.FromResult(permissions);
        }

        public Task<IReadOnlyList<DataScopeBinding>> ListEffectiveDataScopesAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = organizationId;
            _ = environmentId;
            _ = cancellationToken;
            IReadOnlyList<DataScopeBinding> scopes = [];
            return Task.FromResult(scopes);
        }

        public Task<IReadOnlyList<PermissionDataScopeGrant>> ListPermissionDataScopeGrantsAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = environmentId;
            _ = cancellationToken;
            IReadOnlyList<PermissionDataScopeGrant> grants =
            [
                new(
                    "role",
                    organizationId.Id == "org-zzz" ? "role-ops" : "role-empty",
                    "organization",
                    organizationId.Id,
                    [permissionCode],
                    OrganizationWide: true),
            ];
            return Task.FromResult(grants);
        }

        public Task<bool> UserHasMembershipAsync(
            UserId userId,
            OrganizationId organizationId,
            IamEnvironmentId environmentId,
            CancellationToken cancellationToken = default)
        {
            _ = userId;
            _ = cancellationToken;
            return Task.FromResult(
                organizationId.Id == "org-aaa" && environmentId.Id == "env-dev"
                || organizationId.Id == "org-zzz" && environmentId.Id == "env-prod");
        }

        public IUnitOfWork UnitOfWork => throw new NotSupportedException();
        public Membership Add(Membership entity) => throw new NotSupportedException();
        public Task<Membership> AddAsync(Membership entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddRange(IEnumerable<Membership> entities) => throw new NotSupportedException();
        public Task AddRangeAsync(IEnumerable<Membership> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Attach(Membership entity) => throw new NotSupportedException();
        public void AttachRange(IEnumerable<Membership> entities) => throw new NotSupportedException();
        public bool Delete(Entity entity) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Entity entity) => throw new NotSupportedException();
        public int DeleteById(MembershipId id) => throw new NotSupportedException();
        public Task<int> DeleteByIdAsync(MembershipId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Membership Get(MembershipId id) => throw new NotSupportedException();
        public Task<Membership?> GetAsync(MembershipId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool Remove(Entity entity) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Entity entity) => throw new NotSupportedException();
        public Membership Update(Membership entity) => throw new NotSupportedException();
        public Task<Membership> UpdateAsync(Membership entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingSecurityAuditRecorder : ISecurityAuditRecorder
    {
        public List<AuditRecord> Records { get; } = [];

        public Task RecordAsync(
            SecurityAuditContext context,
            string action,
            string targetType,
            string targetId,
            string outcome,
            object details,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            _ = context;
            _ = details;
            _ = occurredAtUtc;
            _ = cancellationToken;
            Records.Add(new AuditRecord(action, targetType, targetId, outcome));
            return Task.CompletedTask;
        }

        public Task RecordAndSaveAsync(
            SecurityAuditContext context,
            string action,
            string targetType,
            string targetId,
            string outcome,
            object details,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken) =>
            RecordAsync(context, action, targetType, targetId, outcome, details, occurredAtUtc, cancellationToken);
    }

    private sealed record AuditRecord(string Action, string TargetType, string TargetId, string Outcome);
}
