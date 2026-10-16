using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nerv.IIP.Contracts.MasterData;

public sealed record SchedulingTimeContextRequest(string OrganizationId, string EnvironmentId, string PlanId,
    string PrincipalId, IReadOnlyCollection<string> WorkCenterCodes);
public sealed record SchedulingTimeContextItem(string WorkCenterCode, string? PlantCode, string? PlantName,
    string? Timezone, string? UnresolvedReason);

/// <summary>只证明 Gateway 已授权的方案时间上下文请求；不授权主数据目录。</summary>
public static class SchedulingTimeContextAuthorization
{
    public const string HeaderName = "X-Nerv-IIP-Scheduling-Time-Authorization";
    public const string Permission = "business.scheduling.plans.read";
    public const string ConfigurationKey = "MasterData:SchedulingTimeContext:SigningKey";

    public static string Sign(string key, SchedulingTimeContextRequest request, DateTimeOffset issuedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var timestamp = issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return timestamp + "." + Signature(key, request, timestamp);
    }

    public static bool Verify(string? key, SchedulingTimeContextRequest request, string proof, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(request.PrincipalId)) return false;
        var parts = proof.Split('.');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs((decimal)now.ToUnixTimeSeconds() - seconds) > 300) return false;
        var expected = Encoding.ASCII.GetBytes(Signature(key, request, parts[0]));
        var actual = Encoding.ASCII.GetBytes(parts[1]);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Signature(string key, SchedulingTimeContextRequest request, string timestamp)
    {
        // 固定 issuer / permission / purpose，完整 DTO 含主体、租户、方案及集合，避免可替换请求。
        var payload = $"business-gateway\n{Permission}\nscheduling-time-context\n{timestamp}\n{JsonSerializer.Serialize(request)}";
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload)));
    }
}
