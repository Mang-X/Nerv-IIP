using System.Net;
using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Caching;
using Nerv.IIP.Localization;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Business Gateway";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipCaching(builder.Configuration, "business-gateway");
builder.Services.AddNervIipObservability(builder.Configuration, "business-gateway");
builder.Services.AddNervIipLocalization();
builder.Services.AddNervIipInternalServiceTokenProvider(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = BusinessGatewayOperationIdConvention.Generate;
}).UseSwaggerGen();
app.Run();

public partial class Program;
