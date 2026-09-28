using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamRepositoryTests
{
    [Fact]
    public async Task User_lookup_normalizes_parameters_with_invariant_culture()
    {
        // The scope serialises every culture mutator in the assembly and restores the exact prior
        // values on dispose, so tr-TR cannot outlive this test. It is still the process culture
        // while the scope is open.
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        globalState.UseCulture("tr-TR");

        await using var db = CreateDbContext();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-invariant-lookup"),
            "identity",
            "info@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var repository = new UserRepository(db);

        Assert.NotNull(await repository.GetByLoginNameAsync("IDENTITY"));
        Assert.NotNull(await repository.GetByEmailAsync("INFO@nerv-iip.local"));
    }

    [Fact]
    public async Task PostgreSql_auth_service_creates_version7_user_session_id()
    {
        await using var db = CreateDbContext();
        var passwordService = new IamPasswordService();
        var user = new User(
            new UserId("user-session-v7"),
            "session-v7",
            "session-v7@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1);
        db.Users.Add(user);
        // 没有成员关系的账号登录会被拒（iam-no-membership），这里要验的是会话 ID，给它一个成员关系。
        db.Memberships.Add(new Membership(
            new MembershipId("membership-session-v7"),
            user.Id,
            new OrganizationId("org-001"),
            new IamEnvironmentId("env-dev"),
            [new RoleId("role-erp-sales")]));
        await db.SaveChangesAsync();
        var tokenService = new IamTokenService(
            new ConfigurationBuilder().Build(),
            new TestWebHostEnvironment());
        var authService = new PostgreSqlIamAuthService(
            new UserRepository(db),
            new UserSessionRepository(db),
            new MembershipRepository(db),
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

        var response = await authService.LoginAsync("session-v7", "Password123!", null, null, CancellationToken.None);

        Assert.Empty(GuidVersionAssertions.Version7GuidSuffixFailures(response.SessionId, "session-"));
    }

    [Fact]
    public async Task PostgreSql_auth_service_rejects_a_user_without_membership_only_after_the_password_matches()
    {
        await using var db = CreateDbContext();
        var passwordService = new IamPasswordService();
        db.Users.Add(new User(
            new UserId("user-unassigned"),
            "unassigned",
            "unassigned@nerv-iip.local",
            passwordService.Hash("Password123!"),
            true,
            Guid.NewGuid().ToString("n"),
            1));
        await db.SaveChangesAsync();
        var authService = new PostgreSqlIamAuthService(
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
            new NoopSecurityAuditRecorder(),
            NullLogger<PostgreSqlIamAuthService>.Instance,
            new TestWebHostEnvironment());

        var wrongPassword = await Assert.ThrowsAsync<IamLoginRejectedException>(() =>
            authService.LoginAsync("unassigned", "Wrong123!", null, null, CancellationToken.None));
        Assert.Equal(IamLoginFailureCodes.InvalidCredentials, wrongPassword.Code);

        var unassigned = await Assert.ThrowsAsync<IamLoginRejectedException>(() =>
            authService.LoginAsync("unassigned", "Password123!", null, null, CancellationToken.None));
        Assert.Equal(IamLoginFailureCodes.NoMembership, unassigned.Code);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        // This test covers parameter-side invariant normalization. PostgreSQL index usage is
        // governed by the lower("LoginName")/lower("Email") expression-index migration.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"iam-repository-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

}
