using FastEndpoints;
using Nerv.IIP.Business.ProductEngineering.Domain;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.CodeAnalysis;

namespace Nerv.IIP.Business.ProductEngineering.Web.Endpoints.Diagnostics;

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
            typeof(ProductEngineeringFacts).Assembly);
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(analysis, "Business ProductEngineering Code Analysis", 1000, 200, false, []);

        HttpContext.Response.ContentType = "text/html";
        await HttpContext.Response.WriteAsync(html, ct);
    }
}
