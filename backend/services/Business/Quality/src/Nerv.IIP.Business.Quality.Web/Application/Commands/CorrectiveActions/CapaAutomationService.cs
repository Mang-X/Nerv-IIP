using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;

public sealed class CapaAutomationOptions
{
    public static readonly string[] SupportedSeverities = ["minor", "major", "critical"];

    public bool Enabled { get; set; } = true;

    public string MinimumSeverity { get; set; } = "major";

    public string[] Dispositions { get; set; } =
    [
        QualityNcrDispositionTypes.Rework,
        QualityNcrDispositionTypes.Scrap,
    ];

    public string OwnerUserId { get; set; } = "system:quality";

    public int DueDays { get; set; } = 14;

    public static bool IsSupportedSeverity(string? severity)
    {
        return SupportedSeverities.Any(x => string.Equals(x, severity?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public interface ICorrectiveActionCodeGenerator
{
    Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken);
}

public sealed class CorrectiveActionCodeGenerator : ICorrectiveActionCodeGenerator
{
    public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var code = $"CAPA-{ToCodeToken(organizationId, "org")}-{ToCodeToken(environmentId, "env")}-{Guid.CreateVersion7():N}";
        return Task.FromResult(code);
    }

    private static string ToCodeToken(string value, string fallback)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .Take(12)
            .ToArray();

        return chars.Length == 0 ? fallback : new string(chars);
    }
}

public interface ICapaAutomationService
{
    Task OpenForDispositionIfRequiredAsync(NonconformanceReport ncr, CancellationToken cancellationToken);
}

public sealed class CapaAutomationService(
    ApplicationDbContext dbContext,
    ICorrectiveActionRepository correctiveActionRepository,
    ICorrectiveActionCodeGenerator codeGenerator,
    IOptions<CapaAutomationOptions> options)
    : ICapaAutomationService
{
    public async Task OpenForDispositionIfRequiredAsync(NonconformanceReport ncr, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ncr);
        var configured = options.Value;
        if (!configured.Enabled
            || string.IsNullOrWhiteSpace(ncr.DispositionType)
            || !DispositionMatches(configured, ncr.DispositionType)
            || await correctiveActionRepository.HasCapaForNcrAsync(
                ncr.OrganizationId,
                ncr.EnvironmentId,
                ncr.Id.ToString(),
                cancellationToken)
            || !await SeverityMeetsThresholdAsync(ncr, configured.MinimumSeverity, cancellationToken))
        {
            return;
        }

        var capaCode = await codeGenerator.NextAsync(ncr.OrganizationId, ncr.EnvironmentId, cancellationToken);
        var dueAtUtc = DateTimeOffset.UtcNow.AddDays(Math.Max(1, configured.DueDays));
        var ownerUserId = string.IsNullOrWhiteSpace(configured.OwnerUserId)
            ? "system:quality"
            : configured.OwnerUserId.Trim();
        var capa = CorrectiveAction.OpenFromNcr(
            ncr.OrganizationId,
            ncr.EnvironmentId,
            capaCode,
            ncr,
            $"Auto CAPA for {ncr.DispositionType} NCR {ncr.NcrCode}",
            "Contain nonconforming material and execute MRB disposition.",
            ownerUserId,
            dueAtUtc);
        capa.AddAction("corrective", $"Eliminate root cause for NCR {ncr.NcrCode}.", ownerUserId, dueAtUtc);
        capa.AddAction("preventive", $"Verify recurrence prevention for NCR {ncr.NcrCode}.", ownerUserId, dueAtUtc);
        await correctiveActionRepository.AddAsync(capa, cancellationToken);
    }

    private async Task<bool> SeverityMeetsThresholdAsync(
        NonconformanceReport ncr,
        string minimumSeverity,
        CancellationToken cancellationToken)
    {
        var severity = await dbContext.QualityReasons
            .Where(x => x.OrganizationId == ncr.OrganizationId
                && x.EnvironmentId == ncr.EnvironmentId
                && x.ReasonCode == ncr.DefectReason
                && x.Enabled)
            .Select(x => x.Severity)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(severity))
        {
            return false;
        }

        return SeverityRank(severity) >= SeverityRank(minimumSeverity);
    }

    private static bool DispositionMatches(CapaAutomationOptions options, string dispositionType)
    {
        return options.Dispositions.Any(x => string.Equals(x, dispositionType, StringComparison.OrdinalIgnoreCase));
    }

    private static int SeverityRank(string severity)
    {
        return severity.Trim().ToLowerInvariant() switch
        {
            "minor" => 1,
            "major" => 2,
            "critical" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported CAPA automation severity."),
        };
    }
}

public sealed class CapaAutomationOptionsValidator : IValidateOptions<CapaAutomationOptions>
{
    public ValidateOptionsResult Validate(string? name, CapaAutomationOptions options)
    {
        return CapaAutomationOptions.IsSupportedSeverity(options.MinimumSeverity)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Quality:CapaAutomation:MinimumSeverity must be one of: {string.Join(", ", CapaAutomationOptions.SupportedSeverities)}.");
    }
}
