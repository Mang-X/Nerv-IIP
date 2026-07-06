using DotNetCore.CAP;
using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.ObservabilityAlerts;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application;
using Nerv.IIP.Notification.Web.Application.DeadLetters;
using Nerv.IIP.Notification.Web.Application.Health;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Notification.Web.Application.IntegrationEvents;
using Nerv.IIP.Notification.Web.Application.Notifications;
using Nerv.IIP.Notification.Web.Application.ObservabilityAlerts;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DistributedTransactions.CAP;
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
var healthChecks = builder.Services.AddHealthChecks();
if (usePostgreSql)
{
    healthChecks.AddCheck<NotificationDatabaseHealthCheck>("notification-db");
}
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
    configuration.AddKnownExceptionValidationBehavior();
    configuration.AddUnitOfWorkBehaviors();
});

if (usePostgreSql)
{
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddContext();
    builder.Services.AddEnvContext("X-Environment-Id");
    builder.Services.AddCapContextProcessor();
    builder.Services.AddIntegrationEvents(typeof(Program)).UseCap<ApplicationDbContext>(_ => { });
    builder.Services.AddCap(options =>
    {
        options.Version = builder.Configuration["Cap:Version"] ?? "v1";
        options.UseEntityFramework<ApplicationDbContext>();
        options.UseConfiguredTransport(builder.Configuration, builder.Environment.EnvironmentName);
        options.UseIntegrationEventDeadLetterOnFailedThreshold();
    });
}
else
{
    builder.Services.AddIntegrationEvents(typeof(Program));
    builder.Services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
}
builder.Services.AddNotificationPersistence(builder.Configuration);
builder.Services.Configure<NotificationDeliveryOptions>(
    builder.Configuration.GetSection("Notification:Delivery"));
builder.Services.Configure<NotificationDeadLetterAlertOptions>(
    builder.Configuration.GetSection(NotificationDeadLetterAlertOptions.SectionName));
builder.Services.Configure<ObservabilityAlertOptions>(
    builder.Configuration.GetSection(ObservabilityAlertOptions.SectionName));
builder.Services.AddHttpClient(ServiceHealthAlertProbe.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<INotificationDeliveryProvider, WeComDeliveryProvider>();
builder.Services.AddScoped<INotificationDeliveryProvider, DingTalkDeliveryProvider>();
builder.Services.AddScoped<INotificationDeliveryProvider, SmtpEmailDeliveryProvider>();
builder.Services.AddScoped<INotificationDeliveryProvider, WebhookDeliveryProvider>();
builder.Services.AddSingleton<NotificationChannelRateLimiter>();
builder.Services.AddScoped<NotificationDeliveryService>();
builder.Services.AddHostedService<NotificationDeliveryRetryWorker>();
builder.Services.Configure<OpsNotificationRecipientOptions>(
    builder.Configuration.GetSection(OpsNotificationRecipientOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddNervIipObservability(builder.Configuration, "notification");
builder.Services.AddNervIipLocalization();
if (usePostgreSql)
{
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
}
else
{
    builder.Services.AddSingleton<IIntegrationEventDeadLetterStore, InMemoryIntegrationEventDeadLetterStore>();
}
builder.Services.AddScoped<IntegrationEventCapFailureDeadLetterer>();
builder.Services.AddScoped<IntegrationEventDeadLetterReplayExecutor>();
builder.Services.AddScoped<IIntegrationEventDeadLetterReplayHandler, NotificationDeadLetterReplayHandler>();
builder.Services.AddScoped<NotificationDeadLetterAlertMonitor>();
builder.Services.AddHostedService<NotificationDeadLetterAlertWorker>();
builder.Services.AddSingleton<IDatabaseWatermarkReader, PostgreSqlDatabaseWatermarkReader>();
builder.Services.AddScoped<IObservabilityAlertProbe, ServiceHealthAlertProbe>();
builder.Services.AddScoped<IObservabilityAlertProbe, NotificationDeadLetterBacklogAlertProbe>();
builder.Services.AddScoped<IObservabilityAlertProbe, AppHubConnectorHeartbeatAlertProbe>();
builder.Services.AddScoped<IObservabilityAlertProbe, PostgreSqlWatermarkAlertProbe>();
builder.Services.AddSingleton<ObservabilityAlertMonitor>();
builder.Services.AddHostedService<ObservabilityAlertWorker>();
builder.Services.AddScoped<OperationTaskFailedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<OperationTaskCompletedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<OperationApprovalRequestedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<OperationApprovalApprovedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<OperationApprovalRejectedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<ApprovalStepOverdueIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<ApprovalStepResolvedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<ApprovalActionRecordedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<ScheduleConflictDetectedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<SchedulePlanInvalidatedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<AlarmRaisedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<AlarmClearedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<AlarmEscalatedIntegrationEventHandlerForNotification>();
builder.Services.AddScoped<InspectionTaskOverdueIntegrationEventHandlerForNotification>();

var app = builder.Build();
if (usePostgreSql && autoMigrate)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDatabaseMigrationRunner>().MigrateAsync();
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
app.MapHealthChecks("/health");
app.Run();

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

public partial class Program;
