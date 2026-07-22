using Microsoft.Extensions.Configuration;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class OrderUrgencyRetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_or_disabled_configuration_never_enables_a_scope()
    {
        Assert.Empty(OrderUrgencyRetentionPolicy.Load(new ConfigurationBuilder().Build(), Now).Scopes);

        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["OrderUrgencyRetention:Enabled"] = "false",
            ["OrderUrgencyRetention:Scopes:0:OrganizationId"] = "org-001",
            ["OrderUrgencyRetention:Scopes:0:EnvironmentId"] = "prod",
            ["OrderUrgencyRetention:Scopes:0:Enabled"] = "true",
        });

        Assert.Empty(OrderUrgencyRetentionPolicy.Load(configuration, Now).Scopes);
    }

    [Theory]
    [InlineData("0", "1095", "100")]
    [InlineData("180", "180", "100")]
    [InlineData("180", "1095", "0")]
    [InlineData("180", "1095", "5001")]
    public void Invalid_policy_values_fail_closed(string onlineDays, string totalDays, string batchSize)
    {
        var policy = OrderUrgencyRetentionPolicy.Load(Configuration(EnabledScope(onlineDays, totalDays, batchSize)), Now);

        Assert.Empty(policy.Scopes);
        Assert.NotEmpty(policy.Errors);
    }

    [Fact]
    public void Default_policy_is_180_days_online_three_years_total_and_no_hold()
    {
        var policy = OrderUrgencyRetentionPolicy.Load(Configuration(EnabledScope()), Now);

        var scope = Assert.Single(policy.Scopes);
        Assert.Equal(TimeSpan.FromDays(180), scope.OnlineRetention);
        Assert.Equal(TimeSpan.FromDays(1095), scope.TotalRetention);
        Assert.False(scope.LegalHoldActive);
        Assert.False(scope.CanDeleteSource(Now));
        Assert.False(scope.CanDeleteArchive(Now));
    }

    [Fact]
    public void Deletion_requires_a_complete_unexpired_authorization_and_no_legal_hold()
    {
        var values = EnabledScope();
        values["OrderUrgencyRetention:Scopes:0:SourceDeletionAuthorization:Reference"] = "CAB-2026-0042";
        values["OrderUrgencyRetention:Scopes:0:SourceDeletionAuthorization:Actor"] = "user:compliance";
        values["OrderUrgencyRetention:Scopes:0:SourceDeletionAuthorization:Reason"] = "Approved retention enforcement";
        values["OrderUrgencyRetention:Scopes:0:SourceDeletionAuthorization:ApprovedAtUtc"] = Now.AddHours(-1).ToString("O");
        values["OrderUrgencyRetention:Scopes:0:SourceDeletionAuthorization:ExpiresAtUtc"] = Now.AddHours(1).ToString("O");

        var scope = Assert.Single(OrderUrgencyRetentionPolicy.Load(Configuration(values), Now).Scopes);
        Assert.True(scope.CanDeleteSource(Now));
        Assert.False(scope.CanDeleteSource(Now.AddHours(2)));

        values["OrderUrgencyRetention:Scopes:0:LegalHoldActive"] = "true";
        var held = Assert.Single(OrderUrgencyRetentionPolicy.Load(Configuration(values), Now).Scopes);
        Assert.False(held.CanDeleteSource(Now));
    }

    private static Dictionary<string, string?> EnabledScope(
        string? onlineDays = null,
        string? totalDays = null,
        string? batchSize = null) => new()
    {
        ["OrderUrgencyRetention:Enabled"] = "true",
        ["OrderUrgencyRetention:Scopes:0:OrganizationId"] = "org-001",
        ["OrderUrgencyRetention:Scopes:0:EnvironmentId"] = "prod",
        ["OrderUrgencyRetention:Scopes:0:Enabled"] = "true",
        ["OrderUrgencyRetention:Scopes:0:OnlineRetentionDays"] = onlineDays,
        ["OrderUrgencyRetention:Scopes:0:TotalRetentionDays"] = totalDays,
        ["OrderUrgencyRetention:Scopes:0:BatchSize"] = batchSize,
    };

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
