using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Printing;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;
using Nerv.IIP.Contracts.BarcodeLabel;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Sdk.FileStorage;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

// PublicContract / DomainInvariant: #3049 A. InMemory proves read projection, not PostgreSQL persistence.
public sealed class TemplateAssetRetirementReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Checksum = "sha256:" + new string('a', 64);

    [Fact]
    public async Task Expired_fence_overrides_retained_detail_at_the_authoritative_boundary()
    {
        using var db = CreateDb();
        var template = LabelTemplate.Create("org", "env", "TPL", "模板", "file", "{}", "inactive");
        var decision = TemplateAssetRetirementDecision.Create("org", "env", template.Id, "TPL", "file", Checksum,
            "key", "user", TemplateAssetRetirementDecision.RequiredPermission, "reason", "trace");
        db.LabelTemplates.Add(template);
        db.TemplateAssetRetirementDecisions.Add(decision);
        db.Entry(decision).Property(x => x.Status).CurrentValue = "quota-released";
        db.TemplateAssetRetirementReplayFences.Add(new TemplateAssetRetirementReplayFence(decision, Now));
        await db.SaveChangesAsync();
        using var wire = new MetadataHandler(false);
        using var http = new HttpClient(wire) { BaseAddress = new Uri("https://file-storage.invalid") };
        using var adapter = new HttpFileStorageLabelTemplateAssetAdapter(new HttpFileStorageClient(http), http, TimeSpan.FromSeconds(1));
        var clock = new FakeTimeProvider(Now.AddTicks(-1));
        var handler = new GetTemplateAssetRetirementQueryHandler(db, adapter, clock);
        var query = new GetTemplateAssetRetirementQuery("org", "env", template.Id, "file");
        Assert.Equal(TemplateAssetRetirementStatus.QuotaReleased, (await handler.Handle(query, CancellationToken.None)).Status);
        clock.Advance(TimeSpan.FromTicks(1));
        var expired = await handler.Handle(query, CancellationToken.None);
        Assert.Equal(TemplateAssetRetirementStatus.ReplayWindowExpired, expired.Status);
        Assert.Equal(decision.Id.Id, expired.DecisionId);
        Assert.Null(expired.Checksum);
        Assert.Equal(0, wire.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pending")]
    [InlineData("quota-released")]
    [InlineData("execution-outcome-unknown")]
    [InlineData("replay-window-expired")]
    public async Task Reads_authoritative_state_and_only_fetches_metadata_before_a_decision(string? state)
    {
        using var db = CreateDb();
        var template = LabelTemplate.Create("org", "env", "TPL", "模板", "file", "{}", "inactive");
        db.LabelTemplates.Add(template);
        var decision = TemplateAssetRetirementDecision.Create("org", "env", template.Id, "TPL", "file", Checksum,
            "idempotency", "sensitive-subject", TemplateAssetRetirementDecision.RequiredPermission, "sensitive-reason", "trace");
        if (state is not null)
        {
            if (state == "replay-window-expired")
                db.TemplateAssetRetirementReplayFences.Add(new TemplateAssetRetirementReplayFence(decision, Now));
            else
            {
                db.TemplateAssetRetirementDecisions.Add(decision);
                db.Entry(decision).Property(x => x.Status).CurrentValue = state;
                // A current template can move to another file while its retained decision remains queryable.
                template.Update("模板", "new-file", "{}", "inactive");
            }
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var wire = new MetadataHandler(state is null);
        using var http = new HttpClient(wire) { BaseAddress = new Uri("https://file-storage.invalid") };
        using var adapter = new HttpFileStorageLabelTemplateAssetAdapter(new HttpFileStorageClient(http), http, TimeSpan.FromSeconds(1));
        var query = new GetTemplateAssetRetirementQuery("org", "env", template.Id, "file");
        var handler = new GetTemplateAssetRetirementQueryHandler(db, adapter, new FakeTimeProvider(Now));
        var result = await handler.Handle(query, CancellationToken.None);
        Assert.Equal(result, await handler.Handle(query, CancellationToken.None));
        Assert.Equal("file", result.FileId);
        Assert.Equal(state is null ? null : decision.Id.Id, result.DecisionId);
        Assert.Equal(state == "replay-window-expired" ? null : Checksum, result.Checksum);
        Assert.Equal(state, result.Status is null ? null : JsonSerializer.Serialize(result.Status).Trim('"'));
        Assert.Equal(state is null ? 2 : 0, wire.Calls);
        Assert.Empty(db.ChangeTracker.Entries());
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("sensitive", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Idempotency", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other-org", "env", "file", false)]
    [InlineData("org", "other-env", "file", false)]
    [InlineData("org", "env", "other-file", false)]
    [InlineData("org", "env", "file", true)]
    public async Task Rejects_wrong_tenant_asset_or_template_before_metadata(
        string organization, string environment, string fileId, bool otherTemplate)
    {
        using var db = CreateDb();
        var template = LabelTemplate.Create("org", "env", "TPL", "模板", "file", "{}", "inactive");
        db.LabelTemplates.Add(template);
        await db.SaveChangesAsync();
        using var wire = new MetadataHandler(false);
        using var http = new HttpClient(wire) { BaseAddress = new Uri("https://file-storage.invalid") };
        using var adapter = new HttpFileStorageLabelTemplateAssetAdapter(new HttpFileStorageClient(http), http, TimeSpan.FromSeconds(1));
        var handler = new GetTemplateAssetRetirementQueryHandler(db, adapter, new FakeTimeProvider(Now));
        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(new GetTemplateAssetRetirementQuery(
            organization, environment, otherTemplate ? new LabelTemplateId(Guid.NewGuid()) : template.Id, fileId), CancellationToken.None));
        Assert.Equal(0, wire.Calls);
    }

    [Theory]
    [InlineData("other-org", "env")]
    [InlineData("org", "other-env")]
    public async Task Foreign_fence_cannot_bypass_metadata_ownership_validation(string ownerOrganization, string ownerEnvironment)
    {
        using var db = CreateDb();
        var template = LabelTemplate.Create("org", "env", "TPL", "模板", "file", "{}", "inactive");
        var foreign = TemplateAssetRetirementDecision.Create(ownerOrganization, ownerEnvironment,
            new LabelTemplateId(Guid.NewGuid()), "TPL", "file", Checksum,
            "foreign-key", "user", TemplateAssetRetirementDecision.RequiredPermission, "reason", "trace");
        db.LabelTemplates.Add(template);
        db.TemplateAssetRetirementReplayFences.Add(new TemplateAssetRetirementReplayFence(foreign, Now));
        await db.SaveChangesAsync();
        using var wire = new MetadataHandler(true, ownerOrganization, ownerEnvironment);
        using var http = new HttpClient(wire) { BaseAddress = new Uri("https://file-storage.invalid") };
        using var adapter = new HttpFileStorageLabelTemplateAssetAdapter(new HttpFileStorageClient(http), http, TimeSpan.FromSeconds(1));
        var handler = new GetTemplateAssetRetirementQueryHandler(db, adapter, new FakeTimeProvider(Now));
        await Assert.ThrowsAsync<InvalidDataException>(() => handler.Handle(
            new GetTemplateAssetRetirementQuery("org", "env", template.Id, "file"), CancellationToken.None));
        Assert.Equal(1, wire.Calls);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, null!);

    private sealed class MetadataHandler(bool allowed, string organization = "org", string environment = "env") : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.True(allowed, "Retained retirement facts must not request FileStorage.");
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/files/v1/files/file", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new FileMetadataResponse("file", organization, environment,
                    new OwnerReference("business-barcode-label", "label-template", "TPL"), "barcode-label-template",
                    "template.json", "application/vnd.nerv-iip.label-template+json", 10, Checksum, "available", Now, Now)),
            });
        }
    }
}
