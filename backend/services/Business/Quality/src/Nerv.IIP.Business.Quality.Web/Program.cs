using System.Reflection;
using System.Text.Json;
using DotNetCore.CAP;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.CodeAnalysis;
using NetCorePal.Extensions.NewtonsoftJson;
using Newtonsoft.Json;
using Prometheus;
using Serilog;
using Serilog.Formatting.Json;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .Enrich.WithClientIp()
    .WriteTo.Console(new JsonFormatter())
    .CreateLogger();

var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    isTesting = builder.Environment.IsEnvironment("Testing");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc()
        .AddNewtonsoftJson(options => { options.SerializerSettings.AddNetCorePalJsonConverters(); });
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();

    if (isTesting)
    {
        builder.Services.AddDataProtection();
    }
    else
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(builder.Configuration.GetConnectionString("Redis")!);
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => redis);
        builder.Services.AddDataProtection().PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");
    }

    builder.Services.AddAuthentication().AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidAudience = "netcorepal";
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidIssuer = "netcorepal";
        options.TokenValidationParameters.ValidateIssuer = true;
    });

    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business Quality";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.AddNetCorePalJsonConverters());
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();

    var qualityConnectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(qualityConnectionString))
    {
        qualityConnectionString = "Host=localhost;Database=nerv_iip_quality_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddQualityPostgreSqlPersistence(qualityConnectionString, builder.Environment.IsDevelopment());
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IQualityIntegrationEventContextAccessor, HttpQualityIntegrationEventContextAccessor>();
    builder.Services.AddScoped<INonconformanceReportCodeGenerator, NonconformanceReportCodeGenerator>();
    builder.Services.AddContext().AddEnvContext().AddCapContextProcessor();
    builder.Services.AddNetCorePalServiceDiscoveryClient();
    if (isTesting)
    {
        builder.Services.AddIntegrationEvents(typeof(Program));
    }
    else
    {
        builder.Services.AddIntegrationEvents(typeof(Program))
            .UseCap<ApplicationDbContext>(b =>
            {
                b.RegisterServicesFromAssemblies(typeof(Program));
                b.AddContextIntegrationFilters();
            });

        builder.Services.AddCap(x =>
        {
            x.UseNetCorePalStorage<ApplicationDbContext>();
            x.JsonSerializerOptions.AddNetCorePalJsonConverters();
            x.UseConfiguredTransport(builder.Configuration);
            x.UseDashboard();
        });
    }

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
            .AddCommandLockBehavior()
            .AddKnownExceptionValidationBehavior()
            .AddUnitOfWorkBehaviors());

    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = QualityFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    if (!isTesting)
    {
        builder.Services.AddHangfire(x => { x.UseRedisStorage(builder.Configuration.GetConnectionString("Redis")); });
        builder.Services.AddHangfireServer();
    }

    var app = builder.Build();
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    app.UseNervIipRequestLocalization();
    app.UseKnownExceptionHandler();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Endpoints.NameGenerator = ctx =>
            QualityEndpointContracts.TryGet(ctx.EndpointType, out var contract)
                ? contract.OperationId
                : ToLowerCamelEndpointName(ctx.EndpointType.Name);
    }).UseSwaggerGen();
    app.UseHttpMetrics();
    app.MapHealthChecks("/health");
    app.MapMetrics();
    app.MapGet("/code-analysis", () =>
    {
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(
            CodeFlowAnalysisHelper.GetResultFromAssemblies(typeof(Program).Assembly,
                typeof(ApplicationDbContext).Assembly,
                typeof(QualityFacts).Assembly));
        return Results.Content(html, "text/html; charset=utf-8");
    });

    if (!isTesting)
    {
        app.UseHangfireDashboard();
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    if (isTesting)
    {
        throw;
    }

    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
