using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelWebHostCollectionTests
{
    [Fact]
    public void Every_test_class_that_starts_the_service_host_uses_the_canonical_collection()
    {
        var sourceRoot = Path.GetDirectoryName(CurrentSourcePath())!;
        var hostClasses = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)
                .GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(creation => creation.Type.ToString()
                    .EndsWith("WebApplicationFactory<Program>", StringComparison.Ordinal))
                .Select(creation => creation.FirstAncestorOrSelf<ClassDeclarationSyntax>()!))
            .Distinct()
            .ToArray();

        Assert.NotEmpty(hostClasses);
        var violations = hostClasses
            .Where(declaration => !declaration.AttributeLists
                .SelectMany(list => list.Attributes)
                .Any(attribute => attribute.Name.ToString().EndsWith("Collection", StringComparison.Ordinal)
                    && attribute.ArgumentList?.Arguments.SingleOrDefault()?.Expression.ToString()
                        == "BarcodeLabelWebApplicationFactoryCollection.Name"))
            .Select(declaration => $"{Path.GetFileName(declaration.SyntaxTree.FilePath)}:{declaration.Identifier.ValueText}")
            .ToArray();
        Assert.Empty(violations);
    }

    private static string CurrentSourcePath([CallerFilePath] string path = "") => path;
}
