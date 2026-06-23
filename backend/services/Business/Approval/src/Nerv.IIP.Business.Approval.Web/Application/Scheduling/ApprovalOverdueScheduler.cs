using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;

namespace Nerv.IIP.Business.Approval.Web.Application.Scheduling;

public sealed class ApprovalOverdueScheduler(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ApprovalOverdueScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Approval:OverdueCheck:Enabled"))
        {
            return;
        }

        var scopes = GetConfiguredScopes().ToArray();
        if (scopes.Length == 0)
        {
            logger.LogWarning("Approval overdue check is enabled but no organization/environment scope is configured.");
            return;
        }

        var interval = configuration.GetValue("Approval:OverdueCheck:Interval", DefaultInterval);
        if (interval <= TimeSpan.Zero)
        {
            logger.LogWarning(
                "Approval overdue check interval {Interval} is not positive; falling back to {DefaultInterval}.",
                interval,
                DefaultInterval);
            interval = DefaultInterval;
        }

        using var timer = new PeriodicTimer(interval);
        await TryCheckAllScopesAsync(scopes, stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryCheckAllScopesAsync(scopes, stoppingToken);
        }
    }

    private IEnumerable<ApprovalOverdueCheckScope> GetConfiguredScopes()
    {
        foreach (var scopeSection in configuration.GetSection("Approval:OverdueCheck:Scopes").GetChildren())
        {
            var scopeOrganizationId = scopeSection["OrganizationId"];
            var scopeEnvironmentId = scopeSection["EnvironmentId"];
            if (!string.IsNullOrWhiteSpace(scopeOrganizationId) && !string.IsNullOrWhiteSpace(scopeEnvironmentId))
            {
                yield return new ApprovalOverdueCheckScope(scopeOrganizationId, scopeEnvironmentId);
            }
        }

        var organizationId = configuration["Approval:OverdueCheck:OrganizationId"];
        var environmentId = configuration["Approval:OverdueCheck:EnvironmentId"];
        if (!string.IsNullOrWhiteSpace(organizationId) && !string.IsNullOrWhiteSpace(environmentId))
        {
            yield return new ApprovalOverdueCheckScope(organizationId, environmentId);
        }
    }

    private async Task TryCheckAllScopesAsync(
        IReadOnlyCollection<ApprovalOverdueCheckScope> scopes,
        CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            await TryCheckAsync(scope.OrganizationId, scope.EnvironmentId, cancellationToken);
        }
    }

    private async Task TryCheckAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var marked = await sender.Send(new CheckOverdueApprovalStepsCommand(organizationId, environmentId), cancellationToken);
            if (marked > 0)
            {
                logger.LogInformation(
                    "Marked {MarkedCount} overdue approval steps for {OrganizationId}/{EnvironmentId}.",
                    marked,
                    organizationId,
                    environmentId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Approval overdue check failed for {OrganizationId}/{EnvironmentId}; the scheduler will retry on the next tick.",
                organizationId,
                environmentId);
        }
    }

    private sealed record ApprovalOverdueCheckScope(string OrganizationId, string EnvironmentId);
}
