using FastEndpoints;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.CodeAnalysis;

namespace Nerv.IIP.Business.MasterData.Web.Endpoints.Diagnostics;

public sealed class CodeAnalysisEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/code-analysis");
        Policies(InternalServiceAuthorizationPolicy.Name);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var analysis = CodeFlowAnalysisHelper.GetResultFromAssemblies(
            typeof(Program).Assembly,
            typeof(ApplicationDbContext).Assembly,
            typeof(MasterDataFacts).Assembly);
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(analysis, "Business MasterData Code Analysis", 1000, 200, false, []);

        HttpContext.Response.ContentType = "text/html";
        await HttpContext.Response.WriteAsync(html, ct);
    }
}
