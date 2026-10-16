using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

// DomainInvariant/PublicContract: #4448 / #4405 r1; InMemory proves mapping, not PostgreSQL behavior.
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class SchedulingTimeContextTests
{
    private static SchedulingTimeContextRequest Request() => new("org-001", "env-dev", "plan-1", "planner", ["WC-2"]);

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("expired", false)]
    [InlineData("tampered", false)]
    public async Task Owner_requires_user_scope_proof_in_addition_to_internal_token(string mode, bool allowed)
    {
        await using var factory = new TimeContextFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "time-context-internal-test");
        var request = Request();
        var proof = SchedulingTimeContextAuthorization.Sign("test-key", request,
            mode == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-6) : DateTimeOffset.UtcNow);
        if (mode != "missing") client.DefaultRequestHeaders.Add(SchedulingTimeContextAuthorization.HeaderName, proof);
        if (mode == "tampered") request = request with { PlanId = "other-plan" };
        using var response = await client.PostAsJsonAsync("/api/business/v1/master-data/scheduling-time-context/resolve", request);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (allowed) Assert.Contains("WC-2", await response.Content.ReadAsStringAsync());
    }

    private sealed class TimeContextFactory : WebApplicationFactory<Program>
    {
        private readonly string database = Guid.NewGuid().ToString();
        private readonly ServiceProvider efServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "time-context-internal-test");
            builder.UseSetting(SchedulingTimeContextAuthorization.ConfigurationKey, "test-key");
            builder.ConfigureTestServices(services => {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database).UseInternalServiceProvider(efServices));
            });
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) efServices.Dispose(); }
    }

    [Fact]
    public void Signed_authorization_binds_subject_tenant_plan_and_complete_work_center_set()
    {
        var request = Request();
        var issued = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var proof = SchedulingTimeContextAuthorization.Sign("key", request, issued);
        Assert.True(SchedulingTimeContextAuthorization.Verify("key", request, proof, issued));
        foreach (var changed in new[] { request with { OrganizationId = "other" }, request with { EnvironmentId = "other" },
            request with { PlanId = "other" }, request with { PrincipalId = "other" }, request with { WorkCenterCodes = ["WC-1"] } })
            Assert.False(SchedulingTimeContextAuthorization.Verify("key", changed, proof, issued));
        Assert.False(SchedulingTimeContextAuthorization.Verify("", request, proof, issued));
        Assert.False(SchedulingTimeContextAuthorization.Verify("wrong-key", request, proof, issued));
        Assert.False(SchedulingTimeContextAuthorization.Verify("key", request, proof, issued.AddMinutes(6)));
        Assert.False(SchedulingTimeContextAuthorization.Verify("key", request, proof, issued.AddMinutes(-6)));
        Assert.False(SchedulingTimeContextAuthorization.Verify("key", request, "", issued));
    }

    [Fact]
    public async Task Maps_real_work_center_plant_and_same_tenant_site_only()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, null!);
        db.Sites.AddRange(Site.Create("org-001", "env-dev", "SITE-1", "上海工厂", "Asia/Shanghai"),
            Site.Create("org-001", "env-dev", "SITE-2", "伦敦工厂", "Europe/London"),
            Site.Create("other", "env-dev", "SITE-2", "其它组织", "UTC"));
        db.WorkCenters.AddRange(
            WorkCenter.CreateResource("org-001", "env-dev", "WC-2", "装配", 480, "work-center", "SITE-2", "", null, "STANDARD", "minute", true),
            WorkCenter.CreateResource("org-001", "env-dev", "WC-1", "机加", 480, "work-center", "SITE-1", "", null, "STANDARD", "minute", true));
        await db.SaveChangesAsync();
        var result = await SchedulingTimeContextQuery.ResolveAsync(db, Request() with { WorkCenterCodes = ["WC-2", "MISSING"] }, CancellationToken.None);
        var mapped = Assert.Single(result.Where(x => x.WorkCenterCode == "WC-2"));
        Assert.Equal("SITE-2", mapped.PlantCode);
        Assert.Equal("Europe/London", mapped.Timezone);
        Assert.Null(mapped.UnresolvedReason);
        Assert.Null(Assert.Single(result.Where(x => x.WorkCenterCode == "MISSING")).Timezone);
        Assert.DoesNotContain(result, x => x.WorkCenterCode == "WC-1");
    }
}
