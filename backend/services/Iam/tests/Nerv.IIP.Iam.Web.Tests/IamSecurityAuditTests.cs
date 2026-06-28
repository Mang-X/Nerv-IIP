using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Roles;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamSecurityAuditTests
{
    [Fact]
    public async Task Failed_password_login_persists_security_audit_and_failed_attempt_count()
    {
        await using var db = CreateDbContext();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-audit-login"),
            "audit-login",
            "audit-login@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var auth = CreateAuthService(db, passwordService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(
            "audit-login",
            "wrong-password",
            "test-client",
            "10.0.0.1",
            CancellationToken.None));

        var persistedUser = await db.Users.SingleAsync(x => x.Id == new UserId("user-audit-login"));
        Assert.Equal(1, persistedUser.FailedLoginCount);

        var audit = Assert.Single(await db.SecurityAuditRecords.ToListAsync());
        Assert.Equal("iam.auth.login.failed", audit.Action);
        Assert.Equal("failure", audit.Outcome);
        Assert.Equal("user", audit.TargetType);
        Assert.Equal("user-audit-login", audit.TargetId);
        Assert.Equal("10.0.0.1", audit.SourceIp);
        Assert.Contains("\"failedLoginCount\":1", audit.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_revoke_persists_security_audit_record()
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var session = new UserSession(
            new UserSessionId("session-audit-revoke"),
            new UserId("user-admin"),
            "refresh-token-hash",
            now,
            now.AddDays(14),
            1,
            "test-client",
            "10.0.0.2");
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        var auth = CreateAuthService(db);

        await auth.RevokeSessionAsync(
            "session-audit-revoke",
            "admin-revoke",
            new SecurityAuditContext("user-admin", "corr-revoke", "10.0.0.2"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var audit = Assert.Single(await db.SecurityAuditRecords.ToListAsync());
        Assert.Equal("iam.session.revoked", audit.Action);
        Assert.Equal("success", audit.Outcome);
        Assert.Equal("session", audit.TargetType);
        Assert.Equal("session-audit-revoke", audit.TargetId);
        Assert.Equal("user-admin", audit.Actor);
        Assert.Equal("corr-revoke", audit.CorrelationId);
        Assert.Contains("\"reason\":\"admin-revoke\"", audit.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_permission_patch_persists_before_and_after_security_audit_record()
    {
        await using var db = CreateDbContext();
        var role = new Role(
            new RoleId("role-audit-operator"),
            "Audit Operator",
            ["iam.roles.read", "ops.tasks.read"]);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var roles = new PostgreSqlIamRoleApplicationService(
            new RoleRepository(db),
            new SecurityAuditRecorder(new SecurityAuditRepository(db)));

        await roles.PatchRolePermissionsAsync(
            "role-audit-operator",
            ["iam.users.read", "ops.tasks.read"],
            new SecurityAuditContext("user-admin", "corr-role", "10.0.0.3"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var audit = Assert.Single(await db.SecurityAuditRecords.ToListAsync());
        Assert.Equal("iam.role.permissions.changed", audit.Action);
        Assert.Equal("success", audit.Outcome);
        Assert.Equal("role", audit.TargetType);
        Assert.Equal("role-audit-operator", audit.TargetId);
        Assert.Equal("user-admin", audit.Actor);
        Assert.Contains("\"before\":[\"iam.roles.read\",\"ops.tasks.read\"]", audit.DetailsJson, StringComparison.Ordinal);
        Assert.Contains("\"after\":[\"iam.users.read\",\"ops.tasks.read\"]", audit.DetailsJson, StringComparison.Ordinal);

        var query = new PostgreSqlIamSecurityAuditApplicationService(new SecurityAuditRepository(db));
        var records = await query.ListAsync(
            new SecurityAuditListOptions(null, null, "iam.role.permissions.changed", "role", "role-audit-operator", 10),
            CancellationToken.None);
        var queried = Assert.Single(records);
        Assert.Equal(audit.Id.Id, queried.SecurityAuditRecordId);
    }

    private static PostgreSqlIamAuthService CreateAuthService(
        ApplicationDbContext db,
        IamPasswordService? passwordService = null)
    {
        passwordService ??= new IamPasswordService();
        return new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
            new ConnectorHostCredentialRepository(db),
            new ExternalClientRepository(db),
            passwordService,
            new IamTokenService(new ConfigurationBuilder().Build(), new TestWebHostEnvironment()),
            Options.Create(new IamAuthenticationOptions()),
            Options.Create(new EnterpriseIdentityOptions()),
            new InMemoryMfaChallengeStore(),
            new SecurityAuditRecorder(new SecurityAuditRepository(db)),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"iam-security-audit-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}
