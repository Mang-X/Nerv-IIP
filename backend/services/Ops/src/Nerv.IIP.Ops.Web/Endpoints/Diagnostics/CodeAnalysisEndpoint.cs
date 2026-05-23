using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.CodeAnalysis;

namespace Nerv.IIP.Ops.Web.Endpoints.Diagnostics;

[HttpGet("/code-analysis")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class CodeAnalysisEndpoint : EndpointWithoutRequest
{
    private const int VisualizationCanvasWidth = 1000;
    private const int VisualizationCanvasHeight = 200;
    private const string HtmlContentType = "text/html; charset=utf-8";

    public override async Task HandleAsync(CancellationToken ct)
    {
        var analysis = CodeFlowAnalysisHelper.GetResultFromAssemblies(
        [
            typeof(Program).Assembly,
            typeof(ApplicationDbContext).Assembly,
            typeof(OperationTask).Assembly
        ]);
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(
            analysis,
            "Ops Code Analysis",
            VisualizationCanvasWidth,
            VisualizationCanvasHeight,
            false,
            []);

        HttpContext.Response.ContentType = HtmlContentType;
        await HttpContext.Response.WriteAsync(html, ct);
    }
}
