using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.VocabularyGovernance.Tests;

/// <summary>#1892：Quality Domain 的检验处置值必须由独立公共词表提供。</summary>
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

    [Fact]
    public void Quality_domain_references_the_1892_vocabulary_without_copying_its_values()
    {
        var backendRoot = BackendRoot();
        var contractProject = Path.Combine(
            backendRoot,
            "common",
            "Contracts",
            "Nerv.IIP.Contracts.Quality",
            "Nerv.IIP.Contracts.Quality.csproj");
        var domainProject = Path.Combine(
            backendRoot,
            "services",
            "Business",
            "Quality",
            "src",
            "Nerv.IIP.Business.Quality.Domain",
            "Nerv.IIP.Business.Quality.Domain.csproj");
        var referencedProjects = XDocument.Load(domainProject)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar))
            .Select(include => Path.GetFullPath(include, Path.GetDirectoryName(domainProject)!))
            .ToArray();

        Assert.Single(referencedProjects, path => string.Equals(path, contractProject, StringComparison.Ordinal));

        var contractSource = Path.Combine(
            Path.GetDirectoryName(contractProject)!,
            "QualityInspectionDispositionStatuses.cs");
        var extraction = ContractsVocabularyExtractor.Extract(
            [new SourceDocument("common/Contracts/Nerv.IIP.Contracts.Quality/QualityInspectionDispositionStatuses.cs", File.ReadAllText(contractSource))]);
        Assert.Empty(extraction.Errors);

        var domainRoot = Path.GetDirectoryName(domainProject)!;
        var domainDocuments = new[]
        {
            Path.Combine(domainRoot, "AggregatesModel", "InspectionRecordAggregate", "InspectionRecord.cs"),
            Path.Combine(domainRoot, "AggregatesModel", "CorrectiveActionAggregate", "CorrectiveAction.cs"),
        }.Select(file => new SourceDocument(
            Path.GetRelativePath(backendRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
            File.ReadAllText(file))).ToArray();
        var result = VocabularyLiteralScanner.Scan(extraction.Constants, domainDocuments, [], []);

        Assert.True(
            result.Violations.Count == 0,
            "#1892 的 Quality Domain 检验处置值必须引用公共词表：" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Violations));
        Assert.Empty(result.StaleExemptions);
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
