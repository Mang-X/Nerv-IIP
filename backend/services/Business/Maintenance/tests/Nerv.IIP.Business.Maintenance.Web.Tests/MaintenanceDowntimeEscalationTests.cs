using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Sdk.Core;
using Nerv.IIP.Sdk.Notification;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

// DomainInvariant: #4131 acceptance / ADR 0032 §2. InMemory proves orchestration, not SQL or durable dedupe.
public sealed class MaintenanceDowntimeEscalationTests
{
    [Fact]
    public async Task Escalation_uses_actual_start_strict_threshold_exact_scope_and_stable_intent_key()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(start.AddMinutes(60));
        var order = Add("org", "env");
        order.UpdateExpectedRestore(start.AddMinutes(10));
        Add("other", "env");
        Add("org", "other");
        var completed = Add("org", "env");
        completed.Complete("fixed", "fault", 1, []);
        var cancelled = Add("org", "env");
        cancelled.Cancel();
        var client = new CapturingClient();
        var options = Options.Create(new MaintenanceDowntimeEscalationOptions
        {
            Threshold = TimeSpan.FromMinutes(60),
            Scopes = [new() { OrganizationId = "org", EnvironmentId = "env", PlannerRecipientRefs = ["user:planner"] }]
        });
        var scanner = new MaintenanceDowntimeEscalationScanner(db, client, options, time);
        await db.SaveChangesAsync();
        await scanner.ScanAsync(default);
        Assert.Empty(client.Requests);
        time.Advance(TimeSpan.FromTicks(1));
        await scanner.ScanAsync(default);
        var first = Assert.Single(client.Requests);
        Assert.Equal("org", first.Context.OrganizationId);
        Assert.Equal("env", first.Context.EnvironmentId);
        Assert.Equal(["user:planner"], first.Intent.SuggestedRecipientRefs);
        Assert.Equal(order.Id.ToString(), first.Intent.Resource!.ResourceId);
        order.UpdateExpectedRestore(start.AddHours(3));
        await db.SaveChangesAsync();
        await scanner.ScanAsync(default);
        Assert.Equal(first.Intent.DedupeKey, client.Requests[1].Intent.DedupeKey);
        order.Cancel();
        await db.SaveChangesAsync();
        await scanner.ScanAsync(default);
        Assert.Equal(2, client.Requests.Count);

        MaintenanceWorkOrder Add(string org, string env)
        {
            var item = MaintenanceWorkOrder.OpenManual(org, env, "device", "high", "operator");
            item.MarkAssetUnavailable(start, "fault");
            db.MaintenanceWorkOrders.Add(item);
            return item;
        }
    }

    private sealed class CapturingClient : INotificationClient
    {
        public List<(SubmitNotificationIntentRequest Intent, PlatformRequestContext Context)> Requests { get; } = [];
        public Task<NotificationIntentResponse> SubmitIntentAsync(SubmitNotificationIntentRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default)
        {
            Requests.Add((request, context));
            return Task.FromResult(new NotificationIntentResponse("intent", Requests.Count > 1, []));
        }
        public Task<NotificationMessageListResponse> ListMessagesAsync(PlatformRequestContext context, string? recipientRef = null, string? status = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MarkNotificationMessageReadResponse> MarkReadAsync(string messageId, PlatformRequestContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
