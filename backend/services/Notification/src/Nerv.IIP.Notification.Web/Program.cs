using DotNetCore.CAP;
using FastEndpoints;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application;
using Nerv.IIP.Notification.Web.Application.IntegrationEvents;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var usePostgreSql = string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);

builder.Services.AddFastEndpoints();
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

var app = builder.Build();
if (usePostgreSql)
{
    app.UseContext();
}
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();
app.Run();

static int ReadRabbitMqPort(IConfiguration configuration)
{
    return int.TryParse(configuration["RabbitMQ:Port"], out var port) && port > 0 ? port : 5672;
}

public partial class Program;
