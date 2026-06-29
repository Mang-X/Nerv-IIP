using FastEndpoints;
using Nerv.IIP.Caching;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Roles;
using Nerv.IIP.Iam.Web.Application.Seed;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Nerv.IIP.Iam.Web.Application.Sessions;
using Nerv.IIP.Iam.Web.Application.Users;
using Nerv.IIP.Iam.Web.Endpoints;
using Nerv.IIP.Localization;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DependencyInjection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var usesPostgreSql = string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
builder.Services.AddFastEndpoints();
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
    configuration.AddKnownExceptionValidationBehavior();
    if (usesPostgreSql)
    {
        configuration.AddUnitOfWorkBehaviors();
    }
});
builder.Services.AddNervIipCaching(builder.Configuration, "iam");
builder.Services.AddNervIipObservability(builder.Configuration, "iam");
builder.Services.AddNervIipLocalization();
builder.Services.AddNervIipInternalServiceAuthorization(builder.Configuration, builder.Environment);
builder.Services.AddIamPersistence(builder.Configuration);
builder.Services.Configure<IamSeedOptions>(builder.Configuration.GetSection("Iam:Seed"));
builder.Services.Configure<EnterpriseIdentityOptions>(builder.Configuration.GetSection("Iam:EnterpriseIdentity"));
builder.Services
    .AddOptions<IamAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection("Iam:Authentication"))
    .Validate(options => options.FailedLoginLockoutThreshold > 0, "Iam:Authentication:FailedLoginLockoutThreshold must be positive.")
    .Validate(options => options.FailedLoginLockoutMinutes > 0, "Iam:Authentication:FailedLoginLockoutMinutes must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton<IMfaChallengeStore, InMemoryMfaChallengeStore>();
builder.Services.AddScoped<IamPasswordService>();
builder.Services.AddSingleton<IamTokenService>();
if (usesPostgreSql)
{
    builder.Services.AddScoped<IIamAuthService, PostgreSqlIamAuthService>();
    builder.Services.AddScoped<IIamPermissionAuthorizer, IamPermissionAuthorizer>();
    builder.Services.AddScoped<IIamUserApplicationService, PostgreSqlIamUserApplicationService>();
    builder.Services.AddScoped<IIamRoleApplicationService, PostgreSqlIamRoleApplicationService>();
    builder.Services.AddScoped<IIamSessionApplicationService, PostgreSqlIamSessionApplicationService>();
    builder.Services.AddScoped<ISecurityAuditRecorder, SecurityAuditRecorder>();
    builder.Services.AddScoped<IIamSecurityAuditApplicationService, PostgreSqlIamSecurityAuditApplicationService>();
}
else
{
    builder.Services.AddSingleton<IInMemoryIamAccessTokenIssuer, InMemoryIamAccessTokenIssuer>();
    builder.Services.AddScoped<IIamAuthService, InMemoryIamAuthService>();
    builder.Services.AddScoped<IIamPermissionAuthorizer, InMemoryIamPermissionAuthorizer>();
    builder.Services.AddScoped<IIamUserApplicationService, InMemoryIamUserApplicationService>();
    builder.Services.AddScoped<IIamRoleApplicationService, InMemoryIamRoleApplicationService>();
    builder.Services.AddScoped<IIamSessionApplicationService, InMemoryIamSessionApplicationService>();
    builder.Services.AddScoped<IIamSecurityAuditApplicationService, InMemoryIamSecurityAuditApplicationService>();
}
builder.Services.AddScoped<IamSeedService>();

var autoMigrate = string.Equals(builder.Configuration["Persistence:AutoMigrate"], "true", StringComparison.OrdinalIgnoreCase);

if (usesPostgreSql && autoMigrate && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for IAM in Development. Use an explicit migrator, release script or migration bundle outside Development.");
}

if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["Iam:Jwt:SigningKeys:0:PrivateKeyPem"]))
{
    throw new InvalidOperationException("Iam:Jwt:SigningKeys:0:PrivateKeyPem is required outside Development.");
}
if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["Iam:Jwt:SigningKeys:0:Kid"]))
{
    throw new InvalidOperationException("Iam:Jwt:SigningKeys:0:Kid is required outside Development.");
}
if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["Iam:Secrets:Pepper"]))
{
    throw new InvalidOperationException("Iam:Secrets:Pepper is required outside Development.");
}
var configuredAccessTokenMinutes = builder.Configuration.GetValue("Iam:Jwt:AccessTokenMinutes", 15);
if (!builder.Environment.IsDevelopment()
    && (configuredAccessTokenMinutes < 1 || configuredAccessTokenMinutes > 60))
{
    throw new InvalidOperationException("Iam:Jwt:AccessTokenMinutes must be between 1 and 60 outside Development.");
}
var enterpriseIdentityOptions = builder.Configuration
    .GetSection("Iam:EnterpriseIdentity")
    .Get<EnterpriseIdentityOptions>() ?? new EnterpriseIdentityOptions();
if (!builder.Environment.IsDevelopment()
    && string.Equals(enterpriseIdentityOptions.Mfa.DevelopmentCode, "000000", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Iam:EnterpriseIdentity:Mfa:DevelopmentCode must be overridden outside Development.");
}
if (!builder.Environment.IsDevelopment() && !usesPostgreSql)
{
    throw new InvalidOperationException("Persistence:Provider=PostgreSQL is required for IAM outside Development.");
}

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseAuthentication();
app.UseAuthorization();
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
