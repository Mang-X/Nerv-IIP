using System.Globalization;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed record SchedulingExecutionDeviationToleranceOption(int? ToleranceMinutes)
{
    public static SchedulingExecutionDeviationToleranceOption Disabled { get; } = new((int?)null);
}

public static class SchedulingExecutionDeviationToleranceResolver
{
    public const string ConfigurationKey = "Scheduling:ExecutionDeviationToleranceMinutes";

    public static SchedulingExecutionDeviationToleranceOption Resolve(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return SchedulingExecutionDeviationToleranceOption.Disabled;
        }

        if (int.TryParse(
                configuredValue.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var toleranceMinutes) &&
            toleranceMinutes >= 0)
        {
            return new SchedulingExecutionDeviationToleranceOption(toleranceMinutes);
        }

        throw new InvalidOperationException(
            $"{ConfigurationKey} 必须是非负整数分钟；留空表示禁用执行偏差失效。");
    }
}
