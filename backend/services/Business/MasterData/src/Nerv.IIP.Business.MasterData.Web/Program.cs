using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prometheus;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using FluentValidation.AspNetCore;
using Nerv.IIP.Business.MasterData.Web.Extensions;
using FastEndpoints;
using FastEndpoints.Swagger;
using Serilog;
using Serilog.Formatting.Json;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.AspNetCore.Http.Json;
using Newtonsoft.Json;
using NetCorePal.Extensions.CodeAnalysis;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;

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

    #region SignalR

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc()
        .AddNewtonsoftJson(options => { options.SerializerSettings.AddNetCorePalJsonConverters(); });

    #endregion

    #region Prometheus监控

    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName)
        .UseHttpClientMetrics();

    #endregion

    // Add services to the container.

    #region 身份认证

    if (isTesting)
    {
        builder.Services.AddDataProtection();
    }
    else
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(builder.Configuration.GetConnectionString("Redis")!);
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => redis);

        // DataProtection - use custom extension that resolves IConnectionMultiplexer from DI
        builder.Services.AddDataProtection()
            .PersistKeysToStackExchangeRedis("DataProtection-Keys");
    }

    builder.Services.AddAuthentication().AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidAudience = "netcorepal";
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidIssuer = "netcorepal";
        options.TokenValidationParameters.ValidateIssuer = true;
    });

    #endregion

    #region Controller

    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    #endregion

    #region FastEndpoints

    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business MasterData";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o =>
        o.SerializerOptions.AddNetCorePalJsonConverters());

    #endregion

    #region 模型验证器

    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();

    #endregion

    #region 基础设施

    var masterDataConnectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(masterDataConnectionString))
    {
        masterDataConnectionString = "Host=localhost;Database=nerv_iip_masterdata_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddMasterDataPostgreSqlPersistence(
        masterDataConnectionString,
        builder.Environment.IsDevelopment());
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IMasterDataIntegrationEventContextAccessor, HttpMasterDataIntegrationEventContextAccessor>();
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
            x.UseRabbitMQ(p => builder.Configuration.GetSection("RabbitMQ").Bind(p));
            x.UseDashboard(); //CAP Dashboard  path：  /cap
        });
    }

    #endregion

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
            .AddCommandLockBehavior()
            .AddKnownExceptionValidationBehavior()
            .AddUnitOfWorkBehaviors());

    #region 多环境支持与服务注册发现

    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = "BusinessMasterData")
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    #endregion

    #region Jobs

    if (!isTesting)
    {
        builder.Services.AddHangfire(x => { x.UseRedisStorage(builder.Configuration.GetConnectionString("Redis")); });
        builder.Services.AddHangfireServer(); //hangfire dashboard  path：  /hangfire
    }

    #endregion


    var app = builder.Build();
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    app.UseKnownExceptionHandler();
    // Configure the HTTP request pipeline.
    app.UseStaticFiles();
    //app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Endpoints.NameGenerator = ctx =>
            MasterDataEndpointContracts.TryGet(ctx.EndpointType, out var contract)
                ? contract.OperationId
                : ToLowerCamelEndpointName(ctx.EndpointType.Name);
    }).UseSwaggerGen();

    app.UseHttpMetrics();
    app.MapHealthChecks("/health");
    app.MapMetrics(); // 通过   /metrics  访问指标

    // Code analysis endpoint
    app.MapGet("/code-analysis", () =>
    {
        var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(
            CodeFlowAnalysisHelper.GetResultFromAssemblies(typeof(Program).Assembly,
                typeof(ApplicationDbContext).Assembly,
                typeof(MasterDataFacts).Assembly)
        );
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
