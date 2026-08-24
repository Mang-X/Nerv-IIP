using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>#1892：Quality 检验处置词表必须由独立公共契约文件提供。</summary>
public sealed class QualityVocabularyReferenceGovernanceTests
{
    [Fact]
    public void Quality_inspection_disposition_vocabulary_has_one_dedicated_contract_source()
    {
        var backendRoot = BackendRoot();
        var contractsRoot = Path.Combine(backendRoot, "common", "Contracts", "Nerv.IIP.Contracts.Quality");
        var declarations = Directory
            .EnumerateFiles(contractsRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(file => CSharpSyntaxTree
                .ParseText(File.ReadAllText(file), path: file)
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(type => type.Identifier.ValueText == "QualityInspectionDispositionStatuses")
                .Select(_ => Path.GetFileName(file)))
            .ToArray();

        Assert.Equal(["QualityInspectionDispositionStatuses.cs"], declarations);
    }

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "common", "Contracts");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "backend");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the backend directory from the test output directory.");
    }
}
