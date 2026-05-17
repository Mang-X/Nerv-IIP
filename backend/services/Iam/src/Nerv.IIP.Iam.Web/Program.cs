using FastEndpoints;
using Nerv.IIP.Caching;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddNervIipCaching(builder.Configuration, "iam");
builder.Services.AddNervIipObservability(builder.Configuration, "iam");
builder.Services.AddIamPersistence(builder.Configuration);
builder.Services.Configure<IamSeedOptions>(builder.Configuration.GetSection("Iam:Seed"));
builder.Services.AddScoped<IamPasswordService>();
builder.Services.AddScoped<IamTokenService>();
builder.Services.AddScoped<IamAuthService>();
builder.Services.AddScoped<IamSeedService>();

var usesPostgreSql = string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
var autoMigrate = string.Equals(builder.Configuration["Persistence:AutoMigrate"], "true", StringComparison.OrdinalIgnoreCase);

if (usesPostgreSql && autoMigrate && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for IAM in Development. Use an explicit migrator, release script or migration bundle outside Development.");
}

if (usesPostgreSql
    && !builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["Iam:Jwt:SigningKey"]))
{
    throw new InvalidOperationException("Iam:Jwt:SigningKey is required for PostgreSQL persistence outside Development.");
}

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();

if (usesPostgreSql && autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var migrationRunner = scope.ServiceProvider.GetRequiredService<IamDatabaseMigrationRunner>();
    await migrationRunner.MigrateAsync();
    var seed = scope.ServiceProvider.GetRequiredService<IamSeedService>();
    await seed.SeedAsync();
}

app.Run();

public partial class Program;
