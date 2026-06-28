using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamEnterpriseIdentityTests
{
    [Fact]
    public async Task Oidc_callback_maps_existing_user_and_binds_sso_session()
    {
        await using var factory = EnterpriseFactory("demo", requireMfa: false);
        var client = factory.CreateClient();

        var callback = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/oidc/callback",
            new
            {
                provider = "demo",
                subject = "entra-user-admin",
                email = "admin@nerv-iip.local",
                organizationId = "org-001",
                environmentId = "env-dev",
                callbackSecret = "oidc-callback-secret"
            });

        callback.EnsureSuccessStatusCode();
        var response = await ReadResponseDataAsync<EnterpriseAuthResponse>(callback);
        Assert.False(response!.MfaRequired);
        Assert.Null(response.MfaChallengeId);
        Assert.NotNull(response.Session);
        Assert.False(string.IsNullOrWhiteSpace(response.Session.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.Session.AccessToken);
        var me = await client.GetAsync("/api/iam/v1/me");
        me.EnsureSuccessStatusCode();

        var store = factory.Services.GetRequiredService<Nerv.IIP.Iam.Infrastructure.InMemoryIamStore>();
        var session = Assert.Single(store.Sessions, x => x.SessionId == response.Session.SessionId);
        Assert.Equal("oidc", session.AuthenticationMethod);
        Assert.Equal("demo", session.ExternalProvider);
        Assert.Equal("entra-user-admin", session.ExternalSubject);
        Assert.Null(session.MfaVerifiedAtUtc);
    }

    [Fact]
    public async Task Oidc_callback_with_mfa_required_issues_challenge_before_session()
    {
        await using var factory = EnterpriseFactory("demo-mfa", requireMfa: true);
        var client = factory.CreateClient();

        var callback = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/oidc/callback",
            new
            {
                provider = "demo-mfa",
                subject = "entra-user-admin",
                email = "admin@nerv-iip.local",
                organizationId = "org-001",
                environmentId = "env-dev",
                callbackSecret = "oidc-callback-secret"
            });

        callback.EnsureSuccessStatusCode();
        var challenge = await ReadResponseDataAsync<EnterpriseAuthResponse>(callback);
        Assert.True(challenge!.MfaRequired);
        Assert.False(string.IsNullOrWhiteSpace(challenge.MfaChallengeId));
        Assert.Null(challenge.Session);

        var wrongCode = await client.PostAsJsonAsync(
            $"/api/iam/v1/auth/mfa/challenges/{challenge.MfaChallengeId}/verify",
            new { code = "111111" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCode.StatusCode);

        var consumedChallenge = await client.PostAsJsonAsync(
            $"/api/iam/v1/auth/mfa/challenges/{challenge.MfaChallengeId}/verify",
            new { code = "654321" });
        Assert.Equal(HttpStatusCode.Unauthorized, consumedChallenge.StatusCode);

        var secondCallback = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/oidc/callback",
            new
            {
                provider = "demo-mfa",
                subject = "entra-user-admin",
                email = "admin@nerv-iip.local",
                organizationId = "org-001",
                environmentId = "env-dev",
                callbackSecret = "oidc-callback-secret"
            });
        secondCallback.EnsureSuccessStatusCode();
        var secondChallenge = await ReadResponseDataAsync<EnterpriseAuthResponse>(secondCallback);

        var verified = await client.PostAsJsonAsync(
            $"/api/iam/v1/auth/mfa/challenges/{secondChallenge.MfaChallengeId}/verify",
            new { code = "654321" });
        verified.EnsureSuccessStatusCode();
        var response = await ReadResponseDataAsync<EnterpriseAuthResponse>(verified);

        Assert.False(response!.MfaRequired);
        Assert.NotNull(response.Session);

        var store = factory.Services.GetRequiredService<Nerv.IIP.Iam.Infrastructure.InMemoryIamStore>();
        var session = Assert.Single(store.Sessions, x => x.SessionId == response.Session!.SessionId);
        Assert.Equal("oidc", session.AuthenticationMethod);
        Assert.NotNull(session.MfaVerifiedAtUtc);
    }

    [Fact]
    public async Task Oidc_callback_revokes_previous_active_sso_session_for_same_external_subject()
    {
        await using var factory = EnterpriseFactory("demo-rotate", requireMfa: false);
        var client = factory.CreateClient();

        var firstCallback = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/oidc/callback",
            new
            {
                provider = "demo-rotate",
                subject = "entra-user-admin",
                email = "admin@nerv-iip.local",
                organizationId = "org-001",
                environmentId = "env-dev",
                callbackSecret = "oidc-callback-secret"
            });
        firstCallback.EnsureSuccessStatusCode();
        var first = await ReadResponseDataAsync<EnterpriseAuthResponse>(firstCallback);

        var secondCallback = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/oidc/callback",
            new
            {
                provider = "demo-rotate",
                subject = "entra-user-admin",
                email = "admin@nerv-iip.local",
                organizationId = "org-001",
                environmentId = "env-dev",
                callbackSecret = "oidc-callback-secret"
            });
        secondCallback.EnsureSuccessStatusCode();
        var second = await ReadResponseDataAsync<EnterpriseAuthResponse>(secondCallback);

        var store = factory.Services.GetRequiredService<Nerv.IIP.Iam.Infrastructure.InMemoryIamStore>();
        var firstSession = Assert.Single(store.Sessions, x => x.SessionId == first.Session!.SessionId);
        var secondSession = Assert.Single(store.Sessions, x => x.SessionId == second.Session!.SessionId);
        Assert.NotNull(firstSession.RevokedAtUtc);
        Assert.Null(secondSession.RevokedAtUtc);
    }

    [Fact]
    public void Iam_rejects_default_mfa_development_code_outside_development()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKeys:0:Kid", IamJwtTestKeys.Kid);
                builder.UseSetting("Iam:Jwt:SigningKeys:0:PrivateKeyPem", IamJwtTestKeys.PrivateKeyPem);
                builder.UseSetting("Iam:Secrets:Pepper", "test-production-pepper");
            });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Iam:EnterpriseIdentity:Mfa:DevelopmentCode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_oidc_callback_rejects_body_claims_even_with_callback_secret()
    {
        await using var factory = ProductionEnterpriseFactory("prod-demo", requireMfa: false);
        var client = factory.CreateClient();

        var callback = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/oidc/callback",
            new
            {
                provider = "prod-demo",
                subject = "entra-user-admin",
                email = "admin@nerv-iip.local",
                organizationId = "org-001",
                environmentId = "env-dev",
                callbackSecret = "oidc-callback-secret"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, callback.StatusCode);
    }

    [Fact]
    public async Task Production_mfa_verify_rejects_development_code_even_when_challenge_exists()
    {
        await using var factory = ProductionEnterpriseFactory("prod-mfa", requireMfa: true);
        var challengeStore = factory.Services.GetRequiredService<IMfaChallengeStore>();
        var challengeId = challengeStore.Create(new MfaChallengeContext(
            "user-admin",
            "prod-mfa",
            "entra-user-admin",
            "org-001",
            "env-dev",
            DateTimeOffset.UtcNow.AddMinutes(5)));
        var client = factory.CreateClient();

        var verified = await client.PostAsJsonAsync(
            $"/api/iam/v1/auth/mfa/challenges/{challengeId}/verify",
            new { code = "654321" });

        Assert.Equal(HttpStatusCode.Unauthorized, verified.StatusCode);
    }

    [Fact]
    public async Task Oidc_callback_rejects_invalid_callback_secret()
    {
        await using var factory = EnterpriseFactory("demo-secret", requireMfa: false);
        var client = factory.CreateClient();

        var callback = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/oidc/callback",
            new
            {
                provider = "demo-secret",
                subject = "entra-user-admin",
                email = "admin@nerv-iip.local",
                organizationId = "org-001",
                environmentId = "env-dev",
                callbackSecret = "wrong-secret"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, callback.StatusCode);
    }

    [Fact]
    public async Task External_client_authorization_grant_enforces_resource_scope()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var token = await client.PostAsJsonAsync(
            "/api/iam/v1/auth/client-token",
            new
            {
                clientId = "external-client-resource-demo",
                clientSecret = "external-client-resource-secret",
                scope = "ops.tasks.create"
            });
        token.EnsureSuccessStatusCode();
        var auth = await ReadResponseDataAsync<ClientCredentialsTokenResponse>(token);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var allowed = await client.PostAsJsonAsync(
            "/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest(
                "ops.tasks.create",
                "org-001",
                "env-dev",
                "operation-template",
                "restart-critical"));
        allowed.EnsureSuccessStatusCode();

        var denied = await client.PostAsJsonAsync(
            "/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest(
                "ops.tasks.create",
                "org-001",
                "env-dev",
                "operation-template",
                "restart-low-risk"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static WebApplicationFactory<Program> EnterpriseFactory(string provider, bool requireMfa)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting($"Iam:EnterpriseIdentity:OidcProviders:{provider}:Enabled", "true");
                builder.UseSetting($"Iam:EnterpriseIdentity:OidcProviders:{provider}:Issuer", $"https://idp.example/{provider}");
                builder.UseSetting($"Iam:EnterpriseIdentity:OidcProviders:{provider}:CallbackSecret", "oidc-callback-secret");
                builder.UseSetting($"Iam:EnterpriseIdentity:OidcProviders:{provider}:RequireMfa", requireMfa.ToString());
                builder.UseSetting($"Iam:EnterpriseIdentity:OidcProviders:{provider}:AllowedEmailDomain", "nerv-iip.local");
                builder.UseSetting("Iam:EnterpriseIdentity:Mfa:DevelopmentCode", "654321");
        });
    }

    private static WebApplicationFactory<Program> ProductionEnterpriseFactory(string provider, bool requireMfa)
    {
        return EnterpriseFactory(provider, requireMfa)
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Iam:Jwt:SigningKeys:0:Kid", IamJwtTestKeys.Kid);
                builder.UseSetting("Iam:Jwt:SigningKeys:0:PrivateKeyPem", IamJwtTestKeys.PrivateKeyPem);
                builder.UseSetting("Iam:Secrets:Pepper", "test-production-pepper");
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
            });
    }

    private sealed record EnterpriseAuthResponse(bool MfaRequired, string? MfaChallengeId, AuthResponse? Session);
    private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
    private sealed record ClientCredentialsTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, string Scope);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
