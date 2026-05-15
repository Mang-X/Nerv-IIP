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
