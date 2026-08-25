using System.Runtime.CompilerServices;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelWebHostCollectionTests
{
    [Fact]
    public void Every_test_class_that_owns_or_consumes_the_service_host_uses_the_canonical_collection()
    {
        var sourceRoot = Path.GetDirectoryName(CurrentSourcePath())!;
        var documents = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Select(path => new BarcodeLabelSourceDocument(path, File.ReadAllText(path)))
            .ToArray();

        var violations = BarcodeLabelWebHostCollectionAnalyzer.FindViolations(documents);

        Assert.Empty(violations);
    }

    [Fact]
    public void Type_system_discovery_covers_construction_inheritance_fixture_and_helper_call_shapes()
    {
        var violations = BarcodeLabelWebHostCollectionAnalyzer.FindViolations(
        [
            new BarcodeLabelSourceDocument("BypassMatrix.cs", BypassMatrixSource),
        ]);

        Assert.Equal(
        [
            "BypassMatrix.cs:AliasTests",
            "BypassMatrix.cs:DerivedTests",
            "BypassMatrix.cs:FixtureTests",
            "BypassMatrix.cs:GenericHelperInvocationTests",
            "BypassMatrix.cs:GlobalProgramTests",
            "BypassMatrix.cs:HelperInvocationTests",
            "BypassMatrix.cs:RecordTests",
            "BypassMatrix.cs:TargetTypedTests",
            "BypassMatrix.cs:VoidHelperInvocationTests",
        ],
            violations);
    }

    private static string CurrentSourcePath([CallerFilePath] string path = "") => path;

    private const string BypassMatrixSource = """
        using Microsoft.AspNetCore.Mvc.Testing;
        using Xunit;
        using AliasFactory = Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>;

        public class Program { }

        public sealed class TargetTypedTests
        {
            [Fact]
            public void StartsHost()
            {
                WebApplicationFactory<Program> factory = new();
            }
        }

        public sealed class GlobalProgramTests
        {
            [Fact]
            public void StartsHost() => _ = new WebApplicationFactory<global::Program>();
        }

        public sealed class AliasTests
        {
            [Fact]
            public void StartsHost() => _ = new AliasFactory();
        }

        public sealed record class RecordTests
        {
            [Fact]
            public void StartsHost() => _ = new AliasFactory();
        }

        public sealed class DerivedFactory : WebApplicationFactory<Program> { }

        public sealed class DerivedTests
        {
            [Fact]
            public void StartsHost() => _ = new DerivedFactory();
        }

        public sealed class HostFixture
        {
            public WebApplicationFactory<Program> Factory { get; } = new();
        }

        public sealed class FixtureTests : IClassFixture<HostFixture>
        {
            private readonly HostFixture fixture;
            public FixtureTests(HostFixture fixture) => this.fixture = fixture;

            [Fact]
            public void UsesHost() => _ = fixture.Factory;
        }

        public static class StaticHostFactoryHelper
        {
            public static WebApplicationFactory<Program> Create() => new();
        }

        public sealed class HelperInvocationTests
        {
            [Fact]
            public void StartsHost() => _ = StaticHostFactoryHelper.Create();
        }

        public static class StaticHostStarter
        {
            public static void Start() => _ = new WebApplicationFactory<Program>();
        }

        public sealed class VoidHelperInvocationTests
        {
            [Fact]
            public void StartsHost() => StaticHostStarter.Start();
        }

        public sealed class GenericHelperInvocationTests
        {
            [Fact]
            public void StartsHost() => GenericHostForwarder<int>.Start();
        }

        public static class GenericHostForwarder<T>
        {
            public static void Start() => GenericHostHelper<T>.Start();
        }

        public static class GenericHostHelper<T>
        {
            public static void Start() => _ = new WebApplicationFactory<Program>();
        }

        namespace Microsoft.AspNetCore.Mvc.Testing
        {
            public class WebApplicationFactory<TEntryPoint> where TEntryPoint : class { }
        }

        namespace Xunit
        {
            public sealed class FactAttribute : System.Attribute { }
            public interface IClassFixture<TFixture> where TFixture : class { }
        }
        """;
}
