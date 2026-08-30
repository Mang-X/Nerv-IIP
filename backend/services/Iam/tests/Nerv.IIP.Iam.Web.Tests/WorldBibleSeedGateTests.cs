using Nerv.IIP.Iam.Web.Application.Seed;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// NERV-1360/#1912 的 seed 入口合同：WMS worker 开通是显式口令 opt-in，
/// 与完整 World Bible 开关可以分别控制。
/// </summary>
public sealed class WorldBibleSeedGateTests
{
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    [InlineData(false, "   ", false)]
    [InlineData(false, "worker-password-for-test", true)]
    [InlineData(true, null, true)]
    [InlineData(true, "", true)]
    public void Worker_seed_requires_world_enabled_or_a_non_blank_demo_worker_password(
        bool worldEnabled,
        string? demoWorkerPassword,
        bool expected)
    {
        Assert.Equal(
            expected,
            WorldBibleSeedGate.ShouldRunWorkerSeeds(worldEnabled, demoWorkerPassword));
    }

    [Fact]
    public void Iam_program_uses_the_worker_opt_in_without_enabling_other_world_seeds()
    {
        var program = ReadRepositoryFile(
            "backend/services/Iam/src/Nerv.IIP.Iam.Web/Program.cs");

        Assert.Contains("WorldBibleSeedGate.ShouldRunWorkerSeeds", program, StringComparison.Ordinal);
        Assert.Contains("Iam:Seed:DemoWorkerPassword", program, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                var path = Path.Combine(
                    directory.FullName,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"未找到受治理文件：{relativePath}");
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
