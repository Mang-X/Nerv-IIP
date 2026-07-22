namespace Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

public sealed record OrderUrgencyDeletionAuthorization(
    string Reference,
    string Actor,
    string Reason,
    DateTimeOffset ApprovedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public bool IsValidAt(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(Reference) &&
        !string.IsNullOrWhiteSpace(Actor) &&
        !string.IsNullOrWhiteSpace(Reason) &&
        ApprovedAtUtc <= now &&
        ExpiresAtUtc > now &&
        ExpiresAtUtc > ApprovedAtUtc;
}

public sealed record OrderUrgencyRetentionScope(
    string OrganizationId,
    string EnvironmentId,
    TimeSpan OnlineRetention,
    TimeSpan TotalRetention,
    int BatchSize,
    bool LegalHoldActive,
    OrderUrgencyDeletionAuthorization? SourceDeletionAuthorization,
    OrderUrgencyDeletionAuthorization? ArchiveDeletionAuthorization)
{
    public bool CanDeleteSource(DateTimeOffset now) =>
        !LegalHoldActive && SourceDeletionAuthorization?.IsValidAt(now) == true;

    public bool CanDeleteArchive(DateTimeOffset now) =>
        !LegalHoldActive && ArchiveDeletionAuthorization?.IsValidAt(now) == true;
}

public sealed record OrderUrgencyRetentionPolicy(
    IReadOnlyCollection<OrderUrgencyRetentionScope> Scopes,
    IReadOnlyCollection<string> Errors)
{
    public static OrderUrgencyRetentionPolicy Load(IConfiguration configuration, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("OrderUrgencyRetention:Enabled"))
        {
            return new OrderUrgencyRetentionPolicy([], []);
        }

        var scopes = new List<OrderUrgencyRetentionScope>();
        var errors = new List<string>();
        foreach (var section in configuration.GetSection("OrderUrgencyRetention:Scopes").GetChildren())
        {
            if (!section.GetValue<bool>("Enabled")) continue;

            var organizationId = section["OrganizationId"]?.Trim();
            var environmentId = section["EnvironmentId"]?.Trim();
            var onlineDays = section.GetValue<int?>("OnlineRetentionDays") ?? 180;
            var totalDays = section.GetValue<int?>("TotalRetentionDays") ?? 1095;
            var batchSize = section.GetValue<int?>("BatchSize") ?? 100;
            var prefix = $"OrderUrgencyRetention scope {section.Key}";
            if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
            {
                errors.Add($"{prefix} requires organization and environment ids.");
                continue;
            }
            if (onlineDays <= 0 || totalDays <= onlineDays || batchSize is < 1 or > 5000)
            {
                errors.Add($"{prefix} has invalid retention days or batch size.");
                continue;
            }
            if (scopes.Any(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId))
            {
                errors.Add($"{prefix} duplicates organization/environment scope.");
                continue;
            }

            var sourceAuthorization = ReadAuthorization(section.GetSection("SourceDeletionAuthorization"));
            var archiveAuthorization = ReadAuthorization(section.GetSection("ArchiveDeletionAuthorization"));
            if (sourceAuthorization is not null && !sourceAuthorization.IsValidAt(now))
            {
                sourceAuthorization = null;
            }
            if (archiveAuthorization is not null && !archiveAuthorization.IsValidAt(now))
            {
                archiveAuthorization = null;
            }

            scopes.Add(new OrderUrgencyRetentionScope(
                organizationId,
                environmentId,
                TimeSpan.FromDays(onlineDays),
                TimeSpan.FromDays(totalDays),
                batchSize,
                section.GetValue<bool>("LegalHoldActive"),
                sourceAuthorization,
                archiveAuthorization));
        }

        return new OrderUrgencyRetentionPolicy(scopes, errors);
    }

    private static OrderUrgencyDeletionAuthorization? ReadAuthorization(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        if (!DateTimeOffset.TryParse(section["ApprovedAtUtc"], out var approvedAtUtc) ||
            !DateTimeOffset.TryParse(section["ExpiresAtUtc"], out var expiresAtUtc))
        {
            return null;
        }

        return new OrderUrgencyDeletionAuthorization(
            section["Reference"]?.Trim() ?? string.Empty,
            section["Actor"]?.Trim() ?? string.Empty,
            section["Reason"]?.Trim() ?? string.Empty,
            approvedAtUtc.ToUniversalTime(),
            expiresAtUtc.ToUniversalTime());
    }
}
