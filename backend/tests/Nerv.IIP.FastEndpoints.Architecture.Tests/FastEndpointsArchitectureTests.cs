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

    public static TheoryData<string> CommandLockWebProjects => new()
    {
        "backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web",
        "backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web",
        "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web",
        "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web",
        "backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web",
        "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web",
        "backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web",
        "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web",
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web",
        "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web",
        "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web",
        "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web"
    };

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
        var programText = File.ReadAllText(Path.Combine(root, projectDirectory, "Program.cs"));

        Assert.Contains("AddCommandLockBehavior", programText);
        Assert.Contains("AddInMemoryDistributedLock", programText);
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
        Assert.Contains("WithHttpEndpoint(port: 5106", programText);
        Assert.Contains("Notification__BaseUrl", programText);
        Assert.Contains("AddContainer(\"minio\"", programText);
        Assert.Contains("AddContainer(\"otel-collector\"", programText);
        Assert.Contains("otel-collector.dev.yaml", programText);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", programText);
        Assert.Contains("AddViteApp(\"console\"", programText);
        Assert.Contains("WithPnpm", programText);

        Assert.Contains("Nerv.IIP.Iam.Web.csproj", projectText);
        Assert.Contains("Nerv.IIP.FileStorage.Web.csproj", projectText);
        Assert.Contains("Nerv.IIP.Notification.Web.csproj", projectText);
        Assert.Contains("Aspire.Hosting.JavaScript", projectText);

        Assert.True(File.Exists(collectorConfig), "OpenTelemetry Collector dev config must be present.");
        Assert.Contains("--config=/etc/otelcol/config.yaml", composeText);
        Assert.Contains("./otel/otel-collector.dev.yaml:/etc/otelcol/config.yaml:ro", composeText);
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
    public void Aspire_apphost_vite_apps_use_fixed_unproxied_local_ports()
    {
        var root = FindRepositoryRoot();
        var appHostDirectory = Path.Combine(root, "infra", "aspire", "Nerv.IIP.AppHost");
        var programText = File.ReadAllText(Path.Combine(appHostDirectory, "Program.cs"));

        Assert.Contains("AddViteApp(\"console\", \"../../../frontend/apps/console\")", programText);
        Assert.Matches(
            "AddViteApp\\(\"console\", \"../../../frontend/apps/console\"\\)[\\s\\S]*WithHttpEndpoint\\(port: 5105, name: \"http\", isProxied: false\\)",
            programText);
        Assert.Contains("AddViteApp(\"business-console\", \"../../../frontend/apps/business-console\")", programText);
        Assert.Matches(
            "AddViteApp\\(\"business-console\", \"../../../frontend/apps/business-console\"\\)[\\s\\S]*WithHttpEndpoint\\(port: 5125, name: \"http\", isProxied: false\\)",
            programText);
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
