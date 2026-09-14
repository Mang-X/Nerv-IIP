extern alias FileStorage;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;
using Nerv.IIP.Contracts.FileStorage;
using ProofVerifier = FileStorage::Nerv.IIP.FileStorage.Web.Application.Files.TemplateAssetRetirementProof;
using VerifierOptions = FileStorage::Nerv.IIP.FileStorage.Web.Application.Files.TemplateAssetRetirementOptions;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class TemplateAssetRetirementWireTests
{
    // #3045: fast lane owns real HTTP wire compatibility, not PostgreSQL acceptance/physical deletion.
    [Fact]
    public async Task Production_signer_and_client_reach_FileStorage_verifier_over_actual_http()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var options = new TemplateAssetRetirementExecutorOptions("synthetic-retirement-wire-key-3045"u8.ToArray(),
            "business-barcode-label", "file-storage", 2592000, 300, 300);
        var verifier = new ProofVerifier(new VerifierOptions(options.Key, options.Issuer, options.Audience,
            new(604800, 300, 300, 300)), clock);
        var accepted = 0;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.MapPost("/internal/file-storage/v1/template-asset-retirements", (RetireTemplateAssetRequest request) =>
        {
            var capability = verifier.Verify(request);
            if (capability is null) return Results.StatusCode(403);
            accepted++;
            Assert.Equal("模板甲", capability.OwnerId);
            Assert.Equal(2592000, capability.ClientWindowSeconds);
            return Results.Json(new RetireTemplateAssetResponse(capability.DecisionId, capability.FileId,
                "physical-hold", clock.GetUtcNow(), 2592000));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await app.StartAsync(timeout.Token);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var client = new TemplateAssetRetirementClient(http);
            var decision = TemplateAssetRetirementDecision.Create("org-retirement", "env-retirement",
                new LabelTemplateId(Guid.NewGuid()), "模板甲", "retirement-file", $"sha256:{new string('c', 64)}",
                "wire", "user", TemplateAssetRetirementDecision.RequiredPermission, "obsolete", "wire-test");
            // Fixture only: initialize the same frozen input consumed by the pure signer; no persistence claim.
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, null!);
            db.Entry(decision).Property(x => x.ReplayPolicyVersion).CurrentValue = 1;
            db.Entry(decision).Property(x => x.ClientWindowSeconds).CurrentValue = 2592000;
            db.Entry(decision).Property(x => x.ExecutorLeaseSeconds).CurrentValue = 300;
            db.Entry(decision).Property(x => x.ExecutorMaxBackoffSeconds).CurrentValue = 300;
            var signer = new TemplateAssetRetirementSigner(options, clock);
            var receipt = await client.SendAsync(signer.Sign(decision), timeout.Token);
            Assert.Equal(decision.Id.Id.ToString("D"), receipt.DecisionId);
            Assert.Equal(2592000, receipt.ReplayHorizonSeconds);
            var wrongKey = new TemplateAssetRetirementSigner(options with { Key = new byte[32] }, clock);
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(wrongKey.Sign(decision), timeout.Token));
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            var proof = signer.Sign(decision);
            error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(proof with { Payload = proof.Payload + "A" }, timeout.Token));
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            Assert.Equal(1, accepted);
        }
        finally { await app.StopAsync(timeout.Token); }
    }

    [Theory]
    [InlineData("Secret", "")]
    [InlineData("Secret", "bad")]
    [InlineData("Secret", "YQ==")]
    [InlineData("Issuer", "")]
    [InlineData("Audience", "")]
    [InlineData("LeaseSeconds", "0")]
    [InlineData("MaxBackoffSeconds", "-1")]
    [InlineData("ClientWindowSeconds", "0")]
    [InlineData("LeaseSeconds", "7776000")]
    public void Invalid_executor_configuration_fails_closed_without_disclosing_secret(string field, string value)
    {
        var prefix = TemplateAssetRetirementExecutorOptions.Section;
        var values = new Dictionary<string, string?>
        {
            [$"{prefix}:Secret"] = Convert.ToBase64String("synthetic-retirement-wire-key-3045"u8.ToArray()),
            [$"{prefix}:Issuer"] = "business-barcode-label", [$"{prefix}:Audience"] = "file-storage",
        };
        values[$"{prefix}:{field}"] = value;
        var error = Assert.Throws<InvalidOperationException>(() => TemplateAssetRetirementExecutorOptions.Load(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        Assert.Null(error.InnerException);
    }
}
