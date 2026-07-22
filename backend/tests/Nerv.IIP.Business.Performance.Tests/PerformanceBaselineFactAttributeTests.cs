namespace Nerv.IIP.Business.Performance.Tests;

public sealed class PerformanceBaselineFactAttributeTests
{
    [Fact]
    public void GetSkipReason_skips_specialized_scenario_when_required_environment_is_missing()
    {
        var skipReason = PerformanceBaselineFactAttribute.GetSkipReason(
            scenario: "scheduling",
            connectionString: "Host=localhost",
            requestedScenario: "all",
            requiredEnvironmentVariable: "NERV_IIP_APS_SCALE_EVIDENCE_DIRECTORY",
            requiredEnvironmentValue: null);

        Assert.Equal(
            "Set NERV_IIP_APS_SCALE_EVIDENCE_DIRECTORY to run the 'scheduling' performance baseline.",
            skipReason);
    }

    [Fact]
    public void GetSkipReason_enables_specialized_scenario_when_required_environment_is_present()
    {
        var skipReason = PerformanceBaselineFactAttribute.GetSkipReason(
            scenario: "scheduling",
            connectionString: "Host=localhost",
            requestedScenario: "all",
            requiredEnvironmentVariable: "NERV_IIP_APS_SCALE_EVIDENCE_DIRECTORY",
            requiredEnvironmentValue: "artifacts/aps-scale");

        Assert.Null(skipReason);
    }
}
