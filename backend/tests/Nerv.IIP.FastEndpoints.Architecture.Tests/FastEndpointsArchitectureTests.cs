using System.Text.RegularExpressions;

namespace Nerv.IIP.FastEndpoints.Architecture.Tests;

public sealed class FastEndpointsArchitectureTests
{
    public static TheoryData<string> PlatformWebProjects => new()
    {
        "backend/services/Iam/src/Nerv.IIP.Iam.Web",
        "backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web",
        "backend/services/AppHub/src/Nerv.IIP.AppHub.Web",
        "backend/services/Ops/src/Nerv.IIP.Ops.Web",
        "backend/services/Notification/src/Nerv.IIP.Notification.Web",
        "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web"
    };

    [Theory]
    [MemberData(nameof(PlatformWebProjects))]
    public void Platform_web_projects_use_fastendpoints_not_minimal_api_maps(string projectDirectory)
    {
        var root = FindRepositoryRoot();
        var fullProjectDirectory = Path.Combine(root, projectDirectory);
        var programText = File.ReadAllText(Path.Combine(fullProjectDirectory, "Program.cs"));
        var projectText = File.ReadAllText(Directory.GetFiles(fullProjectDirectory, "*.csproj").Single());
        var endpointFiles = Directory.Exists(Path.Combine(fullProjectDirectory, "Endpoints"))
            ? Directory.GetFiles(Path.Combine(fullProjectDirectory, "Endpoints"), "*Endpoint.cs", SearchOption.AllDirectories)
            : [];

        Assert.Contains("AddFastEndpoints", programText);
        Assert.Contains("UseFastEndpoints", programText);
        Assert.DoesNotContain(".MapGet(", programText);
        Assert.DoesNotContain(".MapPost(", programText);
        Assert.Contains("FastEndpoints", projectText);
        Assert.NotEmpty(endpointFiles);
        Assert.All(endpointFiles, file => Assert.Contains("FastEndpoints", File.ReadAllText(file)));
    }

    public static TheoryData<string> ResponseDataWebProjects => new()
    {
        "backend/services/Iam/src/Nerv.IIP.Iam.Web",
        "backend/services/AppHub/src/Nerv.IIP.AppHub.Web",
        "backend/services/Ops/src/Nerv.IIP.Ops.Web",
        "backend/services/Notification/src/Nerv.IIP.Notification.Web",
        "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web"
    };

    private static readonly string[] CommandLockWebProjectDirectories =
    [
        "backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web",
        "backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web",
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web",
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web",
        "backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web",
        "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web",
        "backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web",
        "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web",
        "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web",
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web",
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web",
        "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web",
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web"
    ];

    public static TheoryData<string> CommandLockWebProjects
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var projectDirectory in CommandLockWebProjectDirectories)
            {
                data.Add(projectDirectory);
            }

            return data;
        }
    }

    public static TheoryData<string> CapUnitOfWorkWebProjects => new()
    {
        "backend/services/AppHub/src/Nerv.IIP.AppHub.Web",
        "backend/services/Ops/src/Nerv.IIP.Ops.Web",
        "backend/services/Notification/src/Nerv.IIP.Notification.Web",
        "backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web",
        "backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web",
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web",
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web",
        "backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web",
        "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web",
        "backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web",
        "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web",
        "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web",
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web",
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web",
        "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web",
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web"
    };

    public static TheoryData<string> BusinessWebProjects => new()
    {
        "backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web",
        "backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web",
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web",
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web",
        "backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web",
        "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web",
        "backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web",
        "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web",
        "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web",
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web",
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web",
        "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web",
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web"
    };

    public static TheoryData<string> LocalPostgreSqlAppHostResources => new()
    {
        "apphub",
        "iam",
        "ops",
        "notification",
        "businessMasterData",
        "businessProductEngineering",
        "businessInventory",
        "businessQuality",
        "businessMes",
        "businessDemandPlanning",
        "businessBarcodeLabel",
        "businessApproval",
        "businessWms",
        "businessIndustrialTelemetry",
        "businessMaintenance",
        "businessErp",
        "businessScheduling"
    };

    [Theory]
    [MemberData(nameof(ResponseDataWebProjects))]
    public void Platform_web_projects_use_response_data_and_known_exception_middleware(string projectDirectory)
    {
        var root = FindRepositoryRoot();
        var fullProjectDirectory = Path.Combine(root, projectDirectory);
        var programText = File.ReadAllText(Path.Combine(fullProjectDirectory, "Program.cs"));
        var sourceFiles = Directory.GetFiles(fullProjectDirectory, "*.cs", SearchOption.AllDirectories);

        Assert.Contains("UseKnownExceptionHandler", programText);
        Assert.All(sourceFiles, file => Assert.DoesNotContain("WriteAsJsonAsync", File.ReadAllText(file)));
    }

    [Theory]
    [MemberData(nameof(CommandLockWebProjects))]
    public void Command_lock_services_register_distributed_lock(string projectDirectory)
    {
        var root = FindRepositoryRoot();
        var fullProjectDirectory = Path.Combine(root, projectDirectory);
        var programText = File.ReadAllText(Path.Combine(fullProjectDirectory, "Program.cs"));
        var projectText = File.ReadAllText(Directory.GetFiles(fullProjectDirectory, "*.csproj").Single());
        var usesSharedRegistration = programText.Contains("AddNervIipCommandLocking", StringComparison.Ordinal);
        var usesSharedBehavior = programText.Contains(
            "AddOpenBehavior(typeof(NervIipCommandLockBehavior<,>))",
            StringComparison.Ordinal);

        if (usesSharedRegistration || usesSharedBehavior)
        {
            Assert.True(
                usesSharedRegistration && usesSharedBehavior,
                "Shared command-lock services must register AddNervIipCommandLocking and NervIipCommandLockBehavior together.");
            Assert.Contains(
                @"common\DistributedLocking\Nerv.IIP.DistributedLocking\Nerv.IIP.DistributedLocking.csproj",
                projectText);
        }
        else
        {
            Assert.True(
                programText.Contains("AddCommandLockBehavior", StringComparison.Ordinal)
                    || programText.Contains("AddOpenBehavior(typeof(MaintenanceCommandLockBehavior<,>))", StringComparison.Ordinal),
                "Command-lock services must register the built-in command-lock behavior or the Maintenance lock-loss-aware behavior.");
            Assert.Contains("AddInMemoryDistributedLock", programText);
        }
    }

    [Fact]
    public void Shared_command_lock_web_projects_are_registered_for_governance()
    {
        var root = FindRepositoryRoot();
        var sharedCommandLockProjects = Directory
            .GetFiles(Path.Combine(root, "backend"), "Program.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var programText = File.ReadAllText(file);
                return programText.Contains("AddNervIipCommandLocking", StringComparison.Ordinal)
                    || programText.Contains("AddOpenBehavior(typeof(NervIipCommandLockBehavior<,>))", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(root, Path.GetDirectoryName(file)!).Replace('\\', '/'))
            .OrderBy(projectDirectory => projectDirectory, StringComparer.Ordinal)
            .ToArray();
        var unregisteredProjects = sharedCommandLockProjects
            .Except(CommandLockWebProjectDirectories, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unregisteredProjects.Length == 0,
            $"Shared command-lock Web projects must be registered in {nameof(CommandLockWebProjects)}: {string.Join(", ", unregisteredProjects)}");
    }

    [Theory]
    [MemberData(nameof(CapUnitOfWorkWebProjects))]
    public void Cap_unit_of_work_services_register_cap_transaction_factory(string projectDirectory)
    {
        var root = FindRepositoryRoot();
        var fullProjectDirectory = Path.Combine(root, projectDirectory);
        var programText = File.ReadAllText(Path.Combine(fullProjectDirectory, "Program.cs"));
        var sourceText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(fullProjectDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.Contains("UseCap<ApplicationDbContext>", sourceText);
        Assert.Contains("AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>", programText);
    }

    [Theory]
    [MemberData(nameof(BusinessWebProjects))]
    public void Business_web_projects_use_shared_observability_registration(string projectDirectory)
    {
        var root = FindRepositoryRoot();
        var fullProjectDirectory = Path.Combine(root, projectDirectory);
        var programText = File.ReadAllText(Path.Combine(fullProjectDirectory, "Program.cs"));
        var projectText = File.ReadAllText(Directory.GetFiles(fullProjectDirectory, "*.csproj").Single());

        Assert.Contains("using Nerv.IIP.Observability;", programText);
        Assert.Contains("AddNervIipObservability", programText);
        Assert.Contains("UseNervIipCorrelation", programText);
        Assert.DoesNotContain("using Serilog", programText);
        Assert.DoesNotContain("UseSerilog", programText);
        Assert.DoesNotContain("Log.Logger", programText);
        Assert.Contains("Nerv.IIP.Observability.csproj", projectText);
        Assert.DoesNotContain("PackageReference Include=\"Serilog.AspNetCore\"", projectText);
        Assert.DoesNotContain("PackageReference Include=\"Serilog.Enrichers.ClientInfo\"", projectText);
        Assert.DoesNotContain("PackageReference Include=\"Serilog.Sinks.OpenTelemetry\"", projectText);
    }

    [Fact]
    public void Aspire_apphost_covers_platform_services_and_real_infrastructure()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));
        var projectText = File.ReadAllText(Path.Combine(appHostDirectory, "Nerv.IIP.AppHost.csproj"));
        var composeText = File.ReadAllText(Path.Combine(root, "infra", "docker-compose.dev.yml"));
        var collectorConfig = Path.Combine(root, "infra", "otel", "otel-collector.dev.yaml");

        Assert.Contains("Projects.Nerv_IIP_Iam_Web", programText);
        Assert.Contains("Projects.Nerv_IIP_FileStorage_Web", programText);
        Assert.Contains("Projects.Nerv_IIP_Notification_Web", programText);
        Assert.Contains("WithHttpEndpoint(port: fullStackEphemeral ? null : 5106", programText);
        Assert.Contains("Notification__BaseUrl", programText);
        Assert.Contains("AddContainer(\"minio\"", programText);
        Assert.Contains("AddContainer(\"victoria-logs\"", programText);
        Assert.Contains("victoriametrics/victoria-logs", programText);
        Assert.Contains("v1.50.0", programText);
        Assert.Contains("nerv-iip-victoria-logs", programText);
        Assert.Contains("OpenTelemetry__Logs__Endpoint", programText);
        Assert.Contains("OpenTelemetry__Logs__Path", programText);
        Assert.Contains("VictoriaLogs__BaseUrl", programText);
        Assert.Contains("VictoriaLogs__Enabled", programText);
        var normalizedProgramText = programText.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".WithEnvironment(\"OpenTelemetry__Protocol\", \"HttpProtobuf\")\n            .WithEnvironment(\"OpenTelemetry__Logs__Endpoint\"",
            normalizedProgramText);
        Assert.Contains("AddContainer(\"otel-collector\"", programText);
        Assert.Contains("otel-collector.dev.yaml", programText);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", programText);
        Assert.Contains("AddViteApp(\"console\"", programText);
        Assert.Contains("WithPnpm", programText);

        Assert.Matches(
            "if \\(!fullStackEphemeral\\)\\s*\\{\\s*postgres\\.WithHostPort\\(15432\\);\\s*\\}",
            programText);
        Assert.Matches(
            "if \\(!fullStackEphemeral\\)\\s*\\{\\s*redis\\.WithHostPort\\(6379\\);\\s*\\}",
            programText);
        Assert.Single(Regex.Matches(programText, "postgres\\.WithHostPort\\(15432\\)"));
        Assert.Single(Regex.Matches(programText, "redis\\.WithHostPort\\(6379\\)"));

        Assert.Contains("Nerv.IIP.Iam.Web.csproj", projectText);
        Assert.Contains("Nerv.IIP.FileStorage.Web.csproj", projectText);
        Assert.Contains("Nerv.IIP.Notification.Web.csproj", projectText);
        Assert.Contains("Aspire.Hosting.JavaScript", projectText);

        Assert.True(File.Exists(collectorConfig), "OpenTelemetry Collector dev config must be present.");
        var collectorText = File.ReadAllText(collectorConfig);
        Assert.Contains("otlphttp/victorialogs", collectorText);
        Assert.Contains("logs_endpoint: ${env:NERV_IIP_VICTORIA_LOGS_OTLP_HTTP_ENDPOINT}", collectorText);
        Assert.Contains("--config=/etc/otelcol/config.yaml", composeText);
        Assert.Contains("./otel/otel-collector.dev.yaml:/etc/otelcol/config.yaml:ro", composeText);
        Assert.Contains("victoria-logs:", composeText);
        Assert.Contains("victoriametrics/victoria-logs:v1.50.0", composeText);
        Assert.Contains("nerv-iip-victoria-logs:/victoria-logs-data", composeText);
    }

    [Fact]
    public void Aspire_apphost_runs_project_resources_as_development()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));

        Assert.Contains("ASPNETCORE_ENVIRONMENT", programText);
        Assert.Contains("DOTNET_ENVIRONMENT", programText);
        Assert.Contains("AddParameter(\"redis-password\", secret: true)", programText);
        Assert.Contains("AddRedis(\"redis\", password: redisPassword)", programText);
        Assert.Contains("WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_MasterData_Web>", programText);
        Assert.Contains("WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Quality_Web>", programText);
        Assert.Contains("WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Mes_Web>", programText);
        Assert.Contains("WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_Business_Maintenance_Web>", programText);
        Assert.Contains("WithLocalDevelopmentEnvironment(builder.AddProject<Projects.Nerv_IIP_BusinessGateway_Web>", programText);
        Assert.Matches(
            "businessMes[\\s\\S]*WithEnvironment\\(\"Persistence__AutoMigrate\", \"true\"\\)",
            programText);
        Assert.Matches(
            "businessMaintenance[\\s\\S]*WithEnvironment\\(\"Persistence__AutoMigrate\", \"true\"\\)",
            programText);
        Assert.Matches(
            "apphub[\\s\\S]*WithEnvironment\\(\"Persistence__AutoMigrate\", \"true\"\\)",
            programText);
        Assert.Matches(
            "notification[\\s\\S]*WithEnvironment\\(\"Persistence__AutoMigrate\", \"true\"\\)",
            programText);
    }

    [Fact]
    public void Aspire_apphost_product_engineering_uses_master_data_service_discovery()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));
        var resourceBlock = GetAspireResourceBlock(programText, "businessProductEngineering");
        Assert.Contains(
            ".WithEnvironment(\"MasterData__BaseUrl\", businessMasterData.GetEndpoint(\"http\"))",
            resourceBlock);
        Assert.Contains(".WithReference(businessMasterData)", resourceBlock);
        Assert.Contains(".WaitFor(businessMasterData)", resourceBlock);
        Assert.DoesNotContain("localhost:5107", resourceBlock);
    }

    // 反向回填（MasterData 删除防护要反查 ProductEngineering 的引用占用）同样是跨服务接线，
    // WithEnvironment 与 WithReference 必须成对；WaitFor 反向加会与 PE→MasterData 成环，故不加。
    [Fact]
    public void Aspire_apphost_master_data_backfills_product_engineering_reference_without_waiting()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));
        // `businessMasterData = businessMasterData` 出现多次（PostgreSQL 分支也有一处），
        // 取其中真正做 ProductEngineering 回填的那条语句。
        var backfill = Regex
            .Matches(programText, @"businessMasterData = businessMasterData[^;]*;", RegexOptions.Singleline)
            .Select(match => match.Value)
            .Single(statement => statement.Contains("ProductEngineering__BaseUrl", StringComparison.Ordinal));

        Assert.Contains(
            ".WithEnvironment(\"ProductEngineering__BaseUrl\", businessProductEngineering.GetEndpoint(\"http\"))",
            backfill);
        Assert.Contains(".WithReference(businessProductEngineering)", backfill);
        Assert.DoesNotContain(".WaitFor(", backfill);
        Assert.DoesNotContain("localhost:5108", backfill);
    }

    [Fact]
    public void Aspire_apphost_scheduling_waits_for_mes_material_readiness_source()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));
        var resourceBlock = GetAspireResourceBlock(programText, "businessScheduling");
        Assert.Contains(
            ".WithEnvironment(\"Mes__BaseUrl\", businessMes.GetEndpoint(\"http\"))",
            resourceBlock);
        Assert.Contains(".WithReference(businessMes)", resourceBlock);
        Assert.Contains(".WaitFor(businessMes)", resourceBlock);
        Assert.DoesNotContain("localhost:5111", resourceBlock);
    }

    [Fact]
    public void Aspire_apphost_scheduling_waits_for_equipment_availability_sources()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));
        var resourceBlock = GetAspireResourceBlock(programText, "businessScheduling");
        Assert.Contains(
            ".WithEnvironment(\"IndustrialTelemetry__BaseUrl\", businessIndustrialTelemetry.GetEndpoint(\"http\"))",
            resourceBlock);
        Assert.Contains(
            ".WithEnvironment(\"Maintenance__BaseUrl\", businessMaintenance.GetEndpoint(\"http\"))",
            resourceBlock);
        Assert.Contains(".WithReference(businessIndustrialTelemetry)", resourceBlock);
        Assert.Contains(".WithReference(businessMaintenance)", resourceBlock);
        Assert.Contains(".WaitFor(businessIndustrialTelemetry)", resourceBlock);
        Assert.Contains(".WaitFor(businessMaintenance)", resourceBlock);
        Assert.DoesNotContain("localhost:5116", resourceBlock);
        Assert.DoesNotContain("localhost:5117", resourceBlock);
    }

    [Theory]
    [MemberData(nameof(LocalPostgreSqlAppHostResources))]
    public void Aspire_apphost_local_postgresql_resources_enable_development_automigration(string resourceVariable)
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));

        Assert.Matches(
            $"var {resourceVariable} =(?:(?!\\bvar )[\\s\\S])*?WithEnvironment\\(\"Persistence__Provider\", \"PostgreSQL\"\\)(?:(?!\\bvar )[\\s\\S])*?WithEnvironment\\(\"Persistence__AutoMigrate\", \"true\"\\)",
            programText);
    }

    [Fact]
    public void Aspire_apphost_vite_apps_keep_fixed_persistent_ports_and_use_proxied_ephemeral_ports()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));

        foreach (var (name, path, port) in new[]
                 {
                     ("console", "../../../frontend/apps/console", 5105),
                     ("business-console", "../../../frontend/apps/business-console", 5125),
                     ("screen", "../../../frontend/apps/screen", 5128)
                 })
        {
            var marker = $"AddViteApp(\"{name}\", \"{path}\")";
            var start = programText.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing Aspire Vite resource '{name}'.");
            var end = programText.IndexOf(".WithPnpm()", start, StringComparison.Ordinal);
            Assert.True(end > start, $"Missing pnpm registration for Aspire Vite resource '{name}'.");
            var endpointBlock = programText[start..end];

            Assert.Contains($"targetPort: fullStackEphemeral ? null : {port}", endpointBlock);
            Assert.Contains($"port: fullStackEphemeral ? null : {port}", endpointBlock);
            Assert.Contains("env: fullStackEphemeral ? \"NERV_IIP_VITE_PORT\" : null", endpointBlock);
            Assert.Contains("isProxied: fullStackEphemeral", endpointBlock);
            Assert.DoesNotContain("env: fullStackEphemeral ? \"PORT\" : null", endpointBlock);
        }
    }

    [Fact]
    public void Runtime_code_does_not_use_implicit_localhost_service_endpoint_fallbacks()
    {
        var root = FindRepositoryRoot();
        var searchRoots = new[]
        {
            Path.Combine(root, "backend"),
            Path.Combine(root, "infra", "aspire")
        };

        var offenders = searchRoots
            .SelectMany(searchRoot => Directory.GetFiles(searchRoot, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("?? \"http://localhost:", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .Order()
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Service-to-service endpoint fallbacks must fail fast outside Development. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Redis_backed_business_services_do_not_abort_startup_on_initial_redis_connect_failure()
    {
        var root = FindRepositoryRoot();
        var projectDirectories = new[]
        {
            "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web",
            "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web"
        };

        foreach (var projectDirectory in projectDirectories)
        {
            var programText = File.ReadAllText(Path.Combine(root, projectDirectory, "Program.cs"));

            Assert.Contains("NervIipRedisConnection.ConnectAsync", programText);
            Assert.DoesNotContain("static async Task<IConnectionMultiplexer> ConnectRedisAsync", programText);
            Assert.DoesNotContain("ConnectionMultiplexer.ConnectAsync(builder.Configuration.GetConnectionString(\"Redis\")!)", programText);
        }

        var redisConnectionText = File.ReadAllText(Path.Combine(
            root,
            "backend/common/Caching/Nerv.IIP.Caching/NervIipRedisConnection.cs"));
        Assert.Contains("AbortOnConnectFail = false", redisConnectionText);
    }

    [Fact]
    public void Platform_cap_services_register_integration_event_publishers_for_postgresql_profile()
    {
        var root = FindRepositoryRoot();
        var projectDirectories = new[]
        {
            "backend/services/AppHub/src/Nerv.IIP.AppHub.Web",
            "backend/services/Ops/src/Nerv.IIP.Ops.Web"
        };

        foreach (var projectDirectory in projectDirectories)
        {
            var programText = File.ReadAllText(Path.Combine(root, projectDirectory, "Program.cs"));

            Assert.Contains("UseCap<ApplicationDbContext>(b =>", programText);
            Assert.Contains("b.RegisterServicesFromAssemblies(typeof(Program))", programText);
            Assert.Contains("b.AddContextIntegrationFilters()", programText);
        }
    }

    private static string GetAspireResourceBlock(string programText, string resourceVariable)
    {
        var resourceStart = programText.IndexOf($"var {resourceVariable} =", StringComparison.Ordinal);
        Assert.True(resourceStart >= 0, $"Aspire resource '{resourceVariable}' is missing.");

        var resourceEnd = programText.IndexOf(
            $"{resourceVariable} = WithRedisMessagingTransport(",
            resourceStart,
            StringComparison.Ordinal);
        Assert.True(resourceEnd > resourceStart, $"Aspire resource block '{resourceVariable}' is incomplete.");

        return programText[resourceStart..resourceEnd];
    }

    /// <summary>
    /// 下游基址解析只许有一处实现（<c>Nerv.IIP.ServiceAuth.InternalServiceBaseAddress</c>）。
    /// </summary>
    /// <remarks>
    /// 这段逻辑曾在 14 个 Program.cs 里各抄一份并且已经抄漂：6 份只放行 <c>Development</c>、
    /// 8 份还放行 <c>Testing</c>，异常文案两种写法。于是同一个「忘配 BaseUrl」的错误
    /// 在不同服务上表现不同——有的在 Testing 下静默走 localhost，有的直接启动失败。
    /// 本用例防止再抄回去。
    /// </remarks>
    [Fact]
    public void Service_base_address_resolution_is_not_reimplemented_in_any_host()
    {
        var root = FindRepositoryRoot();
        var hostProgramFiles = Directory
            .GetFiles(Path.Combine(root, "backend"), "Program.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(hostProgramFiles);

        // 认**形状**不认函数名：改个名字就能绕过的字面量匹配等于没有门禁。
        // 手搓基址解析的特征是「在 IsDevelopment() 分支里 new 一个 Uri」——两个标记同时出现即判定。
        // 单独出现都不算：16 个 Program.cs 用 IsDevelopment() 做别的开关（属正常），
        // 而收敛后已无任何 Program.cs 还需要 new Uri(。
        var offenders = hostProgramFiles
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("IsDevelopment()", StringComparison.Ordinal)
                    && text.Contains("new Uri(", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "下游基址解析必须调用 InternalServiceBaseAddress.Resolve / ResolveAllowingTestHost，"
                + $"不得在宿主里重抄：{string.Join(", ", offenders)}");
    }

    /// <summary>
    /// 回退档位按宿主性质分档，且**边缘入口不吃 Testing 回退**。
    /// </summary>
    /// <remarks>
    /// Gateway 是边缘入口：若某环境以 <c>ASPNETCORE_ENVIRONMENT=Testing</c> 部署（staging / 测试环），
    /// 漏配下游基址时静默回落 localhost 正是该解析器自己要防的事，必须启动失败。
    /// 业务服务与 Ops 的集成测试宿主确实起在 Testing 下，用放行档。
    /// </remarks>
    [Theory]
    [InlineData("backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs")]
    [InlineData("backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs")]
    public void Edge_gateways_do_not_fall_back_to_localhost_in_the_testing_environment(string relativeProgramPath)
    {
        var root = FindRepositoryRoot();
        var programText = File.ReadAllText(Path.Combine(root, relativeProgramPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("InternalServiceBaseAddress.Resolve(", programText);
        Assert.DoesNotContain("InternalServiceBaseAddress.ResolveAllowingTestHost(", programText);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
