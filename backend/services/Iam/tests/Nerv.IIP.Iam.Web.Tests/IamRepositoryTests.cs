using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamRepositoryTests
{
    [Fact]
    public async Task User_lookup_normalizes_parameters_with_invariant_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var turkish = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = turkish;
            CultureInfo.CurrentUICulture = turkish;

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
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
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
            new InMemoryMfaChallengeStore());

        var response = await authService.LoginAsync("session-v7", "Password123!", null, null, CancellationToken.None);

        AssertVersion7GuidSuffix(response.SessionId, "session-");
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

    private static void AssertVersion7GuidSuffix(string id, string prefix)
    {
        Assert.StartsWith(prefix, id, StringComparison.Ordinal);
        var suffix = id[prefix.Length..];
        Assert.True(Guid.TryParseExact(suffix, "N", out _));
        Assert.Equal('7', suffix[12]);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Nerv.IIP.Iam.Web.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
