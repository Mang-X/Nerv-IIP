using Aspire.Hosting.ApplicationModel;

internal sealed partial class AppHostComposition
{
    private InfrastructureResources RegisterInfrastructure()
    {
        var postgres = WithFullStackOwnership(builder.AddPostgres("postgres"))
            .WithImageTag("19")
            // 保留已有卷名，让镜像识别旧集群并要求显式迁移。
            .WithDataVolume(SessionVolume("nerv-iip-postgres-18"));
        if (!fullStackEphemeral)
        {
            postgres.WithHostPort(15432);
        }
        if (localDevelopmentAppHost)
        {
            // 完整本地平台共享此实例，业务查询与 CAP 后台任务使用独立连接池。
            postgres.WithArgs("-c", "max_connections=300");
        }
        var appHubDatabase = postgres.AddDatabase("apphub-db", "nerv_iip_apphub");
        var iamDatabase = postgres.AddDatabase("iam-db", "nerv_iip_iam");
        var opsDatabase = postgres.AddDatabase("ops-db", "nerv_iip_ops");
        var fileStorageDatabase = postgres.AddDatabase("file-storage-db", "nerv_iip_filestorage");
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
        if (!fullStackEphemeral)
        {
            redis.WithHostPort(6379);
        }
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
        IResourceBuilder<ContainerResource>? victoriaLogs = null;
        if (useVictoriaLogs)
        {
            victoriaLogs = WithFullStackOwnership(builder.AddContainer("victoria-logs", "victoriametrics/victoria-logs", "v1.50.0"))
                .WithArgs("-storageDataPath=/victoria-logs-data", $"-retentionPeriod={victoriaLogsRetentionPeriod}")
                .WithHttpEndpoint(port: fullStackEphemeral ? null : 9428, targetPort: 9428, name: "http")
                .WithVolume(SessionVolume("nerv-iip-victoria-logs"), "/victoria-logs-data");
        }
        IResourceBuilder<ContainerResource>? otelCollector = null;
        if (useOtelCollector)
        {
            otelCollector = WithFullStackOwnership(builder.AddContainer("otel-collector", "otel/opentelemetry-collector", "0.116.1"))
                .WithArgs("--config=/etc/otelcol/config.yaml")
                .WithEnvironment("NERV_IIP_ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT", aspireDashboardOtlpHttpEndpoint)
                .WithBindMount("../../otel/otel-collector.dev.yaml", "/etc/otelcol/config.yaml", isReadOnly: true)
                .WithEndpoint(port: fullStackEphemeral ? null : 4317, targetPort: 4317, name: "otlp-grpc")
                .WithHttpEndpoint(port: fullStackEphemeral ? null : 4318, targetPort: 4318, name: "otlp-http");
        }

        return new InfrastructureResources(
            appHubDatabase,
            iamDatabase,
            opsDatabase,
            fileStorageDatabase,
            notificationDatabase,
            businessMasterDataDatabase,
            businessProductEngineeringDatabase,
            businessInventoryDatabase,
            businessQualityDatabase,
            businessMesDatabase,
            businessDemandPlanningDatabase,
            businessBarcodeLabelDatabase,
            businessApprovalDatabase,
            businessWmsDatabase,
            businessIndustrialTelemetryDatabase,
            businessMaintenanceDatabase,
            businessErpDatabase,
            businessSchedulingDatabase,
            redis,
            rabbitmq,
            minio,
            victoriaLogs,
            otelCollector);
    }

    private IResourceBuilder<T> WithFullStackOwnership<T>(
        IResourceBuilder<T> resource)
        where T : ContainerResource
    {
        return fullStackEphemeral
            ? resource.WithContainerRuntimeArgs("--label", $"com.nerv-iip.session={fullStackSessionId}")
            : resource;
    }

    private sealed record InfrastructureResources(
        IResourceBuilder<PostgresDatabaseResource> AppHubDatabase,
        IResourceBuilder<PostgresDatabaseResource> IamDatabase,
        IResourceBuilder<PostgresDatabaseResource> OpsDatabase,
        IResourceBuilder<PostgresDatabaseResource> FileStorageDatabase,
        IResourceBuilder<PostgresDatabaseResource> NotificationDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessMasterDataDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessProductEngineeringDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessInventoryDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessQualityDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessMesDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessDemandPlanningDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessBarcodeLabelDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessApprovalDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessWmsDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessIndustrialTelemetryDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessMaintenanceDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessErpDatabase,
        IResourceBuilder<PostgresDatabaseResource> BusinessSchedulingDatabase,
        IResourceBuilder<RedisResource> Redis,
        IResourceBuilder<RabbitMQServerResource>? Rabbitmq,
        IResourceBuilder<ContainerResource> Minio,
        IResourceBuilder<ContainerResource>? VictoriaLogs,
        IResourceBuilder<ContainerResource>? OtelCollector)
    {
        public IResourceBuilder<ProjectResource> WithNervIipTelemetry(
            IResourceBuilder<ProjectResource> project)
        {
            if (VictoriaLogs is not null)
            {
                project = project
                    .WithEnvironment("OpenTelemetry__Logs__Endpoint", VictoriaLogs.GetEndpoint("http"))
                    .WithEnvironment("OpenTelemetry__Logs__Path", "/insert/opentelemetry/v1/logs")
                    .WaitFor(VictoriaLogs);
            }

            if (OtelCollector is null)
            {
                return project;
            }

            return project
                .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OtelCollector.GetEndpoint("otlp-http"))
                .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
                .WaitFor(OtelCollector);
        }
    }
}
