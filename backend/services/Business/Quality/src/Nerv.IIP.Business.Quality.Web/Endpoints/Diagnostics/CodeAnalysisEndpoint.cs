using FastEndpoints;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.CodeAnalysis;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.Diagnostics;

public sealed class CodeAnalysisEndpoint : EndpointWithoutRequest
{
    private const int VisualizationCanvasWidth = 1000;
    private const int VisualizationCanvasHeight = 200;
    private const string HtmlContentType = "text/html; charset=utf-8";

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
            typeof(QualityFacts).Assembly);
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(
            analysis,
            "Business Quality Code Analysis",
            VisualizationCanvasWidth,
            VisualizationCanvasHeight,
            false,
            []);

        HttpContext.Response.ContentType = HtmlContentType;
        await HttpContext.Response.WriteAsync(html, ct);
    }
}
