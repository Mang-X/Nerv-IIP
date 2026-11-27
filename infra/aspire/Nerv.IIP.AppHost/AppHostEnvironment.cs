using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

internal sealed partial class AppHostComposition
{
    private readonly IDistributedApplicationBuilder builder;
    private readonly bool localDevelopmentAppHost;
    private readonly string developmentOnlyEnabledValue;
    private readonly string? fullStackSessionId;
    private readonly bool fullStackEphemeral;
    private readonly string walkthroughSeedEnabledValue;
    private readonly IResourceBuilder<ParameterResource> iamJwtSigningKeyId;
    private readonly IResourceBuilder<ParameterResource> iamJwtPrivateKeyPem;
    private readonly IResourceBuilder<ParameterResource> iamJwtJwksJson;
    private readonly IResourceBuilder<ParameterResource> iamSecretsPepper;
    private readonly IResourceBuilder<ParameterResource> iamEnterpriseIdentityMfaCode;
    private readonly IResourceBuilder<ParameterResource> schedulingTimeContextSigningKey;
    private readonly IResourceBuilder<ParameterResource> internalServiceBearerToken;
    private readonly IResourceBuilder<ParameterResource> minioRootUser;
    private readonly IResourceBuilder<ParameterResource> minioRootPassword;
    private readonly IResourceBuilder<ParameterResource> redisPassword;
    private readonly IResourceBuilder<ParameterResource> iamSeedAdminPassword;
    private readonly IResourceBuilder<ParameterResource> iamSeedConnectorHostSecret;
    private readonly string iamSeedDemoWorkerPassword;
    private readonly bool wmsWorkPoolSeedEnabled;
    private readonly IResourceBuilder<ParameterResource> connectorIngestionTokenSigningKey;
    private readonly string messagingProvider;
    private readonly bool useRabbitMq;
    private readonly bool useRedisMessaging;
    private readonly bool useOtelCollector;
    private readonly bool useVictoriaLogs;
    private readonly string aspireDashboardOtlpHttpEndpoint;
    private readonly string victoriaLogsRetentionPeriod;
    private readonly string connectorHostId;
    private readonly string connectorHostOrganizationId;
    private readonly string connectorHostEnvironmentId;
    private readonly bool connectorHealthAcceptanceEnabled;
    private readonly bool maintenancePmGenerationEnabled;
    private readonly string? maintenancePmGenerationOrganizationId;
    private readonly string? maintenancePmGenerationEnvironmentId;
    private readonly string? maintenancePmGenerationInterval;
    private readonly string gatewayCorsAllowedOrigins;
    private readonly string gatewayTrustedProxies;

    public AppHostComposition(IDistributedApplicationBuilder builder)
    {
        this.builder = builder;
        const string LocalDevelopmentEnvironment = "Development";
        localDevelopmentAppHost = string.Equals(
            builder.Environment.EnvironmentName,
            LocalDevelopmentEnvironment,
            StringComparison.OrdinalIgnoreCase);
        developmentOnlyEnabledValue = localDevelopmentAppHost ? "true" : "false";

        fullStackSessionId = Environment.GetEnvironmentVariable("NERV_IIP_SESSION_ID");
        fullStackEphemeral = string.Equals(
            Environment.GetEnvironmentVariable("NERV_IIP_EPHEMERAL"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        // 人工走查最小数据默认随本地 AppHost 启用，并可独立关闭。
        var walkthroughSeedEnabled = localDevelopmentAppHost &&
            !string.Equals(
                Environment.GetEnvironmentVariable("NERV_IIP_WALKTHROUGH_SEED"),
                "false",
                StringComparison.OrdinalIgnoreCase);
        walkthroughSeedEnabledValue = walkthroughSeedEnabled ? "true" : "false";

        if (fullStackEphemeral &&
            (string.IsNullOrWhiteSpace(fullStackSessionId) ||
             !Regex.IsMatch(fullStackSessionId, "^nerv-[a-f0-9]{4}-[a-f0-9]{6}$", RegexOptions.CultureInvariant)))
        {
            throw new InvalidOperationException("NERV_IIP_EPHEMERAL=true requires a validated NERV_IIP_SESSION_ID.");
        }

        builder.AddDockerComposeEnvironment("compose");

        iamJwtSigningKeyId = builder.AddParameter("iam-jwt-signing-key-id", secret: true);
        iamJwtPrivateKeyPem = builder.AddParameter("iam-jwt-private-key-pem", secret: true);
        iamJwtJwksJson = builder.AddParameter("iam-jwt-jwks-json", secret: true);
        iamSecretsPepper = builder.AddParameter("iam-secrets-pepper", secret: true);
        iamEnterpriseIdentityMfaCode = builder.AddParameter("iam-enterprise-identity-mfa-code", secret: true);
        schedulingTimeContextSigningKey = builder.AddParameter("scheduling-time-context-signing-key", new GenerateParameterDefault { MinLength = 64 }, secret: true);
        internalServiceBearerToken = builder.AddParameter("internal-service-bearer-token", secret: true);
        minioRootUser = builder.AddParameter("minio-root-user", secret: true);
        minioRootPassword = builder.AddParameter("minio-root-password", secret: true);
        redisPassword = builder.AddParameter("redis-password", secret: true);
        iamSeedAdminPassword = builder.AddParameter("iam-seed-admin-password", secret: true);
        iamSeedConnectorHostSecret = builder.AddParameter("iam-seed-connector-host-secret", secret: true);
        // 可选：PDA 演示工人统一口令（缺省为空 = 不开通演示工人账号），不建为必填 Parameter 以免阻塞常规启动。
        iamSeedDemoWorkerPassword = builder.Configuration["Parameters:iam-seed-demo-worker-password"] ?? string.Empty;
        // 仅向 WMS 下发非敏感的显式种子开关；工人口令仍只提供给 IAM。
        wmsWorkPoolSeedEnabled = builder.Configuration.GetValue(
            "Wms:WorkPoolSeed:Enabled",
            !string.IsNullOrWhiteSpace(iamSeedDemoWorkerPassword));
        connectorIngestionTokenSigningKey = builder.AddParameter("connector-ingestion-token-signing-key", secret: true);
        messagingProvider = builder.Configuration["Messaging:Provider"] ?? "InMemory";
        useRabbitMq = string.Equals(messagingProvider, "RabbitMQ", StringComparison.OrdinalIgnoreCase);
        useRedisMessaging = string.Equals(messagingProvider, "Redis", StringComparison.OrdinalIgnoreCase);
        if (!localDevelopmentAppHost && !useRabbitMq && !useRedisMessaging)
        {
            throw new InvalidOperationException("Messaging:Provider must be Redis or RabbitMQ outside Development.");
        }
        useOtelCollector = builder.Configuration.GetValue("Observability:UseCollector", false);
        useVictoriaLogs = builder.Configuration.GetValue("Observability:VictoriaLogs:Enabled", true);
        aspireDashboardOtlpHttpEndpoint = builder.Configuration["Observability:AspireDashboardOtlpHttpEndpoint"] ?? "http://host.docker.internal:18890";
        victoriaLogsRetentionPeriod = builder.Configuration["Observability:VictoriaLogs:RetentionPeriod"] ?? "30d";
        connectorHostId = DeploymentRequiredValue("ConnectorHost:ConnectorHostId", "connector-host-001");
        connectorHostOrganizationId = DeploymentRequiredValue("ConnectorHost:OrganizationId", "org-001");
        connectorHostEnvironmentId = DeploymentRequiredValue("ConnectorHost:EnvironmentId", "env-dev");
        connectorHealthAcceptanceEnabled = builder.Configuration.GetValue("ConnectorHealthAcceptance:Enabled", false);
        maintenancePmGenerationEnabled = builder.Configuration.GetValue("Maintenance:PmGeneration:Enabled", false);
        maintenancePmGenerationOrganizationId = builder.Configuration["Maintenance:PmGeneration:OrganizationId"];
        maintenancePmGenerationEnvironmentId = builder.Configuration["Maintenance:PmGeneration:EnvironmentId"];
        maintenancePmGenerationInterval = builder.Configuration["Maintenance:PmGeneration:Interval"];
        gatewayCorsAllowedOrigins = builder.Configuration["Security:Cors:AllowedOrigins"];
        if (string.IsNullOrWhiteSpace(gatewayCorsAllowedOrigins))
        {
            gatewayCorsAllowedOrigins = localDevelopmentAppHost
                ? "http://localhost:5105,http://localhost:5125,http://localhost:5128"
                : throw new InvalidOperationException("Security:Cors:AllowedOrigins is required outside Development.");
        }

        gatewayTrustedProxies = DeploymentRequiredValue("Security:ForwardedHeaders:TrustedProxies", string.Empty);
    }

    private string SessionVolume(string persistentName) =>
        fullStackEphemeral ? $"{persistentName}-{fullStackSessionId}" : persistentName;

    // 与 scripts/lib/FullStackSessionState.ps1 的 Get-NervFullStackStateRoot 同一推导，本机项目资源的持久数据落在这里。
    private static string LocalStateRoot()
    {
        var configured = Environment.GetEnvironmentVariable("NERV_IIP_FULLSTACK_STATE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nerv-IIP");
        }

        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var stateBase = string.IsNullOrWhiteSpace(xdgStateHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state")
            : xdgStateHome;
        return Path.Combine(stateBase, "nerv-iip");
    }

    private string DeploymentRequiredValue(string configurationKey, string developmentFallback)
    {
        var configured = builder.Configuration[configurationKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return localDevelopmentAppHost
            ? developmentFallback
            : throw new InvalidOperationException($"{configurationKey} is required outside Development.");
    }

    // 单值仓储位置：显式配置优先；未配置时只有 Development 回落到演示种子值，其他环境返回 null
    // 表示「不下发」，让服务侧 fail-closed 生效（#2008）。
    private string? DeploymentWarehouseLocation(string configurationKey, string developmentSeedValue)
    {
        var configured = builder.Configuration[configurationKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return localDevelopmentAppHost ? developmentSeedValue : null;
    }

    // 多值仓储位置：既接受 `Key:N` 索引形式，也接受逗号/分号分隔的一串；都没有时只有 Development
    // 回落到演示种子候选库位，其他环境返回空集合表示「一个都不下发」（#2008）。
    private IReadOnlyList<string> DeploymentWarehouseLocations(string configurationKey, string[] developmentSeedValues)
    {
        var indexed = builder.Configuration.GetSection(configurationKey)
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        if (indexed.Length > 0)
        {
            return indexed;
        }

        var delimited = builder.Configuration[configurationKey];
        if (!string.IsNullOrWhiteSpace(delimited))
        {
            var values = delimited.Split(
                [',', ';'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (values.Length > 0)
            {
                return values;
            }
        }

        return localDevelopmentAppHost ? developmentSeedValues : [];
    }

    private static IResourceBuilder<ProjectResource> WithDeploymentEnvironment(
        IResourceBuilder<ProjectResource> project,
        string name,
        string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? project : project.WithEnvironment(name, value);
    }

    private IResourceBuilder<ProjectResource> WithAppHostEnvironment(
        IResourceBuilder<ProjectResource> project)
    {
        return project
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
            .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName);
    }
}
