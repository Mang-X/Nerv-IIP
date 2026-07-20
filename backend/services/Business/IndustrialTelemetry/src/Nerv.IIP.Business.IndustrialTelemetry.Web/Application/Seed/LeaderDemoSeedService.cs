using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

public sealed class LeaderDemoSeedService(ApplicationDbContext dbContext)
{
    public const string DeviceAssetId = "DEV-CNC-DEMO";
    public const string TemperatureTagKey = "temperature";
    public const string RuleCode = "MWO-DEMO-001";

    public async Task SeedAsync(string organizationId, string environmentId, CancellationToken cancellationToken = default)
    {
        var tag = await dbContext.TelemetryTags.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.DeviceAssetId == DeviceAssetId && x.TagKey == TemperatureTagKey,
            cancellationToken);
        if (tag is null)
        {
            dbContext.TelemetryTags.Add(TelemetryTag.Create(organizationId, environmentId, DeviceAssetId, TemperatureTagKey, "decimal", "degC", "bucket-10s"));
        }
        else if (tag.ValueType != "decimal" || tag.UnitCode != "degC" || tag.SamplingPolicy != "bucket-10s")
        {
            throw Collision(TemperatureTagKey);
        }

        var rule = await dbContext.AlarmRules.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.DeviceAssetId == DeviceAssetId && x.RuleCode == RuleCode,
            cancellationToken);
        if (rule is null)
        {
            dbContext.AlarmRules.Add(AlarmRule.Configure(
                organizationId, environmentId, DeviceAssetId, RuleCode, "TEMP-HIGH", "critical", TemperatureTagKey,
                ">=", 80m, "degC", true, 2m, 5, 5, 5, "critical"));
        }
        else if (rule.AlarmCode != "TEMP-HIGH" || rule.TagKey != TemperatureTagKey || rule.ComparisonOperator != ">=" ||
                 rule.ThresholdValue != 80m || rule.UnitCode != "degC" || !rule.IsEnabled)
        {
            throw Collision(RuleCode);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InvalidOperationException Collision(string key) =>
        new($"Reserved leader-demo telemetry fact '{key}' exists with incompatible tenant facts; the seed will not overwrite it.");
}
