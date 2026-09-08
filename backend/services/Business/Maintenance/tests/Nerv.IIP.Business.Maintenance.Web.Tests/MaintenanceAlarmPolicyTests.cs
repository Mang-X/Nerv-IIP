using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceAlarmPolicyTests
{
    [Fact]
    public void Configuration_snapshot_keeps_original_policy_until_services_restart()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maintenance:AlarmPolicy:Entries:0:OrganizationId"] = "org-a",
            ["Maintenance:AlarmPolicy:Entries:0:EnvironmentId"] = "env-a",
            ["Maintenance:AlarmPolicy:Entries:0:AlarmCode"] = "Heat",
            ["Maintenance:AlarmPolicy:Entries:0:Mode"] = "WorkOrderAndOccupy",
            ["Maintenance:AlarmPolicy:Entries:0:AssetUnavailableReasonCode"] = "Custom_Heat",
        }).Build();
        using var first = Services(configuration);
        var snapshot = first.GetRequiredService<IOptions<MaintenanceAlarmPolicyOptions>>().Value;
        Assert.Equal("Custom_Heat", snapshot.ResolveReasonCode("org-a", "env-a", "Heat"));
        Assert.Null(snapshot.ResolveReasonCode("org-b", "env-a", "Heat"));
        Assert.Null(snapshot.ResolveReasonCode("org-a", "env-b", "Heat"));
        Assert.Null(snapshot.ResolveReasonCode("org-a", "env-a", "heat"));
        Assert.Null(snapshot.ResolveReasonCode("org-a", "env-a", " Heat"));

        configuration["Maintenance:AlarmPolicy:Entries:0:AssetUnavailableReasonCode"] = "Replacement";
        configuration.Reload();
        Assert.Equal("Custom_Heat", first.GetRequiredService<IOptions<MaintenanceAlarmPolicyOptions>>().Value.ResolveReasonCode("org-a", "env-a", "Heat"));
        using var restarted = Services(configuration);
        Assert.Equal("Replacement", restarted.GetRequiredService<IOptions<MaintenanceAlarmPolicyOptions>>().Value.ResolveReasonCode("org-a", "env-a", "Heat"));
    }

    [Theory]
    [InlineData(null, "Heat")]
    [InlineData("Heat", null)]
    [InlineData("Heat", "Heat")]
    [InlineData(null, null)]
    public void Overlapping_entries_are_rejected(string? firstAlarm, string? secondAlarm)
    {
        var options = new MaintenanceAlarmPolicyOptions { Entries = [Entry(firstAlarm), Entry(secondAlarm)] };
        Assert.True(new MaintenanceAlarmPolicyOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Distinct_exact_alarms_and_scopes_are_allowed_and_plain_mode_returns_no_reason()
    {
        var other = Entry("Heat");
        other.EnvironmentId = "other-env";
        var options = new MaintenanceAlarmPolicyOptions { Entries = [Entry("Heat"), Entry("heat"), other] };
        Assert.True(new MaintenanceAlarmPolicyOptionsValidator().Validate(null, options).Succeeded);
        Assert.Null(options.ResolveReasonCode("org-a", "env-a", "Heat"));
        Assert.Null(new MaintenanceAlarmPolicyOptions().ResolveReasonCode("org-a", "env-a", "Heat"));
    }

    [Theory]
    [InlineData("WorkOrderOnly", "code")]
    [InlineData("WorkOrderAndOccupy", null)]
    [InlineData("WorkOrderAndOccupy", "")]
    [InlineData("unknown", null)]
    public void Invalid_modes_and_reason_combinations_are_rejected(string mode, string? reason)
    {
        var entry = Entry(null);
        entry.Mode = mode;
        entry.AssetUnavailableReasonCode = reason;
        Assert.True(new MaintenanceAlarmPolicyOptionsValidator().Validate(null, new() { Entries = [entry] }).Failed);
    }

    private static MaintenanceAlarmPolicyEntry Entry(string? alarm) => new()
    {
        OrganizationId = "org-a", EnvironmentId = "env-a", AlarmCode = alarm, Mode = "WorkOrderOnly",
    };

    private static ServiceProvider Services(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<MaintenanceAlarmPolicyOptions>, MaintenanceAlarmPolicyOptionsValidator>();
        services.AddOptions<MaintenanceAlarmPolicyOptions>().Bind(configuration.GetSection(MaintenanceAlarmPolicyOptions.SectionName));
        return services.BuildServiceProvider();
    }
}
