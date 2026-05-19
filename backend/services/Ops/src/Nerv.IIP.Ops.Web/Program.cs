using DotNetCore.CAP;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Observability;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Web.Application.Auth;
using Nerv.IIP.Ops.Web.Application.Commands;
using NetCorePal.Extensions.AspNetCore;
using Nerv.IIP.Ops.Web.Application.IntegrationEvents;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DependencyInjection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var usePostgreSql = string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
builder.Services.AddFastEndpoints();
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
builder.Services.AddOpsPersistence(builder.Configuration);
builder.Services.Configure<OpsConnectorCredentialOptions>(
    builder.Configuration.GetSection(OpsConnectorCredentialOptions.SectionName));
builder.Services.AddSingleton<ConfiguredOpsConnectorCredentialValidator>();
builder.Services.AddHttpClient<IamOpsConnectorCredentialValidator>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
builder.Services.AddTransient<IOpsConnectorCredentialValidator, OpsConnectorCredentialValidator>();
if (usePostgreSql)
{
    builder.Services.AddScoped<OpsDatabaseMigrationRunner>();
    builder.Services.AddScoped<IOperationTaskApplicationService, EfOperationTaskApplicationService>();
}
else
{
    builder.Services.AddSingleton<IOperationTaskApplicationService, InMemoryOperationTaskApplicationService>();
}
builder.Services.AddNervIipObservability(builder.Configuration, "ops");

var app = builder.Build();
if (usePostgreSql && builder.Configuration.GetValue<bool>("Persistence:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OpsDatabaseMigrationRunner>().MigrateAsync();
}

app.UseNervIipCorrelation();
if (usePostgreSql)
{
    app.UseContext();
}
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseFastEndpoints();
app.Run();

static int ReadRabbitMqPort(IConfiguration configuration)
{
    return int.TryParse(configuration["RabbitMQ:Port"], out var port) && port > 0 ? port : 5672;
}

public partial class Program;
