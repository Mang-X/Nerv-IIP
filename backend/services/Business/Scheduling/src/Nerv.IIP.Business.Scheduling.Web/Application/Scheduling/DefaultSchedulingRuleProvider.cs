using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed class DefaultSchedulingRuleProvider : ISchedulingRuleProvider
{
    public const string ProviderId = "built-in";
    public const string ProfileId = "adr-0014-default";
    public const string ProfileVersion = "v1";

    public Task<SchedulingRuleProviderResult> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(problem);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new SchedulingRuleProviderResult(
            ProviderId,
            ProfileId,
            ProfileVersion,
            problem,
            []));
    }
}
