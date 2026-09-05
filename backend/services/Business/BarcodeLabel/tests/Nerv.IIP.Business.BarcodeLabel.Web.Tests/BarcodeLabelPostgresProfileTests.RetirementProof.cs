using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Contracts.BarcodeLabel;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed partial class BarcodeLabelPostgresProfileTests
{
    [RealPostgresFact]
    public async Task Retirement_http_proof_rejections_leave_zero_decisions_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var template = await SeedRetirementTemplateAsync();
        await using var factory = RetirementHttpFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "retirement-http-token");
        var request = RetirementProofCases.Request(template.Id.Id);
        foreach (var (name, invalid) in RetirementProofCases.InvalidRequests(request)
            .Append(("missing-proof", request with { Proof = "" })))
        {
            using var response = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, invalid);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"{name}: expected 403, actual {(int)response.StatusCode}");
            var error = await response.Content.ReadFromJsonAsync<TemplateAssetRetirementProofError>();
            Assert.Equal("template-asset-retirement-proof-invalid", error!.Code);
            await using var db = CreatePostgresDbContext(LaneConnectionString);
            Assert.Equal(0, await db.TemplateAssetRetirementDecisions.CountAsync());
        }
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, request);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await using var verification = CreatePostgresDbContext(LaneConnectionString);
        Assert.Equal(0, await verification.TemplateAssetRetirementDecisions.CountAsync());
    }

    [RealPostgresFact]
    public async Task Retirement_http_valid_proof_commits_authenticated_decision_on_postgres()
    {
        await ResetAndMigrateSchemaAsync();
        var template = await SeedRetirementTemplateAsync();
        await using var factory = RetirementHttpFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "retirement-http-token");
        var request = RetirementProofCases.Request(template.Id.Id);
        using var response = await client.PostAsJsonAsync(TemplateAssetRetirementProofV1.Route, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var decisionId = body.RootElement.GetProperty("data").GetProperty("decisionId").GetGuid();
        await using var verification = CreatePostgresDbContext(LaneConnectionString);
        var decision = await verification.TemplateAssetRetirementDecisions.SingleAsync();
        Assert.Equal(decisionId, decision.Id.Id);
        Assert.Equal("user-3042", decision.RequesterSubject);
        Assert.Equal("business.barcodes.template-assets.retire", decision.Permission);
        Assert.Equal(request.Reason, decision.Reason);
        Assert.Equal(request.Checksum, decision.TemplateAssetSha256);
        Assert.Equal(decision.Id, (await verification.LabelTemplates.SingleAsync()).RetiredCurrentFileByDecisionId);
    }

    private static async Task<LabelTemplate> SeedRetirementTemplateAsync()
    {
        var template = LabelTemplate.Create("org-3042", "env-3042", "TPL-3042", "退役入口测试",
            "file-3042", """{"version":1,"variables":[]}""", "inactive");
        await using var setup = CreatePostgresDbContext(LaneConnectionString);
        setup.LabelTemplates.Add(template);
        await setup.SaveChangesAsync();
        return template;
    }

    private static WebApplicationFactory<Program> RetirementHttpFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:PostgreSQL", LaneConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = LaneConnectionString,
                    ["InternalService:BearerToken"] = "retirement-http-token",
                }));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new RetirementProofCases.Clock()));
        });
}
