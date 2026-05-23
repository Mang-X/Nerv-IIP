var builder = DistributedApplication.CreateBuilder(args);

var iamJwtSigningKey = builder.AddParameter("iam-jwt-signing-key", secret: true);
var minioRootUser = builder.AddParameter("minio-root-user", secret: true);
var minioRootPassword = builder.AddParameter("minio-root-password", secret: true);
var iamSeedAdminPassword = builder.AddParameter("iam-seed-admin-password", secret: true);
var iamSeedConnectorHostSecret = builder.AddParameter("iam-seed-connector-host-secret", secret: true);
var messagingProvider = builder.Configuration["Messaging:Provider"] ?? "InMemory";
var useRabbitMq = string.Equals(messagingProvider, "RabbitMQ", StringComparison.OrdinalIgnoreCase);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("nerv-iip-postgres");
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
var redis = builder.AddRedis("redis")
    .WithDataVolume("nerv-iip-redis");
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
var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector", "0.116.1")
    .WithArgs("--config=/etc/otelcol/config.yaml")
    .WithBindMount("../../otel/otel-collector.dev.yaml", "/etc/otelcol/config.yaml", isReadOnly: true)
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc")
    .WithHttpEndpoint(port: 4318, targetPort: 4318, name: "otlp-http");

var apphub = builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")
    .WithHttpEndpoint(port: 5101, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(appHubDatabase, "AppHubDb")
    .WithReference(redis)
    .WaitFor(appHubDatabase)
    .WaitFor(redis)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    apphub = apphub
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var iam = builder.AddProject<Projects.Nerv_IIP_Iam_Web>("iam")
    .WithHttpEndpoint(port: 5102, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Iam__Seed__Enabled", "true")
    .WithEnvironment("Iam__Seed__AdminPassword", iamSeedAdminPassword)
    .WithEnvironment("Iam__Seed__ConnectorHostSecret", iamSeedConnectorHostSecret)
    .WithEnvironment("Iam__Jwt__SigningKey", iamJwtSigningKey)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(iamDatabase, "IamDb")
    .WithReference(redis)
    .WaitFor(iamDatabase)
    .WaitFor(redis)
    .WaitFor(otelCollector);

var ops = builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")
    .WithHttpEndpoint(port: 5103, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(opsDatabase, "OpsDb")
    .WaitFor(opsDatabase)
    .WaitFor(iam)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    ops = ops
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var fileStorage = builder.AddProject<Projects.Nerv_IIP_FileStorage_Web>("file-storage")
    .WithHttpEndpoint(port: 5104, name: "http")
    .WithEnvironment("Storage__Provider", "MinIO")
    .WithEnvironment("Storage__MinIO__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("Storage__MinIO__AccessKey", minioRootUser)
    .WithEnvironment("Storage__MinIO__SecretKey", minioRootPassword)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(redis)
    .WaitFor(redis)
    .WaitFor(minio)
    .WaitFor(otelCollector);

var notification = builder.AddProject<Projects.Nerv_IIP_Notification_Web>("notification")
    .WithHttpEndpoint(port: 5106, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(notificationDatabase, "NotificationDb")
    .WaitFor(notificationDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    notification = notification
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMasterData = builder.AddProject<Projects.Nerv_IIP_Business_MasterData_Web>("business-master-data")
    .WithHttpEndpoint(port: 5107, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessMasterDataDatabase, "PostgreSQL")
    .WithReference(redis)
    .WaitFor(businessMasterDataDatabase)
    .WaitFor(redis)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessMasterData = businessMasterData
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessProductEngineering = builder.AddProject<Projects.Nerv_IIP_Business_ProductEngineering_Web>("business-product-engineering")
    .WithHttpEndpoint(port: 5108, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessProductEngineeringDatabase, "PostgreSQL")
    .WaitFor(businessProductEngineeringDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessProductEngineering = businessProductEngineering
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessInventory = builder.AddProject<Projects.Nerv_IIP_Business_Inventory_Web>("business-inventory")
    .WithHttpEndpoint(port: 5109, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessInventoryDatabase, "PostgreSQL")
    .WaitFor(businessInventoryDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessInventory = businessInventory
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessQuality = builder.AddProject<Projects.Nerv_IIP_Business_Quality_Web>("business-quality")
    .WithHttpEndpoint(port: 5110, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessQualityDatabase, "PostgreSQL")
    .WithReference(redis)
    .WaitFor(businessQualityDatabase)
    .WaitFor(redis)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessQuality = businessQuality
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMes = builder.AddProject<Projects.Nerv_IIP_Business_Mes_Web>("business-mes")
    .WithHttpEndpoint(port: 5111, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessMesDatabase, "PostgreSQL")
    .WaitFor(businessMesDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessMes = businessMes
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessDemandPlanning = builder.AddProject<Projects.Nerv_IIP_Business_DemandPlanning_Web>("business-demand-planning")
    .WithHttpEndpoint(port: 5112, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessDemandPlanningDatabase, "PostgreSQL")
    .WaitFor(businessDemandPlanningDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessDemandPlanning = businessDemandPlanning
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessBarcodeLabel = builder.AddProject<Projects.Nerv_IIP_Business_BarcodeLabel_Web>("business-barcode-label")
    .WithHttpEndpoint(port: 5113, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessBarcodeLabelDatabase, "PostgreSQL")
    .WaitFor(businessBarcodeLabelDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessBarcodeLabel = businessBarcodeLabel
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessApproval = builder.AddProject<Projects.Nerv_IIP_Business_Approval_Web>("business-approval")
    .WithHttpEndpoint(port: 5114, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessApprovalDatabase, "PostgreSQL")
    .WaitFor(businessApprovalDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessApproval = businessApproval
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessWms = builder.AddProject<Projects.Nerv_IIP_Business_Wms_Web>("business-wms")
    .WithHttpEndpoint(port: 5115, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessWmsDatabase, "PostgreSQL")
    .WaitFor(businessWmsDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessWms = businessWms
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessIndustrialTelemetry = builder.AddProject<Projects.Nerv_IIP_Business_IndustrialTelemetry_Web>("business-industrial-telemetry")
    .WithHttpEndpoint(port: 5116, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessIndustrialTelemetryDatabase, "PostgreSQL")
    .WaitFor(businessIndustrialTelemetryDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessIndustrialTelemetry = businessIndustrialTelemetry
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var businessMaintenance = builder.AddProject<Projects.Nerv_IIP_Business_Maintenance_Web>("business-maintenance")
    .WithHttpEndpoint(port: 5117, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessMaintenanceDatabase, "PostgreSQL")
    .WaitFor(businessMaintenanceDatabase)
    .WaitFor(otelCollector);
if (rabbitmq is not null)
{
    businessMaintenance = businessMaintenance
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq);
}

var gateway = builder.AddProject<Projects.Nerv_IIP_PlatformGateway_Web>("gateway")
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithEnvironment("AppHub__BaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("Iam__Jwt__SigningKey", iamJwtSigningKey)
    .WithEnvironment("Ops__BaseUrl", ops.GetEndpoint("http"))
    .WithEnvironment("Notification__BaseUrl", notification.GetEndpoint("http"))
    .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
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
    .WithReference(redis)
    .WaitFor(apphub)
    .WaitFor(iam)
    .WaitFor(ops)
    .WaitFor(notification)
    .WaitFor(fileStorage)
    .WaitFor(redis);

var connectorHost = builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host")
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

builder.AddViteApp("console", "../../../frontend/apps/console")
    .WithHttpEndpoint(port: 5105, name: "http")
    .WithPnpm()
    .WithEnvironment("NERV_IIP_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WaitFor(gateway)
    .WaitFor(connectorHost);

builder.Build().Run();
