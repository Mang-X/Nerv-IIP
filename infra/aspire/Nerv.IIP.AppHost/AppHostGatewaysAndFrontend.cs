internal sealed partial class AppHostComposition
{
    private void RegisterGatewaysAndFrontend(InfrastructureResources infrastructure, ServiceResources services)
    {
        var redis = infrastructure.Redis;
        var victoriaLogs = infrastructure.VictoriaLogs;
        var apphub = services.AppHub;
        var iam = services.Iam;
        var ops = services.Ops;
        var fileStorage = services.FileStorage;
        var notification = services.Notification;
        var businessMasterData = services.BusinessMasterData;
        var businessProductEngineering = services.BusinessProductEngineering;
        var businessInventory = services.BusinessInventory;
        var businessQuality = services.BusinessQuality;
        var businessMes = services.BusinessMes;
        var businessDemandPlanning = services.BusinessDemandPlanning;
        var businessBarcodeLabel = services.BusinessBarcodeLabel;
        var businessApproval = services.BusinessApproval;
        var businessWms = services.BusinessWms;
        var businessIndustrialTelemetry = services.BusinessIndustrialTelemetry;
        var businessMaintenance = services.BusinessMaintenance;
        var businessErp = services.BusinessErp;
        var businessScheduling = services.BusinessScheduling;

        var gateway = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_PlatformGateway_Web>("gateway")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5100, name: "http")
            .WithEnvironment("AppHub__BaseUrl", apphub.GetEndpoint("http"))
            .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
            .WithEnvironment("Iam__Jwt__JwksJson", iamJwtJwksJson)
            .WithEnvironment("Security__Cors__AllowedOrigins", gatewayCorsAllowedOrigins)
            .WithEnvironment("Security__ForwardedHeaders__TrustedProxies", gatewayTrustedProxies)
            .WithEnvironment("Ops__BaseUrl", ops.GetEndpoint("http"))
            .WithEnvironment("Notification__BaseUrl", notification.GetEndpoint("http"))
            // PlatformGateway 的控制台文件面缺这行，客户端回落到固定端口 5104：ephemeral 会话上打不到本会话的
            // file-storage，落到端口 5104 上碰巧存在的另一套栈时会拿到 401（该栈的内部令牌是另一个随机值）。
            .WithEnvironment("FileStorage__BaseUrl", fileStorage.GetEndpoint("http"))
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
        // 控制台日志查询面（ConsoleLogEndpoints）经 AddVictoriaLogsClient 读 VictoriaLogsOptions.FromConfiguration，
        // 后者直接读 Configuration["VictoriaLogs:BaseUrl"]（NervIipObservability.cs:359），未走
        // InternalServiceBaseAddress.Resolve*，因此基址门禁的需求集枚举不到它——删掉这行门禁照绿（实测：
        // UnexemptedViolations 仍为 0）。缺这行时该方回落到内置的 http://victoria-logs:9428，那是容器网络里的
        // 名字，而 gateway 是 AddProject 的宿主进程，拿不到本会话 victoria-logs 容器实际映射出的端口。
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

        var businessGateway = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_BusinessGateway_Web>("business-gateway")))
            .WithHttpEndpoint(port: fullStackEphemeral ? null : 5119, name: "http")
            .WithEnvironment("MasterData__SchedulingTimeContext__SigningKey", schedulingTimeContextSigningKey)
            .WithEnvironment("AppHub__BaseUrl", apphub.GetEndpoint("http"))
            .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
            .WithEnvironment("Iam__Jwt__JwksJson", iamJwtJwksJson)
            .WithEnvironment("Iam__Jwt__Issuer", "nerv-iip-iam")
            .WithEnvironment("Iam__Jwt__Audience", "nerv-iip-api")
            .WithEnvironment("Security__Cors__AllowedOrigins", gatewayCorsAllowedOrigins)
            .WithEnvironment("Security__ForwardedHeaders__TrustedProxies", gatewayTrustedProxies)
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
            // BusinessGateway 的 FileStorage 门面（工程 SOP 下载、交接班附件上传下载）此前没有这行，
            // 客户端回落到固定端口 5104；在动态端口的 ephemeral 会话上必然连不上，整个文件面 500。
            .WithEnvironment("FileStorage__BaseUrl", fileStorage.GetEndpoint("http"))
            .WithEnvironment("InternalService__BearerToken", internalServiceBearerToken)
            .WithReference(apphub)
            .WithReference(iam)
            .WithReference(fileStorage)
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
            .WaitFor(fileStorage)
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

        if (platformGatewayDownloadToken is not null && businessGatewayDownloadToken is not null)
        {
            gateway.WithEnvironment("FileStorage__DownloadBearerToken", platformGatewayDownloadToken);
            businessGateway.WithEnvironment("FileStorage__DownloadBearerToken", businessGatewayDownloadToken);
        }

        if (fullStackEphemeral)
        {
            var templateAssetRetirementProofIssuer = builder.AddParameter("template-asset-retirement-proof-issuer");
            var templateAssetRetirementProofAudience = builder.AddParameter("template-asset-retirement-proof-audience");
            var templateAssetRetirementProofSecretBase64 = builder.AddParameter("template-asset-retirement-proof-secret-base64", secret: true);
            businessBarcodeLabel = businessBarcodeLabel
                .WithEnvironment("TemplateAssetRetirementProof__Issuer", templateAssetRetirementProofIssuer)
                .WithEnvironment("TemplateAssetRetirementProof__Audience", templateAssetRetirementProofAudience)
                .WithEnvironment("TemplateAssetRetirementProof__SecretBase64", templateAssetRetirementProofSecretBase64);
            businessGateway = businessGateway
                .WithEnvironment("TemplateAssetRetirementProof__Issuer", templateAssetRetirementProofIssuer)
                .WithEnvironment("TemplateAssetRetirementProof__Audience", templateAssetRetirementProofAudience)
                .WithEnvironment("TemplateAssetRetirementProof__SecretBase64", templateAssetRetirementProofSecretBase64);
        }

        var connectorHost = infrastructure.WithNervIipTelemetry(WithAppHostEnvironment(builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host")))
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

        // PublishAsStaticWebsite 是 Aspire 实验性 API（ASPIREJAVASCRIPT001）。
        // Business Console 要等两个后端的生产路由确定后再接入。
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
    }
}
