using DotNetCore.CAP;
using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Localization;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application;
using Nerv.IIP.Notification.Web.Application.IntegrationEvents;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var usePostgreSql = string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
if (usePostgreSql && autoMigrate && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for Notification in Development. Use an explicit migrator, release script or migration bundle outside Development.");
}

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Notification";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
    configuration.AddKnownExceptionValidationBehavior();
    configuration.AddUnitOfWorkBehaviors();
});

if (usePostgreSql)
{
    builder.Services.AddContext();
    builder.Services.AddEnvContext("X-Environment-Id");
    builder.Services.AddCapContextProcessor();
    builder.Services.AddIntegrationEvents(typeof(Program)).UseCap<ApplicationDbContext>(_ => { });
    builder.Services.AddCap(options =>
    {
        options.UseNetCorePalStorage<ApplicationDbContext>();
        options.UseRabbitMQ(rabbitMqOptions =>
        {
            rabbitMqOptions.HostName = builder.Configuration["RabbitMQ:HostName"] ?? "localhost";
            rabbitMqOptions.Port = ReadRabbitMqPort(builder.Configuration);
            rabbitMqOptions.UserName = builder.Configuration["RabbitMQ:UserName"] ?? "guest";
            rabbitMqOptions.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        });
    });
}
else
{
    builder.Services.AddIntegrationEvents(typeof(Program));
    builder.Services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
}
builder.Services.AddNotificationPersistence(builder.Configuration);
builder.Services.AddNervIipObservability(builder.Configuration, "notification");
builder.Services.AddNervIipLocalization();

var app = builder.Build();
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

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

static int ReadRabbitMqPort(IConfiguration configuration)
{
    return int.TryParse(configuration["RabbitMQ:Port"], out var port) && port > 0 ? port : 5672;
}

public partial class Program;
