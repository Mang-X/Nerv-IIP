using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    public const string DeviceAssetId = "DEV-CNC-DEMO";
    public const string TemperatureTagKey = "temperature";
    public const string VibrationTagKey = "vibration";
    public const string RuleCode = "ALARM-DEMO-001";

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        await EnsureTagAsync(organizationId, environmentId, TemperatureTagKey, "degC", cancellationToken);
        await EnsureTagAsync(organizationId, environmentId, VibrationTagKey, "mm/s", cancellationToken);

        var rule = await dbContext.AlarmRules.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.DeviceAssetId == DeviceAssetId && x.RuleCode == RuleCode,
            cancellationToken);
        if (rule is null)
        {
            dbContext.AlarmRules.Add(AlarmRule.Configure(
                organizationId, environmentId, DeviceAssetId, RuleCode, "VIBRATION-HIGH", "critical", VibrationTagKey,
                ">=", 8m, "mm/s", true, 0.3m, 4, 4, 4, "critical"));
        }
        else if (rule.AlarmCode != "VIBRATION-HIGH" || rule.TagKey != VibrationTagKey || rule.ComparisonOperator != ">=" ||
                 rule.ThresholdValue != 8m || rule.UnitCode != "mm/s" || !rule.IsEnabled ||
                 rule.DeadbandValue != 0.3m || rule.OnDelaySeconds != 4 || rule.OffDelaySeconds != 4 ||
                 rule.MinDurationSeconds != 4)
        {
            throw Collision(RuleCode);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureTagAsync(
        string organizationId,
        string environmentId,
        string tagKey,
        string unitCode,
        CancellationToken cancellationToken)
    {
        var tag = await dbContext.TelemetryTags.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.DeviceAssetId == DeviceAssetId &&
            x.TagKey == tagKey,
            cancellationToken);
        if (tag is null)
        {
            dbContext.TelemetryTags.Add(TelemetryTag.Create(
                organizationId,
                environmentId,
                DeviceAssetId,
                tagKey,
                "decimal",
                unitCode,
                "sample-2s"));
            return;
        }

        if (tag.ValueType != "decimal" || tag.UnitCode != unitCode || tag.SamplingPolicy != "sample-2s")
        {
            throw Collision(tagKey);
        }
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo telemetry fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
