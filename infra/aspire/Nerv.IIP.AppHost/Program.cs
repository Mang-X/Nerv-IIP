var builder = DistributedApplication.CreateBuilder(args);

const string LocalJwtSigningKey = "aspire-local-development-signing-key-that-is-long-enough";

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("nerv-iip-postgres");
var appHubDatabase = postgres.AddDatabase("apphub-db", "nerv_iip_apphub");
var iamDatabase = postgres.AddDatabase("iam-db", "nerv_iip_iam");
var opsDatabase = postgres.AddDatabase("ops-db", "nerv_iip_ops");
var redis = builder.AddRedis("redis")
    .WithDataVolume("nerv-iip-redis");
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();
var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", "nervminio")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "nervminio")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithVolume("nerv-iip-minio", "/data");
var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector", "0.116.1")
    .WithArgs("--config=/etc/otelcol/config.yaml")
    .WithBindMount("../../otel/otel-collector.dev.yaml", "/etc/otelcol/config.yaml", isReadOnly: true)
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc")
    .WithHttpEndpoint(port: 4318, targetPort: 4318, name: "otlp-http");

var apphub = builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(appHubDatabase, "AppHubDb")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(appHubDatabase)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WaitFor(otelCollector);

var iam = builder.AddProject<Projects.Nerv_IIP_Iam_Web>("iam")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Persistence__AutoMigrate", "true")
    .WithEnvironment("Iam__Seed__Enabled", "true")
    .WithEnvironment("Iam__Seed__AdminPassword", "Admin123!")
    .WithEnvironment("Iam__Seed__ConnectorHostSecret", "local-connector-secret")
    .WithEnvironment("Iam__Jwt__SigningKey", LocalJwtSigningKey)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(iamDatabase, "IamDb")
    .WithReference(redis)
    .WaitFor(iamDatabase)
    .WaitFor(redis)
    .WaitFor(otelCollector);

var ops = builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(opsDatabase, "OpsDb")
    .WithReference(rabbitmq)
    .WaitFor(opsDatabase)
    .WaitFor(rabbitmq)
    .WaitFor(iam)
    .WaitFor(otelCollector);

var fileStorage = builder.AddProject<Projects.Nerv_IIP_FileStorage_Web>("file-storage")
    .WithEnvironment("Storage__Provider", "MinIO")
    .WithEnvironment("Storage__MinIO__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("Storage__MinIO__AccessKey", "nervminio")
    .WithEnvironment("Storage__MinIO__SecretKey", "nervminio")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(redis)
    .WaitFor(redis)
    .WaitFor(minio)
    .WaitFor(otelCollector);

var gateway = builder.AddProject<Projects.Nerv_IIP_PlatformGateway_Web>("gateway")
    .WithEnvironment("AppHub__BaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("Iam__Jwt__SigningKey", LocalJwtSigningKey)
    .WithEnvironment("Ops__BaseUrl", ops.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(apphub)
    .WithReference(iam)
    .WithReference(ops)
    .WithReference(fileStorage)
    .WithReference(redis)
    .WaitFor(apphub)
    .WaitFor(iam)
    .WaitFor(ops)
    .WaitFor(fileStorage)
    .WaitFor(redis);

var connectorHost = builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host")
    .WithEnvironment("ConnectorHost__CycleSeconds", "1")
    .WithEnvironment("Platform__AppHubBaseUrl", apphub.GetEndpoint("http"))
    .WithEnvironment("Platform__OpsBaseUrl", ops.GetEndpoint("http"))
    .WithReference(apphub)
    .WithReference(ops)
    .WithReference(iam)
    .WaitFor(apphub)
    .WaitFor(ops)
    .WaitFor(iam);

builder.AddViteApp("console", "../../../frontend/apps/console")
    .WithPnpm()
    .WithEnvironment("NERV_IIP_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WaitFor(gateway)
    .WaitFor(connectorHost);

builder.Build().Run();
