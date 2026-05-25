using System.Net;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.Extensions.Http.Resilience;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
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
builder.Services.Configure<BusinessGatewayAuthorizationOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AcceptLanguageForwardingHandler>();
builder.Services.AddHttpClient<IBusinessGatewayAuthorizationClient, HttpBusinessGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IBusinessMasterDataClient, HttpBusinessMasterDataClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["MasterData:BaseUrl"] ?? "http://localhost:5107");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IBusinessInventoryClient, HttpBusinessInventoryClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Inventory:BaseUrl"] ?? "http://localhost:5109");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IBusinessQualityClient, HttpBusinessQualityClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Quality:BaseUrl"] ?? "http://localhost:5110");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IBusinessMesClient, HttpBusinessMesClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Mes:BaseUrl"] ?? "http://localhost:5111");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
builder.Services.AddBusinessGatewayAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = BusinessGatewayOperationIdConvention.Generate;
}).UseSwaggerGen();
app.Run();

public partial class Program;
