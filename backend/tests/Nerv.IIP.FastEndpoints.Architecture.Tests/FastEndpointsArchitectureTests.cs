namespace Nerv.IIP.FastEndpoints.Architecture.Tests;

public sealed class FastEndpointsArchitectureTests
{
    public static TheoryData<string> PlatformWebProjects => new()
    {
        "backend/services/Iam/src/Nerv.IIP.Iam.Web",
        "backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web",
        "backend/services/AppHub/src/Nerv.IIP.AppHub.Web",
        "backend/services/Ops/src/Nerv.IIP.Ops.Web",
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
        Assert.Contains("AddContainer(\"minio\"", programText);
        Assert.Contains("AddContainer(\"otel-collector\"", programText);
        Assert.Contains("otel-collector.dev.yaml", programText);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", programText);
        Assert.Contains("AddViteApp(\"console\"", programText);
        Assert.Contains("WithPnpm", programText);

        Assert.Contains("Nerv.IIP.Iam.Web.csproj", projectText);
        Assert.Contains("Nerv.IIP.FileStorage.Web.csproj", projectText);
        Assert.Contains("Aspire.Hosting.JavaScript", projectText);

        Assert.True(File.Exists(collectorConfig), "OpenTelemetry Collector dev config must be present.");
        Assert.Contains("--config=/etc/otelcol/config.yaml", composeText);
        Assert.Contains("./otel/otel-collector.dev.yaml:/etc/otelcol/config.yaml:ro", composeText);
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
