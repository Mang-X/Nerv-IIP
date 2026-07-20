using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

var builder = DistributedApplication.CreateBuilder(args);
const string LocalDevelopmentEnvironment = "Development";

var fullStackSessionId = Environment.GetEnvironmentVariable("NERV_IIP_SESSION_ID");
var fullStackEphemeral = string.Equals(
    Environment.GetEnvironmentVariable("NERV_IIP_EPHEMERAL"),
    "true",
    StringComparison.OrdinalIgnoreCase);
var leaderDemoEnabled = string.Equals(
    Environment.GetEnvironmentVariable("NERV_IIP_LEADER_DEMO"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (fullStackEphemeral &&
    (string.IsNullOrWhiteSpace(fullStackSessionId) ||
     !Regex.IsMatch(fullStackSessionId, "^nerv-[a-f0-9]{4}-[a-f0-9]{6}$", RegexOptions.CultureInvariant)))
{
    throw new InvalidOperationException("NERV_IIP_EPHEMERAL=true requires a validated NERV_IIP_SESSION_ID.");
}

string SessionVolume(string persistentName) =>
    fullStackEphemeral ? $"{persistentName}-{fullStackSessionId}" : persistentName;

builder.AddDockerComposeEnvironment("compose");

var iamJwtSigningKeyId = builder.AddParameter("iam-jwt-signing-key-id", secret: true);
var iamJwtPrivateKeyPem = builder.AddParameter("iam-jwt-private-key-pem", secret: true);
var iamJwtJwksJson = builder.AddParameter("iam-jwt-jwks-json", secret: true);
var iamSecretsPepper = builder.AddParameter("iam-secrets-pepper", secret: true);
var internalServiceBearerToken = builder.AddParameter("internal-service-bearer-token", secret: true);
var minioRootUser = builder.AddParameter("minio-root-user", secret: true);
var minioRootPassword = builder.AddParameter("minio-root-password", secret: true);
var redisPassword = builder.AddParameter("redis-password", secret: true);
var iamSeedAdminPassword = builder.AddParameter("iam-seed-admin-password", secret: true);
var iamSeedConnectorHostSecret = builder.AddParameter("iam-seed-connector-host-secret", secret: true);
var connectorIngestionTokenSigningKey = builder.AddParameter("connector-ingestion-token-signing-key", secret: true);
var messagingProvider = builder.Configuration["Messaging:Provider"] ?? "InMemory";
var useRabbitMq = string.Equals(messagingProvider, "RabbitMQ", StringComparison.OrdinalIgnoreCase);
var useRedisMessaging = string.Equals(messagingProvider, "Redis", StringComparison.OrdinalIgnoreCase);
var useOtelCollector = builder.Configuration.GetValue("Observability:UseCollector", false);
var useVictoriaLogs = builder.Configuration.GetValue("Observability:VictoriaLogs:Enabled", true);
var aspireDashboardOtlpHttpEndpoint = builder.Configuration["Observability:AspireDashboardOtlpHttpEndpoint"] ?? "http://host.docker.internal:18890";
var victoriaLogsRetentionPeriod = builder.Configuration["Observability:VictoriaLogs:RetentionPeriod"] ?? "30d";
var connectorHostId = builder.Configuration["ConnectorHost:ConnectorHostId"] ?? "connector-host-001";
var connectorHostOrganizationId = builder.Configuration["ConnectorHost:OrganizationId"] ?? "org-001";
var connectorHostEnvironmentId = builder.Configuration["ConnectorHost:EnvironmentId"] ?? "env-dev";
var connectorHealthAcceptanceEnabled = builder.Configuration.GetValue("ConnectorHealthAcceptance:Enabled", false);
var gatewayCorsAllowedOrigins = builder.Configuration["Security:Cors:AllowedOrigins"];
if (string.IsNullOrWhiteSpace(gatewayCorsAllowedOrigins))
{
    gatewayCorsAllowedOrigins = "http://localhost:5105,http://localhost:5125,http://localhost:5128";
}

var postgres = WithFullStackOwnership(builder.AddPostgres("postgres"))
    .WithImageTag("18")
    .WithDataVolume(SessionVolume("nerv-iip-postgres-18"));
if (fullStackEphemeral)
{
    postgres.WithArgs("-c", "max_connections=300");
}
var appHubDatabase = postgres.AddDatabase("apphub-db", "nerv_iip_apphub");
var iamDatabase = postgres.AddDatabase("iam-db", "nerv_iip_iam");
var opsDatabase = postgres.AddDatabase("ops-db", "nerv_iip_ops");
var notificationDatabase = postgres.AddDatabase("notification-db", "nerv_iip_notification");
var businessMasterDataDatabase = postgres.AddDatabase("business-master-data-db", "nerv_iip_business_masterdata");
var businessProductEngineeringDatabase = postgres.AddDatabase("business-product-engineering-db", "nerv_iip_product_engineering");
var businessInventoryDatabase = postgres.AddDatabase("business-inventory-db", "nerv_iip_inventory");
var businessQualityDatabase = postgres.AddDatabase("business-quality-db", "nerv_iip_quality");
var businessMesDatabase = postgres.AddDatabase("business-mes-db", "nerv_iip_mes");
var businessDemandPlanningDatabase = postgres.AddDatabase("business-demand-planning-db", "nerv_iip_demand_planning");
var businessBarcodeLabelDatabase = postgres.AddDatabase("business-barcode-label-db", "nerv_iip_barcode");
var businessApprovalDatabase = postgres.AddDatabase("business-approval-db", "nerv_iip_business_approval");
var businessWmsDatabase = postgres.AddDatabase("business-wms-db", "nerv_iip_wms");
var businessIndustrialTelemetryDatabase = postgres.AddDatabase("business-industrial-telemetry-db", "nerv_iip_industrial_telemetry");
var businessMaintenanceDatabase = postgres.AddDatabase("business-maintenance-db", "nerv_iip_maintenance");
var businessErpDatabase = postgres.AddDatabase("business-erp-db", "nerv_iip_erp");
var businessSchedulingDatabase = postgres.AddDatabase("business-scheduling-db", "nerv_iip_scheduling");
var redis = WithFullStackOwnership(builder.AddRedis("redis", password: redisPassword))
    .WithImageTag("8")
    .WithDataVolume(SessionVolume("nerv-iip-redis"))
    .WithPersistence(TimeSpan.FromSeconds(60), 1);
var rabbitmq = useRabbitMq
    ? WithFullStackOwnership(builder.AddRabbitMQ("rabbitmq")).WithManagementPlugin()
    : null;
var minio = WithFullStackOwnership(builder.AddContainer("minio", "pgsty/minio", "RELEASE.2026-04-17T00-00-00Z"))
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", minioRootUser)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioRootPassword)
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 9001, targetPort: 9001, name: "console")
    .WithVolume(SessionVolume("nerv-iip-minio"), "/data");
Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ContainerResource>? victoriaLogs = null;
if (useVictoriaLogs)
{
    victoriaLogs = WithFullStackOwnership(builder.AddContainer("victoria-logs", "victoriametrics/victoria-logs", "v1.50.0"))
        .WithArgs("-storageDataPath=/victoria-logs-data", $"-retentionPeriod={victoriaLogsRetentionPeriod}")
        .WithHttpEndpoint(port: fullStackEphemeral ? null : 9428, targetPort: 9428, name: "http")
        .WithVolume(SessionVolume("nerv-iip-victoria-logs"), "/victoria-logs-data");
}
Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ContainerResource>? otelCollector = null;
if (useOtelCollector)
{
    otelCollector = WithFullStackOwnership(builder.AddContainer("otel-collector", "otel/opentelemetry-collector", "0.116.1"))
        .WithArgs("--config=/etc/otelcol/config.yaml")
        .WithEnvironment("NERV_IIP_ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT", aspireDashboardOtlpHttpEndpoint)
        .WithBindMount("../../otel/otel-collector.dev.yaml", "/etc/otelcol/config.yaml", isReadOnly: true)
        .WithEndpoint(port: fullStackEphemeral ? null : 4317, targetPort: 4317, name: "otlp-grpc")
        .WithHttpEndpoint(port: fullStackEphemeral ? null : 4318, targetPort: 4318, name: "otlp-http");
}

var apphub = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5101, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("ConnectorHostCredential__ConnectorHostId", connectorHostId)
    .WithEnvironment("ConnectorHostCredential__OrganizationId", connectorHostOrganizationId)
    .WithEnvironment("ConnectorHostCredential__EnvironmentId", connectorHostEnvironmentId)
    .WithEnvironment("ConnectorHostCredential__Secret", iamSeedConnectorHostSecret)
    .WithEnvironment("ConnectorIngestionToken__SigningKey", connectorIngestionTokenSigningKey)
    .WithEnvironment("AppHub__HeartbeatTimeoutScan__Enabled", "true")
    .WithEnvironment("CollectionHealth__HostHeartbeatCadence", "00:00:02")
    .WithEnvironment("CollectionHealth__HostLivenessTimeout", "00:00:06")
    .WithEnvironment("CollectionHealth__BackendDeadline", "00:00:08")
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(appHubDatabase, "AppHubDb")
    .WithReference(redis)
    .WaitFor(appHubDatabase)
    .WaitFor(redis);

if (rabbitmq is not null)
{
    apphub = apphub
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var iam = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Iam_Web>("iam")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5102, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Iam__Seed__Enabled", "true")
    .WithEnvironment("Iam__Seed__AdminPassword", iamSeedAdminPassword)
    .WithEnvironment("Iam__Seed__ConnectorHostSecret", iamSeedConnectorHostSecret)
    .WithEnvironment("Iam__Jwt__SigningKeys__0__Kid", iamJwtSigningKeyId)
    .WithEnvironment("Iam__Jwt__SigningKeys__0__PrivateKeyPem", iamJwtPrivateKeyPem)
    .WithEnvironment("Iam__Secrets__Pepper", iamSecretsPepper)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(iamDatabase, "IamDb")
    .WithReference(redis)
    .WaitFor(iamDatabase)
    .WaitFor(redis);

var ops = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5103, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(opsDatabase, "OpsDb")
    .WaitFor(opsDatabase)
    .WaitFor(iam);
ops = WithRedisMessagingTransport(ops);
if (rabbitmq is not null)
{
    ops = ops
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var fileStorage = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_FileStorage_Web>("file-storage")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5104, name: "http")
    .WithEnvironment("Storage__Provider", "MinIO")
    .WithEnvironment("Storage__MinIO__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("Storage__MinIO__AccessKey", minioRootUser)
    .WithEnvironment("Storage__MinIO__SecretKey", minioRootPassword)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(redis)
    .WaitFor(redis)
    .WaitFor(minio);

var notification = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Notification_Web>("notification")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5106, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithEnvironment("Observability__Alerts__Enabled", "true")
    .WithEnvironment("Observability__Alerts__OrganizationId", connectorHostOrganizationId)
    .WithEnvironment("Observability__Alerts__EnvironmentId", connectorHostEnvironmentId)
    .WithEnvironment("Observability__Alerts__InternalServiceBearerToken", internalServiceBearerToken)
    .WithEnvironment("Observability__Alerts__AppHubBaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Observability__Alerts__RecipientRefs__0", "role:ops-admin")
    .WithEnvironment("Observability__Alerts__Rules__0__RuleId", "service-health-apphub")
    .WithEnvironment("Observability__Alerts__Rules__0__Name", "AppHub health")
    .WithEnvironment("Observability__Alerts__Rules__0__Kind", "service-health")
    .WithEnvironment("Observability__Alerts__Rules__0__HealthUrl", $"{apphub.GetEndpoint("http")}/health")
    .WithEnvironment("Observability__Alerts__Rules__1__RuleId", "notification-dlq-backlog")
    .WithEnvironment("Observability__Alerts__Rules__1__Name", "Notification CAP/DLQ backlog")
    .WithEnvironment("Observability__Alerts__Rules__1__Kind", "cap-dlq-backlog")
    .WithEnvironment("Observability__Alerts__Rules__1__Threshold", "1")
    .WithEnvironment("Observability__Alerts__Rules__2__RuleId", "connector-host-heartbeat-stale")
    .WithEnvironment("Observability__Alerts__Rules__2__Name", "Connector Host heartbeat stale")
    .WithEnvironment("Observability__Alerts__Rules__2__Kind", "connector-heartbeat-stale")
    .WithEnvironment("Observability__Alerts__Rules__2__Threshold", "1")
    .WithEnvironment("Observability__Alerts__Rules__2__HeartbeatMaxAge", "00:05:00")
    .WithEnvironment("Observability__Alerts__Rules__3__RuleId", "postgres-connection-watermark")
    .WithEnvironment("Observability__Alerts__Rules__3__Name", "PostgreSQL connection watermark")
    .WithEnvironment("Observability__Alerts__Rules__3__Kind", "postgres-watermark")
    .WithEnvironment("Observability__Alerts__Rules__3__MetricName", "connections")
    .WithEnvironment("Observability__Alerts__Rules__3__ConnectionStringName", "NotificationDb")
    .WithEnvironment("Observability__Alerts__Rules__3__WatermarkPercent", "80")
    .WithEnvironment("Observability__Alerts__Rules__4__RuleId", "postgres-database-size-watermark")
    .WithEnvironment("Observability__Alerts__Rules__4__Name", "PostgreSQL database size watermark")
    .WithEnvironment("Observability__Alerts__Rules__4__Kind", "postgres-watermark")
    .WithEnvironment("Observability__Alerts__Rules__4__MetricName", "database-size")
    .WithEnvironment("Observability__Alerts__Rules__4__ConnectionStringName", "NotificationDb")
    .WithEnvironment("Observability__Alerts__Rules__4__WatermarkPercent", "85")
    .WithEnvironment("Observability__Alerts__Rules__4__CapacityMegabytes", "10240")
    .WithEnvironment("Approval__OverdueEscalation__RecipientRefs__0", "role:business-approval-manager")
    .WithReference(apphub)
    .WithReference(notificationDatabase, "NotificationDb")
    .WaitFor(apphub)
    .WaitFor(notificationDatabase);
notification = WithRedisMessagingTransport(notification);
if (rabbitmq is not null)
{
    notification = notification
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMasterData = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_MasterData_Web>("business-master-data")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5107, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessMasterDataDatabase, "PostgreSQL")
    .WithReference(redis)
    .WaitFor(businessMasterDataDatabase)
    .WaitFor(redis);
if (rabbitmq is not null)
{
    businessMasterData = businessMasterData
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessProductEngineering = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_ProductEngineering_Web>("business-product-engineering")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5108, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")
    .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessProductEngineeringDatabase, "PostgreSQL")
    .WithReference(businessMasterData)
    .WaitFor(businessProductEngineeringDatabase)
    .WaitFor(businessMasterData);
businessProductEngineering = WithRedisMessagingTransport(businessProductEngineering);
if (rabbitmq is not null)
{
    businessProductEngineering = businessProductEngineering
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessInventory = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Inventory_Web>("business-inventory")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5109, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessInventoryDatabase, "PostgreSQL")
    .WaitFor(businessInventoryDatabase);
businessInventory = WithRedisMessagingTransport(businessInventory);
if (rabbitmq is not null)
{
    businessInventory = businessInventory
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessQuality = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Quality_Web>("business-quality")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5110, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessQualityDatabase, "PostgreSQL")
    .WithReference(redis)
    .WaitFor(businessQualityDatabase)
    .WaitFor(redis);
if (rabbitmq is not null)
{
    businessQuality = businessQuality
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMes = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Mes_Web>("business-mes")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5111, name: "http")
    .WithHttpHealthCheck("/swagger/v1/swagger.json")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")
    .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
    .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
    .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
    .WithEnvironment("Inventory__DefaultSiteCode", "production")
    .WithEnvironment("Inventory__SiteCodes__0", "warehouse")
    .WithEnvironment("Inventory__SiteCodes__1", "production")
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessMesDatabase, "PostgreSQL")
    .WithReference(businessMasterData)
    .WithReference(businessProductEngineering)
    .WithReference(businessInventory)
    .WaitFor(businessMesDatabase)
    .WaitFor(businessMasterData)
    .WaitFor(businessProductEngineering)
    .WaitFor(businessInventory);
businessMes = WithRedisMessagingTransport(businessMes);
if (rabbitmq is not null)
{
    businessMes = businessMes
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessDemandPlanning = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_DemandPlanning_Web>("business-demand-planning")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5112, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
    .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
    .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessDemandPlanningDatabase, "PostgreSQL")
    .WithReference(businessMasterData)
    .WithReference(businessProductEngineering)
    .WithReference(businessInventory)
    .WaitFor(businessDemandPlanningDatabase)
    .WaitFor(businessMasterData)
    .WaitFor(businessProductEngineering)
    .WaitFor(businessInventory);
businessDemandPlanning = WithRedisMessagingTransport(businessDemandPlanning);
if (rabbitmq is not null)
{
    businessDemandPlanning = businessDemandPlanning
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessBarcodeLabel = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_BarcodeLabel_Web>("business-barcode-label")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5113, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessBarcodeLabelDatabase, "PostgreSQL")
    .WaitFor(businessBarcodeLabelDatabase);
businessBarcodeLabel = WithRedisMessagingTransport(businessBarcodeLabel);
if (rabbitmq is not null)
{
    businessBarcodeLabel = businessBarcodeLabel
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessApproval = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Approval_Web>("business-approval")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5114, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("Approval__OverdueCheck__Enabled", "true")
    .WithEnvironment("Approval__OverdueCheck__Scopes__0__OrganizationId", "org-001")
    .WithEnvironment("Approval__OverdueCheck__Scopes__0__EnvironmentId", "env-dev")
    .WithEnvironment("Approval__OverdueCheck__Interval", "00:05:00")
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessApprovalDatabase, "PostgreSQL")
    .WaitFor(businessApprovalDatabase);
businessApproval = WithRedisMessagingTransport(businessApproval);
if (rabbitmq is not null)
{
    businessApproval = businessApproval
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessWms = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Wms_Web>("business-wms")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5115, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessWmsDatabase, "PostgreSQL")
    .WithReference(businessInventory)
    .WaitFor(businessWmsDatabase)
    .WaitFor(businessInventory);
businessWms = WithRedisMessagingTransport(businessWms);
if (rabbitmq is not null)
{
    businessWms = businessWms
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessIndustrialTelemetry = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_IndustrialTelemetry_Web>("business-industrial-telemetry")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5116, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")
    .WithEnvironment("Ops__BaseUrl", ops.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessIndustrialTelemetryDatabase, "PostgreSQL")
    .WithReference(ops)
    .WaitFor(businessIndustrialTelemetryDatabase)
    .WaitFor(ops);
businessIndustrialTelemetry = WithRedisMessagingTransport(businessIndustrialTelemetry);
if (rabbitmq is not null)
{
    businessIndustrialTelemetry = businessIndustrialTelemetry
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMaintenance = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Maintenance_Web>("business-maintenance")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5117, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessMaintenanceDatabase, "PostgreSQL")
    .WaitFor(businessMaintenanceDatabase);
businessMaintenance = WithRedisMessagingTransport(businessMaintenance);
if (rabbitmq is not null)
{
    businessMaintenance = businessMaintenance
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessErp = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Erp_Web>("business-erp")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5118, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("Erp__Seed__SalesOrderDemandDemo__Enabled", "true")
    .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessErpDatabase, "PostgreSQL")
    .WithReference(businessMasterData)
    .WithReference(iam)
    .WaitFor(businessErpDatabase)
    .WaitFor(businessMasterData)
    .WaitFor(iam);
businessErp = WithRedisMessagingTransport(businessErp);
if (rabbitmq is not null)
{
    businessErp = businessErp
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

businessDemandPlanning = businessDemandPlanning
    .WithEnvironment("Erp__BaseUrl", businessErp.GetEndpoint("http"))
    .WithReference(businessErp)
    .WaitFor(businessErp);

businessQuality = businessQuality
    .WithEnvironment("Erp__BaseUrl", businessErp.GetEndpoint("http"))
    .WithReference(businessErp)
    .WaitFor(businessErp);

var businessScheduling = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Scheduling_Web>("business-scheduling")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5120, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("Mes__BaseUrl", businessMes.GetEndpoint("http"))
    .WithEnvironment("IndustrialTelemetry__BaseUrl", businessIndustrialTelemetry.GetEndpoint("http"))
    .WithEnvironment("Maintenance__BaseUrl", businessMaintenance.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessSchedulingDatabase, "PostgreSQL")
    .WithReference(businessMes)
    .WithReference(businessIndustrialTelemetry)
    .WithReference(businessMaintenance)
    .WaitFor(businessSchedulingDatabase);
businessScheduling = WithRedisMessagingTransport(businessScheduling);
if (rabbitmq is not null)
{
    businessScheduling = businessScheduling
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var gateway = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_PlatformGateway_Web>("gateway")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5100, name: "http")
    .WithEnvironment("AppHub__BaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("Iam__Jwt__JwksJson", iamJwtJwksJson)
    .WithEnvironment("Security__Cors__AllowedOrigins", gatewayCorsAllowedOrigins)
    .WithEnvironment("Ops__BaseUrl", ops.GetEndpoint("http"))
    .WithEnvironment("Notification__BaseUrl", notification.GetEndpoint("http"))
    .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(apphub)
    .WithReference(iam)
    .WithReference(ops)
    .WithReference(notification)
    .WithReference(fileStorage)
    .WithReference(businessMasterData)
    .WithReference(businessProductEngineering)
    .WithReference(businessInventory)
    .WithReference(businessQuality)
    .WithReference(businessMes)
    .WithReference(businessDemandPlanning)
    .WithReference(businessBarcodeLabel)
    .WithReference(businessApproval)
    .WithReference(businessWms)
    .WithReference(businessIndustrialTelemetry)
    .WithReference(businessMaintenance)
    .WithReference(businessErp)
    .WithReference(businessScheduling)
    .WithReference(redis)
    .WaitFor(apphub)
    .WaitFor(iam)
    .WaitFor(ops)
    .WaitFor(notification)
    .WaitFor(fileStorage)
    .WaitFor(businessMasterData)
    .WaitFor(businessProductEngineering)
    .WaitFor(businessInventory)
    .WaitFor(businessQuality)
    .WaitFor(businessMes)
    .WaitFor(businessDemandPlanning)
    .WaitFor(businessBarcodeLabel)
    .WaitFor(businessApproval)
    .WaitFor(businessWms)
    .WaitFor(businessIndustrialTelemetry)
    .WaitFor(businessMaintenance)
    .WaitFor(businessErp)
    .WaitFor(businessScheduling)
    .WaitFor(redis);
if (victoriaLogs is not null)
{
    gateway = gateway
        .WithEnvironment("VictoriaLogs__BaseUrl", victoriaLogs.GetEndpoint("http"))
        .WaitFor(victoriaLogs);
}
else
{
    gateway = gateway
        .WithEnvironment("VictoriaLogs__Enabled", "false");
}

var businessGateway = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_BusinessGateway_Web>("business-gateway")))
    .WithHttpEndpoint(port: fullStackEphemeral ? null : 5119, name: "http")
    .WithEnvironment("AppHub__BaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("Iam__Jwt__JwksJson", iamJwtJwksJson)
    .WithEnvironment("Iam__Jwt__Issuer", "nerv-iip-iam")
    .WithEnvironment("Iam__Jwt__Audience", "nerv-iip-api")
    .WithEnvironment("Security__Cors__AllowedOrigins", gatewayCorsAllowedOrigins)
    .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
    .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
    .WithEnvironment("Quality__BaseUrl", businessQuality.GetEndpoint("http"))
    .WithEnvironment("Mes__BaseUrl", businessMes.GetEndpoint("http"))
    .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
    .WithEnvironment("DemandPlanning__BaseUrl", businessDemandPlanning.GetEndpoint("http"))
    .WithEnvironment("Scheduling__BaseUrl", businessScheduling.GetEndpoint("http"))
    .WithEnvironment("Erp__BaseUrl", businessErp.GetEndpoint("http"))
    .WithEnvironment("Wms__BaseUrl", businessWms.GetEndpoint("http"))
    .WithEnvironment("Approval__BaseUrl", businessApproval.GetEndpoint("http"))
    .WithEnvironment("BarcodeLabel__BaseUrl", businessBarcodeLabel.GetEndpoint("http"))
    .WithEnvironment("Notification__BaseUrl", notification.GetEndpoint("http"))
    .WithEnvironment("IndustrialTelemetry__BaseUrl", businessIndustrialTelemetry.GetEndpoint("http"))
    .WithEnvironment("Maintenance__BaseUrl", businessMaintenance.GetEndpoint("http"))
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(apphub)
    .WithReference(iam)
    .WithReference(businessMasterData)
    .WithReference(businessInventory)
    .WithReference(businessQuality)
    .WithReference(businessMes)
    .WithReference(businessProductEngineering)
    .WithReference(businessDemandPlanning)
    .WithReference(businessScheduling)
    .WithReference(businessErp)
    .WithReference(businessWms)
    .WithReference(businessApproval)
    .WithReference(businessBarcodeLabel)
    .WithReference(notification)
    .WithReference(businessIndustrialTelemetry)
    .WithReference(businessMaintenance)
    .WithReference(redis)
    .WaitFor(apphub)
    .WaitFor(iam)
    .WaitFor(businessMasterData)
    .WaitFor(businessInventory)
    .WaitFor(businessQuality)
    .WaitFor(businessMes)
    .WaitFor(businessProductEngineering)
    .WaitFor(businessDemandPlanning)
    .WaitFor(businessScheduling)
    .WaitFor(businessErp)
    .WaitFor(businessWms)
    .WaitFor(businessApproval)
    .WaitFor(businessBarcodeLabel)
    .WaitFor(notification)
    .WaitFor(businessIndustrialTelemetry)
    .WaitFor(businessMaintenance)
    .WaitFor(redis);

var connectorHost = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host")))
    .WithEnvironment("ConnectorHost__CycleSeconds", "1")
    .WithEnvironment("ConnectorHost__ConnectorHostId", connectorHostId)
    .WithEnvironment("ConnectorHost__OrganizationId", connectorHostOrganizationId)
    .WithEnvironment("ConnectorHost__EnvironmentId", connectorHostEnvironmentId)
    .WithEnvironment("ConnectorHost__ConnectorSecret", iamSeedConnectorHostSecret)
    .WithEnvironment("Platform__AppHubBaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Platform__OpsBaseUrl", ops.GetEndpoint("http"))
    .WithReference(apphub)
    .WithReference(ops)
    .WithReference(iam)
    .WaitFor(apphub)
    .WaitFor(ops)
    .WaitFor(iam);

if (connectorHealthAcceptanceEnabled)
{
    var modbusEndpoint = builder.Configuration["ConnectorHealthAcceptance:ModbusEndpoint"];
    if (string.IsNullOrWhiteSpace(modbusEndpoint))
    {
        throw new InvalidOperationException("ConnectorHealthAcceptance:ModbusEndpoint is required when acceptance wiring is enabled.");
    }

    connectorHost = connectorHost
        .WithEnvironment("Platform__IndustrialTelemetryBaseUrl", businessIndustrialTelemetry.GetEndpoint("http"))
        .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
        .WithEnvironment("ConnectorHost__CollectionCycleSeconds", "1")
        .WithEnvironment("ConnectorHost__HeartbeatSeconds", "2")
        .WithEnvironment("Modbus__Enabled", "true")
        .WithEnvironment("Modbus__ConnectorId", "acceptance-modbus")
        .WithEnvironment("Modbus__CollectionConnectorId", "modbus-acceptance")
        .WithEnvironment("Modbus__Endpoint", modbusEndpoint)
        .WithEnvironment("Modbus__MaxReconnectAttempts", "0")
        .WithEnvironment("Modbus__Registers__0__DeviceAssetId", "DEV-ACCEPTANCE-01")
        .WithEnvironment("Modbus__Registers__0__TagKey", "acceptance.sampled")
        .WithEnvironment("Modbus__Registers__0__UnitId", "1")
        .WithEnvironment("Modbus__Registers__0__Table", "HoldingRegisters")
        .WithEnvironment("Modbus__Registers__0__Address", "40001")
        .WithEnvironment("Modbus__Registers__0__RegisterCount", "1")
        .WithEnvironment("Modbus__Registers__0__DataType", "UInt16")
        .WithEnvironment("Modbus__Registers__0__WordOrder", "BigEndian")
        .WithEnvironment("Modbus__Registers__0__BucketSeconds", "1")
        .WithEnvironment("Modbus__Registers__1__DeviceAssetId", "DEV-ACCEPTANCE-01")
        .WithEnvironment("Modbus__Registers__1__TagKey", "acceptance.never-sampled")
        .WithEnvironment("Modbus__Registers__1__UnitId", "1")
        .WithEnvironment("Modbus__Registers__1__Table", "HoldingRegisters")
        .WithEnvironment("Modbus__Registers__1__Address", "40002")
        .WithEnvironment("Modbus__Registers__1__RegisterCount", "2")
        .WithEnvironment("Modbus__Registers__1__DataType", "Float32")
        .WithEnvironment("Modbus__Registers__1__BucketSeconds", "1")
        .WithReference(businessIndustrialTelemetry)
        .WaitFor(businessIndustrialTelemetry);
}

// PublishAsStaticWebsite is an experimental Aspire API (ASPIREJAVASCRIPT001).
// Business Console omits it until its two-backend production route model is finalized.
#pragma warning disable ASPIREJAVASCRIPT001
builder.AddViteApp("console", "../../../frontend/apps/console")
    .WithEndpoint(
        targetPort: fullStackEphemeral ? null : 5105,
        port: fullStackEphemeral ? null : 5105,
        scheme: "http",
        name: "http",
        env: fullStackEphemeral ? "NERV_IIP_VITE_PORT" : null,
        isProxied: fullStackEphemeral)
    .WithPnpm()
    .WithEnvironment("NERV_IIP_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WaitFor(gateway)
    .WaitFor(connectorHost)
    .PublishAsStaticWebsite(apiPath: "/api", apiTarget: gateway);
#pragma warning restore ASPIREJAVASCRIPT001

builder.AddViteApp("business-console", "../../../frontend/apps/business-console")
    .WithEndpoint(
        targetPort: fullStackEphemeral ? null : 5125,
        port: fullStackEphemeral ? null : 5125,
        scheme: "http",
        name: "http",
        env: fullStackEphemeral ? "NERV_IIP_VITE_PORT" : null,
        isProxied: fullStackEphemeral)
    .WithPnpm()
    .WithEnvironment("NERV_IIP_PLATFORM_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("NERV_IIP_BUSINESS_GATEWAY_URL", businessGateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WithReference(businessGateway)
    .WaitFor(gateway)
    .WaitFor(businessGateway);

// 工业数据大屏（公共展示 / 指挥中心，独立 app）。消费 BusinessGateway 业务数据，
// 鉴权走 PlatformGateway。生产静态站待两后端路由模型定稿，同 business-console 暂不加 PublishAsStaticWebsite。
builder.AddViteApp("screen", "../../../frontend/apps/screen")
    .WithEndpoint(
        targetPort: fullStackEphemeral ? null : 5128,
        port: fullStackEphemeral ? null : 5128,
        scheme: "http",
        name: "http",
        env: fullStackEphemeral ? "NERV_IIP_VITE_PORT" : null,
        isProxied: fullStackEphemeral)
    .WithPnpm()
    .WithEnvironment("NERV_IIP_PLATFORM_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("NERV_IIP_BUSINESS_GATEWAY_URL", businessGateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WithReference(businessGateway)
    .WaitFor(gateway)
    .WaitFor(businessGateway);

builder.Build().Run();

Aspire.Hosting.ApplicationModel.IResourceBuilder<T> WithFullStackOwnership<T>(
    Aspire.Hosting.ApplicationModel.IResourceBuilder<T> resource)
    where T : Aspire.Hosting.ApplicationModel.ContainerResource
{
    return fullStackEphemeral
        ? resource.WithContainerRuntimeArgs("--label", $"com.nerv-iip.session={fullStackSessionId}")
        : resource;
}

Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ProjectResource> WithRedisMessagingTransport(
    Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ProjectResource> project)
{
    return useRedisMessaging
        ? project.WithReference(redis).WaitFor(redis)
        : project;
}

Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ProjectResource> WithLocalDevelopmentEnvironment(
    Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ProjectResource> project)
{
    return project
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", LocalDevelopmentEnvironment)
        .WithEnvironment("DOTNET_ENVIRONMENT", LocalDevelopmentEnvironment);
}

Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ProjectResource> WithNervIipTelemetry(
    Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ProjectResource> project)
{
    if (victoriaLogs is not null)
    {
        project = project
            .WithEnvironment("OpenTelemetry__Logs__Endpoint", victoriaLogs.GetEndpoint("http"))
            .WithEnvironment("OpenTelemetry__Logs__Path", "/insert/opentelemetry/v1/logs")
            .WaitFor(victoriaLogs);
    }

    if (otelCollector is null)
    {
        return project;
    }

    return project
        .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
        .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
        .WaitFor(otelCollector);
}
