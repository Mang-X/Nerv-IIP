using DotNetCore.CAP;
using FastEndpoints;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;
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
builder.Services.AddNervIipCaching(builder.Configuration, "apphub");
builder.Services.AddNervIipObservability(builder.Configuration, "apphub");
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
builder.Services.AddAppHubPersistence(builder.Configuration);

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
