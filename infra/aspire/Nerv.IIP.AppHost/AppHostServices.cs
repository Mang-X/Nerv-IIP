using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

internal sealed partial class AppHostComposition
{
    private ServiceResources RegisterServices(InfrastructureResources infrastructure)
    {
        var appHubDatabase = infrastructure.AppHubDatabase;
        var iamDatabase = infrastructure.IamDatabase;
        var opsDatabase = infrastructure.OpsDatabase;
        var fileStorageDatabase = infrastructure.FileStorageDatabase;
        var notificationDatabase = infrastructure.NotificationDatabase;
        var businessMasterDataDatabase = infrastructure.BusinessMasterDataDatabase;
        var businessProductEngineeringDatabase = infrastructure.BusinessProductEngineeringDatabase;
        var businessInventoryDatabase = infrastructure.BusinessInventoryDatabase;
        var businessQualityDatabase = infrastructure.BusinessQualityDatabase;
        var businessMesDatabase = infrastructure.BusinessMesDatabase;
        var businessDemandPlanningDatabase = infrastructure.BusinessDemandPlanningDatabase;
        var businessBarcodeLabelDatabase = infrastructure.BusinessBarcodeLabelDatabase;
        var businessApprovalDatabase = infrastructure.BusinessApprovalDatabase;
        var businessWmsDatabase = infrastructure.BusinessWmsDatabase;
        var businessIndustrialTelemetryDatabase = infrastructure.BusinessIndustrialTelemetryDatabase;
        var businessMaintenanceDatabase = infrastructure.BusinessMaintenanceDatabase;
        var businessErpDatabase = infrastructure.BusinessErpDatabase;
        var businessSchedulingDatabase = infrastructure.BusinessSchedulingDatabase;
        var redis = infrastructure.Redis;
        var rabbitmq = infrastructure.Rabbitmq;
        var minio = infrastructure.Minio;

        // Development 使用库存产品基线种子的 loc-* 库位；在手量由业务流程建立。
        // 非 Development 下 AppHost 不再无条件下发它们（#2008）：部署方要么用与服务同名的配置键显式
        // 给出真实站点/库位（例如 Inventory__SiteCode、MaterialIssue__SourceLocationCode），要么这些键
        // 根本不下发，由服务侧 fail-closed 自己暴露——WMS 领料进死信 unresolved-location，MES 抛
        // MATERIAL_SUPPLY_LOCATION_UNCONFIGURED / FINISHED_GOODS_LOCATION_UNCONFIGURED。
        // 门控判据与所有项目资源的运行环境、Development-only 注入同源：AppHost 自身的 EnvironmentName。

        var apphub = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5101, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
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

        var iam = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Iam_Web>("iam")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5102, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Iam__Seed__Enabled", developmentOnlyEnabledValue)
            .WithEnvironment("Iam__Seed__AdminPassword", iamSeedAdminPassword)
            .WithEnvironment("Iam__Seed__ConnectorHostSecret", iamSeedConnectorHostSecret)
            .WithEnvironment("Iam__Seed__DemoWorkerPassword", iamSeedDemoWorkerPassword)
            .WithEnvironment("Iam__Jwt__SigningKeys__0__Kid", iamJwtSigningKeyId)
            .WithEnvironment("Iam__Jwt__SigningKeys__0__PrivateKeyPem", iamJwtPrivateKeyPem)
            .WithEnvironment("Iam__Secrets__Pepper", iamSecretsPepper)
            .WithEnvironment("Iam__EnterpriseIdentity__Mfa__DevelopmentCode", iamEnterpriseIdentityMfaCode)
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(iamDatabase, "IamDb")
            .WithReference(redis)
            .WaitFor(iamDatabase)
            .WaitFor(redis);

        var ops = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5103, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
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

        // 上传字节走 tus：complete 的提交证据由 FileStorage 从本地 tus 盘读回，默认的 server-proxy 没有字节面。
        // tus 盘同时承载已 complete 文件的字节，FileStorage:Tus:RootPath 必须显式、绝对、持久（ADR 0024 §5），缺它服务拒绝启动：
        //  - 运行模式：与 fullstack 会话状态同根的本机状态目录；ephemeral 会话按 session id 隔离。
        //  - publish 模式：容器内路径 + 命名卷。挂载点取 /home/app：SDK 生成的镜像以非 root 的 app(1654) 运行，
        //    /home/app 是镜像里唯一归 app 所有的目录，命名卷首次创建时继承该属主；挂到镜像里不存在的路径会得到 root 所有、进程写不进的挂载点。
        const string FileStorageContainerDataRoot = "/home/app";
        var fileStorageTusRootPath = builder.ExecutionContext.IsPublishMode
            ? $"{FileStorageContainerDataRoot}/nerv-iip/file-storage/tus"
            : Path.Combine(LocalStateRoot(), "file-storage", SessionVolume("tus"));
        var fileStorage = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_FileStorage_Web>("file-storage")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5104, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("FileStorage__UploadProvider", "tus")
            .WithEnvironment("FileStorage__Tus__RootPath", fileStorageTusRootPath)
            .WithAnnotation(new ContainerMountAnnotation(
                "nerv-iip-file-storage",
                FileStorageContainerDataRoot,
                ContainerMountType.Volume,
                isReadOnly: false))
            .WithEnvironment("Storage__Provider", "MinIO")
            .WithEnvironment("Storage__MinIO__Endpoint", minio.GetEndpoint("api"))
            .WithEnvironment("Storage__MinIO__AccessKey", minioRootUser)
            .WithEnvironment("Storage__MinIO__SecretKey", minioRootPassword)
            .WithEnvironment("Storage__MinIO__ComplianceArchiveBucket", "nerv-iip-compliance-archive")
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(fileStorageDatabase, "FileStorageDb")
            .WithReference(redis)
            .WaitFor(fileStorageDatabase)
            .WaitFor(redis)
            .WaitFor(minio);

        var notification = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Notification_Web>("notification")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5106, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
            // 设备预警可达工作台：Notification 对 alarm-raised 的收件人默认回退 role:maintenance，
            // 但消息面按 principalRef（user:user-admin）精确匹配、没有角色展开层——演示管理员必须显式列入。
            .WithEnvironment("IndustrialTelemetry__AlarmNotification__RecipientRefs__0", "user:user-admin")
            .WithEnvironment("IndustrialTelemetry__AlarmNotification__RecipientRefs__1", "role:maintenance")
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
            .WithReference(iam)
            .WithReference(notificationDatabase, "NotificationDb")
            .WaitFor(apphub)
            .WaitFor(iam)
            .WaitFor(notificationDatabase);
        notification = WithRedisMessagingTransport(notification);
        if (rabbitmq is not null)
        {
            notification = notification
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        var businessMasterData = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_MasterData_Web>("business-master-data")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5107, name: "http")
            .WithEnvironment("Iam__Jwt__JwksJson", iamJwtJwksJson)
            .WithEnvironment("MasterData__SchedulingTimeContext__SigningKey", schedulingTimeContextSigningKey)
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("Walkthrough__Seed__Enabled", walkthroughSeedEnabledValue)
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

        var businessProductEngineering = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_ProductEngineering_Web>("business-product-engineering")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5108, name: "http")
            .WithEnvironment("Iam__Jwt__JwksJson", iamJwtJwksJson)
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("Walkthrough__Seed__Enabled", walkthroughSeedEnabledValue)
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

        // MasterData 删除防护需要反查 ProductEngineering 的引用占用（HttpProductEngineeringReferenceUsageChecker）。
        // 缺这一行时 MasterData 回退固定端口 5108，在动态端口的 ephemeral 会话上必打错端口。
        // 与全仓其余「跨服务 BaseUrl」接线同构：WithEnvironment + WithReference 成对出现，
        // 服务发现名与显式基址两条路都通（#1317）。
        // 但**不加 WaitFor**：ProductEngineering 已 WaitFor(businessMasterData)，反向等待会成环。
        businessMasterData = businessMasterData
            .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
            .WithReference(businessProductEngineering);

        var businessInventory = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Inventory_Web>("business-inventory")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5109, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            // Inventory 的 SKU 效期策略读取走 MasterData HTTP（HttpInventorySkuExpiryPolicyProvider）；
            // 不注入则回退固定端口 5107，ephemeral 会话上必打错端口。
            .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessInventoryDatabase, "PostgreSQL")
            .WithReference(businessMasterData)
            .WaitFor(businessInventoryDatabase)
            .WaitFor(businessMasterData);
        businessInventory = WithRedisMessagingTransport(businessInventory);
        if (rabbitmq is not null)
        {
            businessInventory = businessInventory
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        var businessQuality = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Quality_Web>("business-quality")))
            .WithEnvironment("FileStorage__BaseUrl", fileStorage.GetEndpoint("http"))
            .WithEnvironment("Iam__Jwt__JwksJson", iamJwtJwksJson)
            .WithReference(fileStorage)
            .WaitFor(fileStorage)
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5110, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
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

        var businessMes = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Mes_Web>("business-mes")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5111, name: "http")
            .WithHttpHealthCheck("/swagger/v1/swagger.json")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
            .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
            .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
            .WithEnvironment("Quality__BaseUrl", businessQuality.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessMesDatabase, "PostgreSQL")
            .WithReference(businessMasterData)
            .WithReference(businessProductEngineering)
            .WithReference(businessInventory)
            .WithReference(businessQuality)
            .WaitFor(businessMesDatabase)
            .WaitFor(businessMasterData)
            .WaitFor(businessProductEngineering)
            .WaitFor(businessInventory)
            .WaitFor(businessQuality);
        // 单一权威站点键：齐套可用量查询与线边过账都从它回落，避免三份语义重叠的站点配置。
        // 只有真正的多站点部署才需要额外设置 Inventory__SiteCodes__N（跨站点求可用量）。
        businessMes = WithDeploymentEnvironment(
            businessMes,
            "Inventory__SiteCode",
            DeploymentWarehouseLocation("Inventory:SiteCode", "SITE-001"));
        var mesSourceLocationCodes = DeploymentWarehouseLocations(
            "Inventory:SourceLocationCodes",
            ["loc-raw-01", "loc-semi-01", "loc-fg-01"]);
        for (var sourceLocationIndex = 0; sourceLocationIndex < mesSourceLocationCodes.Count; sourceLocationIndex++)
        {
            businessMes = businessMes.WithEnvironment(
                "Inventory__SourceLocationCodes__" +
                    sourceLocationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                mesSourceLocationCodes[sourceLocationIndex]);
        }

        businessMes = WithDeploymentEnvironment(
            businessMes,
            "Inventory__LineSideLocationCode",
            DeploymentWarehouseLocation(
                "Inventory:LineSideLocationCode",
                "loc-line-01"));
        // 完工入库目标库位（#1331）：成品仓库位同样取种子事实，站点复用上面的权威 Inventory__SiteCode，
        // 不再让 MES 硬编码 finished-goods/receiving 命名空间。
        businessMes = WithDeploymentEnvironment(
            businessMes,
            "Inventory__FinishedGoodsLocationCode",
            DeploymentWarehouseLocation(
                "Inventory:FinishedGoodsLocationCode",
                "loc-fg-01"));
        businessMes = WithRedisMessagingTransport(businessMes);
        if (rabbitmq is not null)
        {
            businessMes = businessMes
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        // Inventory 的成品入库权威成本读取回查 MES；MES 已等待 Inventory，反向只回填服务发现，
        // 不加 WaitFor，避免 Aspire 资源依赖成环并避免 ephemeral 会话回落到固定端口 5111。
        businessInventory = businessInventory
            .WithEnvironment("Mes__BaseUrl", businessMes.GetEndpoint("http"))
            .WithReference(businessMes);

        var businessDemandPlanning = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_DemandPlanning_Web>("business-demand-planning")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5112, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
            .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
            .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
            .WithEnvironment("Mes__BaseUrl", businessMes.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessDemandPlanningDatabase, "PostgreSQL")
            .WithReference(businessMasterData)
            .WithReference(businessProductEngineering)
            .WithReference(businessInventory)
            .WithReference(businessMes)
            .WaitFor(businessDemandPlanningDatabase)
            .WaitFor(businessMasterData)
            .WaitFor(businessProductEngineering)
            .WaitFor(businessInventory)
            .WaitFor(businessMes);
        businessDemandPlanning = WithRedisMessagingTransport(businessDemandPlanning);
        if (rabbitmq is not null)
        {
            businessDemandPlanning = businessDemandPlanning
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        var businessBarcodeLabel = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_BarcodeLabel_Web>("business-barcode-label")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5113, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("FileStorage__BaseUrl", fileStorage.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessBarcodeLabelDatabase, "PostgreSQL")
            .WithReference(fileStorage)
            .WaitFor(businessBarcodeLabelDatabase)
            .WaitFor(fileStorage);
        if (localDevelopmentAppHost)
        {
            businessBarcodeLabel = businessBarcodeLabel
                .WithEnvironment("LabelPrinter__Mode", "simulated")
                .WithEnvironment("LabelPrinter__Printers__0__Id", "development-label-printer")
                .WithEnvironment("LabelPrinter__Printers__0__Name", "开发模拟打印机")
                .WithEnvironment("LabelPrinter__Printers__0__Enabled", "true");
        }
        else
        {
            var barcodeLabelPrinterId = builder.AddParameter("barcode-label-printer-id");
            var barcodeLabelPrinterHost = builder.AddParameter("barcode-label-printer-host");
            var barcodeLabelPrinterPort = builder.AddParameter("barcode-label-printer-port");
            var barcodeLabelPrinterConnectTimeout = builder.AddParameter("barcode-label-printer-connect-timeout-seconds");
            var barcodeLabelPrinterWriteTimeout = builder.AddParameter("barcode-label-printer-write-timeout-seconds");
            var barcodeLabelPrinterDpi = builder.AddParameter("barcode-label-printer-dpi");
            var barcodeLabelPrinterCapabilities = builder.AddParameter("barcode-label-printer-capabilities");
            businessBarcodeLabel = businessBarcodeLabel
                .WithEnvironment("LabelPrinter__Mode", "zpl-tcp")
                .WithEnvironment("LabelPrinter__Printers__0__Id", barcodeLabelPrinterId)
                .WithEnvironment("LabelPrinter__Printers__0__Host", barcodeLabelPrinterHost)
                .WithEnvironment("LabelPrinter__Printers__0__Port", barcodeLabelPrinterPort)
                .WithEnvironment("LabelPrinter__Printers__0__ConnectTimeoutSeconds", barcodeLabelPrinterConnectTimeout)
                .WithEnvironment("LabelPrinter__Printers__0__WriteTimeoutSeconds", barcodeLabelPrinterWriteTimeout)
                .WithEnvironment("LabelPrinter__Printers__0__Dpi", barcodeLabelPrinterDpi)
                .WithEnvironment("LabelPrinter__Printers__0__Language", "zpl")
                .WithEnvironment("LabelPrinter__Printers__0__Capabilities", barcodeLabelPrinterCapabilities)
                .WithEnvironment("LabelPrinter__Printers__0__Enabled", "true");
        }
        businessBarcodeLabel = WithRedisMessagingTransport(businessBarcodeLabel);
        if (rabbitmq is not null)
        {
            businessBarcodeLabel = businessBarcodeLabel
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        var businessApproval = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Approval_Web>("business-approval")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5114, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
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

        // Inventory 盘点调整审批链（HttpStockCountApprovalClient）、ProductEngineering 工程审批校验
        // （HttpEngineeringApprovalVerifier）与 Mes 工序开工审批（HttpMesOperationTaskStartApprovalClient）
        // 都通过 HTTP 访问 Approval；Approval 声明晚于三者，
        // 只能在此回填端点环境变量，否则 ephemeral 会话回退固定端口 5114 必打错端口。
        businessInventory = businessInventory
            .WithEnvironment("Approval__BaseUrl", businessApproval.GetEndpoint("http"))
            .WithReference(businessApproval)
            .WaitFor(businessApproval);
        businessProductEngineering = businessProductEngineering
            .WithEnvironment("Approval__BaseUrl", businessApproval.GetEndpoint("http"))
            .WithReference(businessApproval)
            .WaitFor(businessApproval);
        businessMes = businessMes
            .WithEnvironment("Approval__BaseUrl", businessApproval.GetEndpoint("http"))
            .WithReference(businessApproval)
            .WaitFor(businessApproval);

        var businessWms = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Wms_Web>("business-wms")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5115, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("Wms__WorkPoolSeed__Enabled", wmsWorkPoolSeedEnabled ? "true" : "false")
            .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessWmsDatabase, "PostgreSQL")
            .WithReference(businessInventory)
            .WaitFor(businessWmsDatabase)
            .WaitFor(businessInventory);
        businessWms = WithDeploymentEnvironment(
            businessWms,
            "MaterialIssue__SourceLocationCode",
            DeploymentWarehouseLocation(
                "MaterialIssue:SourceLocationCode",
                "loc-raw-01"));
        businessWms = WithDeploymentEnvironment(
            businessWms,
            "MaterialIssue__LineSideLocationCode",
            DeploymentWarehouseLocation(
                "MaterialIssue:LineSideLocationCode",
                "loc-line-01"));
        businessWms = WithRedisMessagingTransport(businessWms);
        if (rabbitmq is not null)
        {
            businessWms = businessWms
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        var businessIndustrialTelemetry = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_IndustrialTelemetry_Web>("business-industrial-telemetry")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5116, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
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

        var businessMaintenance = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Maintenance_Web>("business-maintenance")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5117, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("Maintenance__PmGeneration__Enabled", maintenancePmGenerationEnabled ? "true" : "false")
            .WithEnvironment("Maintenance__PmGeneration__OrganizationId", maintenancePmGenerationOrganizationId ?? string.Empty)
            .WithEnvironment("Maintenance__PmGeneration__EnvironmentId", maintenancePmGenerationEnvironmentId ?? string.Empty)
            .WithEnvironment("Notification__BaseUrl", notification.GetEndpoint("http"))
            .WithEnvironment("IndustrialTelemetry__BaseUrl", businessIndustrialTelemetry.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessMaintenanceDatabase, "PostgreSQL")
            .WithReference(notification)
            .WithReference(businessIndustrialTelemetry)
            .WaitFor(businessMaintenanceDatabase)
            .WaitFor(businessIndustrialTelemetry)
            .WaitFor(notification);
        if (!string.IsNullOrWhiteSpace(maintenancePmGenerationInterval))
        {
            businessMaintenance = businessMaintenance.WithEnvironment("Maintenance__PmGeneration__Interval", maintenancePmGenerationInterval);
        }
        businessMaintenance = WithRedisMessagingTransport(businessMaintenance);
        if (rabbitmq is not null)
        {
            businessMaintenance = businessMaintenance
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        var businessErp = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Erp_Web>("business-erp")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5118, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            .WithEnvironment("Walkthrough__Seed__Enabled", walkthroughSeedEnabledValue)
            .WithEnvironment("Erp__Seed__SalesOrderDemandDemo__Enabled", developmentOnlyEnabledValue)
            .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
            .WithEnvironment("Approval__BaseUrl", businessApproval.GetEndpoint("http"))
            // ERP 出/入库单取消联动 WMS（HttpWmsOutbound/InboundCancellationClient）；
            // 不注入则回退固定端口，ephemeral 会话上必打错端口。
            .WithEnvironment("Wms__BaseUrl", businessWms.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessErpDatabase, "PostgreSQL")
            .WithReference(businessMasterData)
            .WithReference(businessApproval)
            .WithReference(businessWms)
            .WithReference(iam)
            .WaitFor(businessErpDatabase)
            .WaitFor(businessMasterData)
            .WaitFor(businessApproval)
            .WaitFor(businessWms)
            .WaitFor(iam);
        // 工单成本差异、机器制造费用核对这组 ERP 端点只认 scoped caller 名单（每个令牌绑定一个
        // organization/environment），不认通用内部服务策略。BusinessGateway 用会话内部服务令牌转发，
        // 所以本地栈要把这枚令牌登记成绑定演示租户的只读调用方，否则两页恒 401（#3784）。
        // 下标用 1：0 号是 appsettings.Development.json 里的固定本地调用方，写 0 会与它逐字段合并。
        // 非 Development 的名单属于部署配置，不在这里下发。
        if (localDevelopmentAppHost)
        {
            const string gatewayScopedCaller = "Erp__MachineOverheadReconciliation__ScopedCallers__Profiles__1__";
            businessErp = businessErp
                .WithEnvironment(gatewayScopedCaller + "Name", "business-gateway")
                .WithEnvironment(gatewayScopedCaller + "BearerToken", internalServiceBearerToken)
                .WithEnvironment(gatewayScopedCaller + "Subject", "business-gateway")
                .WithEnvironment(gatewayScopedCaller + "OrganizationId", "org-001")
                .WithEnvironment(gatewayScopedCaller + "EnvironmentId", "env-dev")
                .WithEnvironment(gatewayScopedCaller + "Permissions__0", "business.erp.finance.read");
        }
        businessErp = WithRedisMessagingTransport(businessErp);
        if (rabbitmq is not null)
        {
            businessErp = businessErp
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        businessWms = businessWms
            .WithEnvironment("Erp__BaseUrl", businessErp.GetEndpoint("http"))
            .WithReference(businessErp);

        businessDemandPlanning = businessDemandPlanning
            .WithEnvironment("Erp__BaseUrl", businessErp.GetEndpoint("http"))
            .WithReference(businessErp)
            .WaitFor(businessErp);

        businessMes = businessMes
            .WithEnvironment("Erp__BaseUrl", businessErp.GetEndpoint("http"))
            .WithReference(businessErp)
            .WaitFor(businessErp);

        // Quality 通过 HTTP 查询 ERP 采购收货事实，以及返工、报废、退供、让步放行的 Approval 状态。
        // 两个基址都使用当前会话的动态端点，避免回退到固定开发端口。内部令牌已在注册 Quality 时注入。
        businessQuality = businessQuality
            .WithEnvironment("Erp__BaseUrl", businessErp.GetEndpoint("http"))
            .WithEnvironment("Approval__BaseUrl", businessApproval.GetEndpoint("http"))
            .WithReference(businessErp)
            .WithReference(businessApproval)
            .WaitFor(businessErp)
            .WaitFor(businessApproval);

        var businessScheduling = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Scheduling_Web>("business-scheduling")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5120, name: "http")
            .WithEnvironment("Persistence__Provider", "PostgreSQL")
            .WithEnvironment("Persistence__AutoMigrate", developmentOnlyEnabledValue)
            .WithEnvironment("Messaging__Provider", messagingProvider)
            // 排产「生成首版」依赖 MasterData（产能日历/工作中心）与 ProductEngineering（工艺路线）HTTP 读面；
            // 此前漏注入这两行，服务回退固定端口 5107/5108，在动态端口的 ephemeral 会话上
            // 生成首版必 500 downstream-request-failed —— GitHub #1316 的直接原因。
            .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
            .WithEnvironment("ProductEngineering__BaseUrl", businessProductEngineering.GetEndpoint("http"))
            .WithEnvironment("Mes__BaseUrl", businessMes.GetEndpoint("http"))
            .WithEnvironment("IndustrialTelemetry__BaseUrl", businessIndustrialTelemetry.GetEndpoint("http"))
            .WithEnvironment("Maintenance__BaseUrl", businessMaintenance.GetEndpoint("http"))
            // 订单紧急度留存归档（HttpOrderUrgencyArchiveStore）把归档体 POST 给 FileStorage。它直接读
            // Configuration["FileStorage:BaseUrl"] 设 HttpClient.BaseAddress（Scheduling.Web/Program.cs:114），
            // 未走 InternalServiceBaseAddress.Resolve*，因此基址门禁的需求集枚举不到它——删掉这行门禁照绿
            // （实测：UnexemptedViolations 仍为 0），而 OrderUrgencyArchiveClient.cs:55 会在运行时抛
            // 「FileStorage:BaseUrl is required for urgency retention.」。
            .WithEnvironment("FileStorage__BaseUrl", fileStorage.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(businessSchedulingDatabase, "PostgreSQL")
            .WithReference(businessMasterData)
            .WithReference(businessProductEngineering)
            .WithReference(businessMes)
            .WithReference(businessIndustrialTelemetry)
            .WithReference(businessMaintenance)
            .WithReference(fileStorage)
            .WaitFor(businessSchedulingDatabase)
            .WaitFor(businessMasterData)
            .WaitFor(businessProductEngineering)
            .WaitFor(businessMes)
            .WaitFor(businessIndustrialTelemetry)
            .WaitFor(businessMaintenance);
        businessDemandPlanning = businessDemandPlanning
            .WithEnvironment("Scheduling__BaseUrl", businessScheduling.GetEndpoint("http"))
            .WithReference(businessScheduling);

        businessScheduling = businessScheduling.WaitFor(fileStorage);
        businessScheduling = WithRedisMessagingTransport(businessScheduling);
        if (rabbitmq is not null)
        {
            businessScheduling = businessScheduling
                .WithReference(rabbitmq)
                .WaitFor(rabbitmq);
        }

        return new ServiceResources(
            apphub,
            iam,
            ops,
            fileStorage,
            notification,
            businessMasterData,
            businessProductEngineering,
            businessInventory,
            businessQuality,
            businessMes,
            businessDemandPlanning,
            businessBarcodeLabel,
            businessApproval,
            businessWms,
            businessIndustrialTelemetry,
            businessMaintenance,
            businessErp,
            businessScheduling);

        IResourceBuilder<ProjectResource> WithRedisMessagingTransport(
            IResourceBuilder<ProjectResource> project)
        {
            return useRedisMessaging
                ? project.WithReference(redis).WaitFor(redis)
                : project;
        }
    }

    private sealed record ServiceResources(
        IResourceBuilder<ProjectResource> AppHub,
        IResourceBuilder<ProjectResource> Iam,
        IResourceBuilder<ProjectResource> Ops,
        IResourceBuilder<ProjectResource> FileStorage,
        IResourceBuilder<ProjectResource> Notification,
        IResourceBuilder<ProjectResource> BusinessMasterData,
        IResourceBuilder<ProjectResource> BusinessProductEngineering,
        IResourceBuilder<ProjectResource> BusinessInventory,
        IResourceBuilder<ProjectResource> BusinessQuality,
        IResourceBuilder<ProjectResource> BusinessMes,
        IResourceBuilder<ProjectResource> BusinessDemandPlanning,
        IResourceBuilder<ProjectResource> BusinessBarcodeLabel,
        IResourceBuilder<ProjectResource> BusinessApproval,
        IResourceBuilder<ProjectResource> BusinessWms,
        IResourceBuilder<ProjectResource> BusinessIndustrialTelemetry,
        IResourceBuilder<ProjectResource> BusinessMaintenance,
        IResourceBuilder<ProjectResource> BusinessErp,
        IResourceBuilder<ProjectResource> BusinessScheduling);
}
