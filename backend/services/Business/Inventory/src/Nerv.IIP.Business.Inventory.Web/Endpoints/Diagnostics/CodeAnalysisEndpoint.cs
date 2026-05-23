using FastEndpoints;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.CodeAnalysis;

namespace Nerv.IIP.Business.Inventory.Web.Endpoints.Diagnostics;

[HttpGet("/code-analysis")]
public sealed class CodeAnalysisEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var analysis = CodeFlowAnalysisHelper.GetResultFromAssemblies(
        [
            typeof(Program).Assembly,
            typeof(ApplicationDbContext).Assembly,
            typeof(StockLedger).Assembly,
        ]);
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(analysis, "Business Inventory Code Analysis", 1000, 200, false, []);

        HttpContext.Response.ContentType = "text/html";
        await HttpContext.Response.WriteAsync(html, ct);
    }
}
