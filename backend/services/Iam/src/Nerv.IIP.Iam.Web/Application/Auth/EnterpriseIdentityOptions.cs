using System.Collections.Concurrent;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class EnterpriseIdentityOptions
{
    public Dictionary<string, OidcProviderOptions> OidcProviders { get; init; } = [];
    public MfaOptions Mfa { get; init; } = new();
}

public sealed class OidcProviderOptions
{
    public bool Enabled { get; init; }
    public string Issuer { get; init; } = string.Empty;
    public string CallbackSecret { get; init; } = string.Empty;
    public string? AllowedEmailDomain { get; init; }
    public bool RequireMfa { get; init; }
}

public sealed class MfaOptions
{
    public string DevelopmentCode { get; init; } = "000000";
    public int ChallengeMinutes { get; init; } = 5;
}

public sealed record MfaChallengeContext(
    string UserId,
    string Provider,
    string Subject,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset ExpiresAtUtc);

public interface IMfaChallengeStore
{
    string Create(MfaChallengeContext context);
    MfaChallengeContext? Consume(string challengeId, string code, string expectedCode);
}

public sealed class InMemoryMfaChallengeStore : IMfaChallengeStore
{
    private readonly ConcurrentDictionary<string, MfaChallengeContext> _challenges = new(StringComparer.Ordinal);

    public string Create(MfaChallengeContext context)
    {
        var challengeId = $"mfa-{Guid.CreateVersion7():N}";
        _challenges[challengeId] = context;
        return challengeId;
    }

    public MfaChallengeContext? Consume(string challengeId, string code, string expectedCode)
    {
        if (!_challenges.TryGetValue(challengeId, out var context))
        {
            return null;
        }

        if (context.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || !string.Equals(code, expectedCode, StringComparison.Ordinal))
        {
            return null;
        }

        _challenges.TryRemove(challengeId, out _);
        return context;
    }
}
