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
        var apphubResourceStart = programText.IndexOf("var apphub =", StringComparison.Ordinal);
        var apphubRabbitMqBranchStart = programText.IndexOf("if (rabbitmq is not null)", apphubResourceStart, StringComparison.Ordinal);
        var apphubResourceText = programText[apphubResourceStart..apphubRabbitMqBranchStart];
        Assert.Contains("WithEnvironment(\"Persistence__AutoMigrate\", \"true\")", apphubResourceText);
        var notificationResourceStart = programText.IndexOf("var notification =", StringComparison.Ordinal);
        var notificationRabbitMqBranchStart = programText.IndexOf("if (rabbitmq is not null)", notificationResourceStart, StringComparison.Ordinal);
        var notificationResourceText = programText[notificationResourceStart..notificationRabbitMqBranchStart];
        Assert.Contains("WithEnvironment(\"Persistence__AutoMigrate\", \"true\")", notificationResourceText);
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

            Assert.Contains("AbortOnConnectFail = false", programText);
            Assert.DoesNotContain("ConnectionMultiplexer.ConnectAsync(builder.Configuration.GetConnectionString(\"Redis\")!)", programText);
        }
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
