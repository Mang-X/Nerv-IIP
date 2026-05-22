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
    public override async Task HandleAsync(CancellationToken ct)
    {
        var analysis = CodeFlowAnalysisHelper.GetResultFromAssemblies(
        [
            typeof(Program).Assembly,
            typeof(ApplicationDbContext).Assembly,
            typeof(OperationTask).Assembly
        ]);
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(analysis, "Ops Code Analysis", 1000, 200, false, []);

        HttpContext.Response.ContentType = "text/html";
        await HttpContext.Response.WriteAsync(html, ct);
    }
}
