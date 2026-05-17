using FastEndpoints;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Infrastructure;
using NetCorePal.Extensions.CodeAnalysis;

namespace Nerv.IIP.AppHub.Web.Endpoints.Diagnostics;

[HttpGet("/code-analysis")]
[AllowAnonymous]
public sealed class CodeAnalysisEndpoint : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var analysis = CodeFlowAnalysisHelper.GetResultFromAssemblies(
        [
            typeof(Program).Assembly,
            typeof(ApplicationDbContext).Assembly,
            typeof(AppHubApplication).Assembly
        ]);
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(analysis, "AppHub Code Analysis", 1000, 200, false, []);

        HttpContext.Response.ContentType = "text/html";
        await HttpContext.Response.WriteAsync(html, ct);
    }
}
