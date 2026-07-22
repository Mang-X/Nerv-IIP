using DotNetCore.CAP;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Web.Application.Auth;
using Nerv.IIP.Ops.Web.Application.Commands;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Persistence;
using NetCorePal.Extensions.AspNetCore;
using Nerv.IIP.Ops.Web.Application.IntegrationEvents;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using NetCorePal.Extensions.DependencyInjection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var persistence = PersistenceStartupGovernance.Resolve(
    builder.Configuration,
    builder.Environment,
    new PersistenceStartupRequirements("Ops", ["OpsDb"]));
var usePostgreSql = persistence.UsePostgreSql;

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Ops";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
    configuration.AddKnownExceptionValidationBehavior();
    if (usePostgreSql)
    {
        configuration.AddUnitOfWorkBehaviors();
    }
});
if (usePostgreSql)
{
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddContext();
    builder.Services.AddEnvContext("X-Environment-Id");
    builder.Services.AddCapContextProcessor();
    builder.Services.AddIntegrationEvents(typeof(Program))
        .UseCap<ApplicationDbContext>(b =>
        {
            b.RegisterServicesFromAssemblies(typeof(Program));
            b.AddContextIntegrationFilters();
        });
    builder.Services.AddCap(options =>
    {
        options.Version = builder.Configuration["Cap:Version"] ?? "v1";
        options.UseEntityFramework<ApplicationDbContext>();
        options.UseConfiguredTransport(builder.Configuration, builder.Environment.EnvironmentName);
    });
}
else
{
    builder.Services.AddIntegrationEvents(typeof(Program));
    builder.Services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
}
builder.Services.AddOpsPersistence(builder.Configuration, usePostgreSql);
builder.Services.Configure<OpsConnectorCredentialOptions>(
    builder.Configuration.GetSection(OpsConnectorCredentialOptions.SectionName));
builder.Services.Configure<OperationLeaseReaperOptions>(
    builder.Configuration.GetSection("Ops:LeaseReaper"));
builder.Services.AddSingleton<ConfiguredOpsConnectorCredentialValidator>();
var iamBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Iam:BaseUrl", "http://localhost:5102");
builder.Services.AddHttpClient<IamOpsConnectorCredentialValidator>(client =>
{
    client.BaseAddress = iamBaseAddress;
});
builder.Services.AddTransient<IOpsConnectorCredentialValidator, OpsConnectorCredentialValidator>();
if (usePostgreSql)
{
    builder.Services.AddScoped<OpsDatabaseMigrationRunner>();
    builder.Services.AddScoped<IOperationTaskApplicationService, EfOperationTaskApplicationService>();
    builder.Services.AddScoped<IOperationLeaseReaper, EfOperationLeaseReaper>();
    builder.Services.AddHostedService<OperationLeaseReaperHostedService>();
    builder.Services.AddScoped<IOperationTemplateApplicationService, EfOperationTemplateApplicationService>();
}
else
{
    builder.Services.AddSingleton<IOperationTaskApplicationService, InMemoryOperationTaskApplicationService>();
    builder.Services.AddSingleton<IOperationTemplateApplicationService, InMemoryOperationTemplateApplicationService>();
}
builder.Services.AddNervIipObservability(builder.Configuration, "ops");
builder.Services.AddNervIipLocalization();

var app = builder.Build();
if (usePostgreSql && persistence.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OpsDatabaseMigrationRunner>().MigrateAsync();
}

app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
if (usePostgreSql)
{
    app.UseContext();
}
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = ctx => ToLowerCamelEndpointName(ctx.EndpointType.Name);
}).UseSwaggerGen();
app.Run();

static Uri ResolveServiceBaseAddress(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    string configurationKey,
    string developmentFallback)
{
    var configuredBaseUrl = configuration[configurationKey];
    if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
    {
        return new Uri(configuredBaseUrl, UriKind.Absolute);
    }

    if (environment.IsDevelopment())
    {
        return new Uri(developmentFallback, UriKind.Absolute);
    }

    throw new InvalidOperationException($"{configurationKey} is required outside Development.");
}

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

public partial class Program;
