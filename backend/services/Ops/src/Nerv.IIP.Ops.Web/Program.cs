using DotNetCore.CAP;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Observability;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Web.Application.Commands;
using NetCorePal.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
var usePostgreSql = string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
builder.Services.AddFastEndpoints();
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
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
builder.Services.AddOpsPersistence(builder.Configuration);
if (usePostgreSql)
{
    builder.Services.AddScoped<IOperationTaskApplicationService, EfOperationTaskApplicationService>();
}
else
{
    builder.Services.AddSingleton<IOperationTaskApplicationService, InMemoryOperationTaskApplicationService>();
}
builder.Services.AddNervIipObservability(builder.Configuration, "ops");

var app = builder.Build();
if (usePostgreSql)
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
}

app.UseNervIipCorrelation();
if (usePostgreSql)
{
    app.UseContext();
}
app.UseFastEndpoints();
app.Run();

static int ReadRabbitMqPort(IConfiguration configuration)
{
    return int.TryParse(configuration["RabbitMQ:Port"], out var port) && port > 0 ? port : 5672;
}

public partial class Program;
