using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
const string LocalDevelopmentEnvironment = "Development";

builder.AddDockerComposeEnvironment("compose");

var iamJwtSigningKey = builder.AddParameter("iam-jwt-signing-key", secret: true);
var internalServiceBearerToken = builder.AddParameter("internal-service-bearer-token", secret: true);
var minioRootUser = builder.AddParameter("minio-root-user", secret: true);
var minioRootPassword = builder.AddParameter("minio-root-password", secret: true);
var redisPassword = builder.AddParameter("redis-password", secret: true);
var iamSeedAdminPassword = builder.AddParameter("iam-seed-admin-password", secret: true);
var iamSeedConnectorHostSecret = builder.AddParameter("iam-seed-connector-host-secret", secret: true);
var messagingProvider = builder.Configuration["Messaging:Provider"] ?? "InMemory";
var useRabbitMq = string.Equals(messagingProvider, "RabbitMQ", StringComparison.OrdinalIgnoreCase);
var useRedisMessaging = string.Equals(messagingProvider, "Redis", StringComparison.OrdinalIgnoreCase);
var useOtelCollector = builder.Configuration.GetValue("Observability:UseCollector", false);
var useVictoriaLogs = builder.Configuration.GetValue("Observability:VictoriaLogs:Enabled", true);
var aspireDashboardOtlpHttpEndpoint = builder.Configuration["Observability:AspireDashboardOtlpHttpEndpoint"] ?? "http://host.docker.internal:18890";
var victoriaLogsRetentionPeriod = builder.Configuration["Observability:VictoriaLogs:RetentionPeriod"] ?? "30d";
var gatewayCorsAllowedOrigins = builder.Configuration["Security:Cors:AllowedOrigins"];
if (string.IsNullOrWhiteSpace(gatewayCorsAllowedOrigins))
{
    gatewayCorsAllowedOrigins = "http://localhost:5105,http://localhost:5125";
}

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume("nerv-iip-postgres-18");
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
var redis = builder.AddRedis("redis", password: redisPassword)
    .WithImageTag("8")
    .WithDataVolume("nerv-iip-redis")
    .WithPersistence(TimeSpan.FromSeconds(60), 1);
var rabbitmq = useRabbitMq
    ? builder.AddRabbitMQ("rabbitmq").WithManagementPlugin()
    : null;
var minio = builder.AddContainer("minio", "pgsty/minio", "RELEASE.2026-04-17T00-00-00Z")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", minioRootUser)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioRootPassword)
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithVolume("nerv-iip-minio", "/data");
Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ContainerResource>? victoriaLogs = null;
if (useVictoriaLogs)
{
    victoriaLogs = builder.AddContainer("victoria-logs", "victoriametrics/victoria-logs", "v1.50.0")
        .WithArgs("-storageDataPath=/victoria-logs-data", $"-retentionPeriod={victoriaLogsRetentionPeriod}")
        .WithHttpEndpoint(port: 9428, targetPort: 9428, name: "http")
        .WithVolume("nerv-iip-victoria-logs", "/victoria-logs-data");
}
Aspire.Hosting.ApplicationModel.IResourceBuilder<Aspire.Hosting.ApplicationModel.ContainerResource>? otelCollector = null;
if (useOtelCollector)
{
    otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector", "0.116.1")
        .WithArgs("--config=/etc/otelcol/config.yaml")
        .WithEnvironment("NERV_IIP_ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT", aspireDashboardOtlpHttpEndpoint)
        .WithBindMount("../../otel/otel-collector.dev.yaml", "/etc/otelcol/config.yaml", isReadOnly: true)
        .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc")
        .WithHttpEndpoint(port: 4318, targetPort: 4318, name: "otlp-http");
}

var apphub = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")))
    .WithHttpEndpoint(port: 5101, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("ConnectorHostCredential__Secret", iamSeedConnectorHostSecret)
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
    .WithHttpEndpoint(port: 5102, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Iam__Seed__Enabled", "true")
    .WithEnvironment("Iam__Seed__AdminPassword", iamSeedAdminPassword)
    .WithEnvironment("Iam__Seed__ConnectorHostSecret", iamSeedConnectorHostSecret)
    .WithEnvironment("Iam__Jwt__SigningKey", iamJwtSigningKey)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(iamDatabase, "IamDb")
    .WithReference(redis)
    .WaitFor(iamDatabase)
    .WaitFor(redis);

var ops = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")))
    .WithHttpEndpoint(port: 5103, name: "http")
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
    .WithHttpEndpoint(port: 5104, name: "http")
    .WithEnvironment("Storage__Provider", "MinIO")
    .WithEnvironment("Storage__MinIO__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("Storage__MinIO__AccessKey", minioRootUser)
    .WithEnvironment("Storage__MinIO__SecretKey", minioRootPassword)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(redis)
    .WaitFor(redis)
    .WaitFor(minio);

var notification = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Notification_Web>("notification")))
    .WithHttpEndpoint(port: 5106, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(notificationDatabase, "NotificationDb")
    .WaitFor(notificationDatabase);
notification = WithRedisMessagingTransport(notification);
if (rabbitmq is not null)
{
    notification = notification
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMasterData = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_MasterData_Web>("business-master-data")))
    .WithHttpEndpoint(port: 5107, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
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
    .WithHttpEndpoint(port: 5108, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessProductEngineeringDatabase, "PostgreSQL")
    .WaitFor(businessProductEngineeringDatabase);
businessProductEngineering = WithRedisMessagingTransport(businessProductEngineering);
if (rabbitmq is not null)
{
    businessProductEngineering = businessProductEngineering
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessInventory = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Inventory_Web>("business-inventory")))
    .WithHttpEndpoint(port: 5109, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
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
    .WithHttpEndpoint(port: 5110, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
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
    .WithHttpEndpoint(port: 5111, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessMesDatabase, "PostgreSQL")
    .WaitFor(businessMesDatabase);
businessMes = WithRedisMessagingTransport(businessMes);
if (rabbitmq is not null)
{
    businessMes = businessMes
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessDemandPlanning = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_DemandPlanning_Web>("business-demand-planning")))
    .WithHttpEndpoint(port: 5112, name: "http")
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
    .WithHttpEndpoint(port: 5113, name: "http")
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
    .WithHttpEndpoint(port: 5114, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
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
    .WithHttpEndpoint(port: 5115, name: "http")
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
    .WithHttpEndpoint(port: 5116, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessIndustrialTelemetryDatabase, "PostgreSQL")
    .WaitFor(businessIndustrialTelemetryDatabase);
businessIndustrialTelemetry = WithRedisMessagingTransport(businessIndustrialTelemetry);
if (rabbitmq is not null)
{
    businessIndustrialTelemetry = businessIndustrialTelemetry
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMaintenance = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Maintenance_Web>("business-maintenance")))
    .WithHttpEndpoint(port: 5117, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
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
    .WithHttpEndpoint(port: 5118, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(businessErpDatabase, "PostgreSQL")
    .WithReference(iam)
    .WaitFor(businessErpDatabase)
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

var businessScheduling = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Scheduling_Web>("business-scheduling")))
    .WithHttpEndpoint(port: 5120, name: "http")
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
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithEnvironment("AppHub__BaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("Iam__Jwt__SigningKey", iamJwtSigningKey)
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
    .WithHttpEndpoint(port: 5119, name: "http")
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("Iam__Jwt__SigningKey", iamJwtSigningKey)
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
    .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
    .WithReference(iam)
    .WithReference(businessMasterData)
    .WithReference(businessInventory)
    .WithReference(businessQuality)
    .WithReference(businessMes)
    .WithReference(businessProductEngineering)
    .WithReference(businessDemandPlanning)
    .WithReference(businessScheduling)
    .WithReference(redis)
    .WaitFor(iam)
    .WaitFor(businessMasterData)
    .WaitFor(businessInventory)
    .WaitFor(businessQuality)
    .WaitFor(businessMes)
    .WaitFor(businessProductEngineering)
    .WaitFor(businessDemandPlanning)
    .WaitFor(businessScheduling)
    .WaitFor(redis);

var connectorHost = WithNervIipTelemetry(WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host")))
    .WithEnvironment("ConnectorHost__CycleSeconds", "1")
    .WithEnvironment("ConnectorHost__ConnectorSecret", iamSeedConnectorHostSecret)
    .WithEnvironment("Platform__AppHubBaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Platform__OpsBaseUrl", ops.GetEndpoint("http"))
    .WithReference(apphub)
    .WithReference(ops)
    .WithReference(iam)
    .WaitFor(apphub)
    .WaitFor(ops)
    .WaitFor(iam);

// PublishAsStaticWebsite is an experimental Aspire API (ASPIREJAVASCRIPT001).
// Business Console omits it until its two-backend production route model is finalized.
#pragma warning disable ASPIREJAVASCRIPT001
builder.AddViteApp("console", "../../../frontend/apps/console")
    .WithHttpEndpoint(port: 5105, name: "http", isProxied: false)
    .WithPnpm()
    .WithEnvironment("NERV_IIP_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WaitFor(gateway)
    .WaitFor(connectorHost)
    .PublishAsStaticWebsite(apiPath: "/api", apiTarget: gateway);
#pragma warning restore ASPIREJAVASCRIPT001

builder.AddViteApp("business-console", "../../../frontend/apps/business-console")
    .WithHttpEndpoint(port: 5125, name: "http", isProxied: false)
    .WithPnpm()
    .WithEnvironment("NERV_IIP_PLATFORM_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("NERV_IIP_BUSINESS_GATEWAY_URL", businessGateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WithReference(businessGateway)
    .WaitFor(gateway)
    .WaitFor(businessGateway);

builder.Build().Run();

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
