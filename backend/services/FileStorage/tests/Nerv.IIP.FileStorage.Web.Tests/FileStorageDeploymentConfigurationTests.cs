namespace Nerv.IIP.FileStorage.Web.Tests;

using System.Text.RegularExpressions;

public sealed class FileStorageDeploymentConfigurationTests
{
    private const string ComplianceArchiveBucket = "nerv-iip-compliance-archive";

    [Fact]
    public void Legacy_compose_initializes_the_compliance_bucket_before_file_storage_starts()
    {
        var dependencies = ReadRepositoryFile("infra/compose/nerv-iip.dependencies.yml");
        var platform = ReadRepositoryFile("infra/compose/nerv-iip.platform.yml");
        var minioInit = ComposeServiceBlock(dependencies, "minio-init");
        var fileStorage = ComposeServiceBlock(platform, "file-storage");

        Assert.Contains("    restart: \"no\"", minioInit, StringComparison.Ordinal);
        Assert.Contains("      minio:\n        condition: service_healthy", minioInit, StringComparison.Ordinal);
        Assert.Contains("mc mb --ignore-existing --with-lock --with-versioning", minioInit, StringComparison.Ordinal);
        Assert.Contains("mc version enable", minioInit, StringComparison.Ordinal);
        Assert.Contains("mc version info --json", minioInit, StringComparison.Ordinal);
        Assert.Contains("case \"$${version_info}\" in", minioInit, StringComparison.Ordinal);
        Assert.Contains("mc retention info --json --default", minioInit, StringComparison.Ordinal);
        Assert.Contains("case \"$${retention_info}\" in", minioInit, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(minioInit, @"(?m)^          \*\) exit 1 ;;$").Count);
        Assert.Contains($"COMPLIANCE_ARCHIVE_BUCKET: {ComplianceArchiveBucket}", minioInit, StringComparison.Ordinal);
        Assert.Contains("      minio-init:\n        condition: service_completed_successfully", fileStorage, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_compose_and_apphost_inject_the_same_minio_configuration()
    {
        var platform = ReadRepositoryFile("infra/compose/nerv-iip.platform.yml");
        var appHost = ReadRepositoryFile("infra/aspire/Nerv.IIP.AppHost/Program.cs");
        var fileStorage = ComposeServiceBlock(platform, "file-storage");
        var appHostFileStorage = TextBetween(appHost, "var fileStorage =", "var notification =");
        string[] configurationKeys =
        [
            "Storage__Provider",
            "Storage__MinIO__Endpoint",
            "Storage__MinIO__AccessKey",
            "Storage__MinIO__SecretKey",
            "Storage__MinIO__ComplianceArchiveBucket"
        ];

        var composeKeys = Regex.Matches(fileStorage, @"(?m)^      (?<key>Storage__[A-Za-z0-9_]+):")
            .Select(match => match.Groups["key"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var appHostKeys = Regex.Matches(appHostFileStorage, @"\.WithEnvironment\(""(?<key>Storage__[A-Za-z0-9_]+)""")
            .Select(match => match.Groups["key"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(configurationKeys.Order(StringComparer.Ordinal), composeKeys);
        Assert.Equal(configurationKeys.Order(StringComparer.Ordinal), appHostKeys);
        Assert.Contains("Storage__Provider: MinIO", fileStorage, StringComparison.Ordinal);
        Assert.Contains("Storage__MinIO__Endpoint: http://minio:9000", fileStorage, StringComparison.Ordinal);
        Assert.Contains($"Storage__MinIO__ComplianceArchiveBucket: {ComplianceArchiveBucket}", fileStorage, StringComparison.Ordinal);
        Assert.Contains(".WithEnvironment(\"Storage__Provider\", \"MinIO\")", appHostFileStorage, StringComparison.Ordinal);
        Assert.Contains(".WithEnvironment(\"Storage__MinIO__Endpoint\", minio.GetEndpoint(\"api\"))", appHostFileStorage, StringComparison.Ordinal);
        Assert.Contains($".WithEnvironment(\"Storage__MinIO__ComplianceArchiveBucket\", \"{ComplianceArchiveBucket}\")", appHostFileStorage, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ComposeServiceBlock(string yaml, string serviceName)
    {
        var match = Regex.Match(
            yaml.Replace("\r\n", "\n", StringComparison.Ordinal),
            $@"(?ms)^  {Regex.Escape(serviceName)}:\s*\n(?<body>.*?)(?=^  [a-z0-9][a-z0-9-]*:\s*$|\z)");

        Assert.True(match.Success, $"Compose service '{serviceName}' was not found.");
        return match.Value;
    }

    private static string TextBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Text range '{startMarker}' to '{endMarker}' was not found.");
        return text[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
