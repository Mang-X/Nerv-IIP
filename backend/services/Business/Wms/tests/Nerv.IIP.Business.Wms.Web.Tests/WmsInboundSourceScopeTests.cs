using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Application.Queries;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsInboundSourceScopeTests
{
    // PublicContract: #4255 / #3825 r1. Union filtering precedes search, total and paging.
    [Fact]
    public async Task Receipt_source_union_includes_unassigned_and_other_operators_before_search_and_paging()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var serviceScope = provider.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.InboundOrders.AddRange(
            Order("IN-2026-A", "SITE-A"), Order("IN-2026-B", "SITE-B", "other"),
            Order("IN-2026-C", "SITE-C"), Order("IN-2026-D", "SITE-A", organization: "other-org"),
            Order("IN-2026-E", "SITE-B", environment: "other-env"), Order("IN-2025-A", "SITE-A"));
        await db.SaveChangesAsync();
        var authorizer = new WarehouseWorkScopeAuthorizer(db, TimeProvider.System);
        var request = new WarehouseWorkScopeRequest("org-001", "env-dev", "actor", ["SITE-A", "SITE-B"], "authorized-sites", "all", null);
        var selection = await authorizer.ResolveReceiptListAsync(request, CancellationToken.None);
        var handler = new ListInboundOrdersQueryHandler(db);
        async Task<ListInboundOrdersResponse> Read(int skip) => await handler.Handle(
            new ListInboundOrdersQuery("org-001", "env-dev", Skip: skip, Take: 1, Keyword: "IN-2026",
                SiteCodes: selection.SiteCodes, SiteWideScope: selection.SiteWide), CancellationToken.None);
        var first = await Read(0);
        var second = await Read(1);
        var repeated = await Read(0);
        Assert.Equal(2, first.Total);
        Assert.Equal(2, second.Total);
        Assert.Equal(first.Items.Select(x => x.InboundOrderNo), repeated.Items.Select(x => x.InboundOrderNo));
        Assert.Equal(["IN-2026-A", "IN-2026-B"], first.Items.Concat(second.Items).Select(x => x.InboundOrderNo).Order(StringComparer.Ordinal));
        Assert.Empty((await Read(2)).Items);
        var narrowed = await authorizer.ResolveReceiptListAsync(request with { SiteCode = "SITE-B" }, CancellationToken.None);
        var site = await handler.Handle(new ListInboundOrdersQuery("org-001", "env-dev", SiteCodes: narrowed.SiteCodes,
            SiteWideScope: narrowed.SiteWide), CancellationToken.None);
        Assert.Equal("IN-2026-B", Assert.Single(site.Items).InboundOrderNo);
    }

    [Fact]
    public async Task Receipt_union_rejects_missing_grants_and_explicit_unauthorized_site_and_is_not_a_work_scope()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var authorizer = new WarehouseWorkScopeAuthorizer(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), TimeProvider.System);
        var request = new WarehouseWorkScopeRequest("org-001", "env-dev", "actor", ["SITE-A"], "authorized-sites", "all", null);
        await Assert.ThrowsAsync<WmsAuthorizationException>(() => authorizer.ResolveReceiptListAsync(request with { AuthorizedSiteCodes = [] }, CancellationToken.None));
        await Assert.ThrowsAsync<WmsAuthorizationException>(() => authorizer.ResolveReceiptListAsync(request with { SiteCode = "SITE-C" }, CancellationToken.None));
        await Assert.ThrowsAsync<WmsAuthorizationException>(() => authorizer.ResolveAsync(request, CancellationToken.None));
    }

    private static InboundOrder Order(string number, string site, string? actor = null,
        string organization = "org-001", string environment = "env-dev") => InboundOrder.Create(
            organization, environment, number, "asn", "ASN-2026", site,
            [new InboundOrderLineDraft("1", "SKU-RM-1000", "kg", 5m, "RECV-01", "LOT-2026", null, "qualified", "company", "owner")],
            actor, actor is null ? null : "POOL-B");
}
