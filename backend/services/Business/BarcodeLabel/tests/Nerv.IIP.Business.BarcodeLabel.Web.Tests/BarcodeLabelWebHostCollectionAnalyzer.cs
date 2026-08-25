using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public static class BarcodeLabelWebHostCollectionAnalyzer
{
    private const string CanonicalCollectionName = "BarcodeLabel WebApplicationFactory";
    private static readonly IEqualityComparer<INamedTypeSymbol> TypeComparer = new NamedTypeSymbolEqualityComparer();

    public static IReadOnlyList<string> FindViolations(
        IReadOnlyCollection<BarcodeLabelSourceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("BarcodeLabel WebApplicationFactory 源集合不能为空。");
        }

        var sourceTrees = documents
            .Select(document => CSharpSyntaxTree.ParseText(document.Text, path: NormalizePath(document.Path)))
            .Append(CSharpSyntaxTree.ParseText(
                """
                global using System;
                global using System.Collections.Generic;
                global using System.IO;
                global using System.Linq;
                global using System.Net.Http;
                global using System.Threading;
                global using System.Threading.Tasks;
                global using Xunit;
                """,
                path: "__BarcodeLabelTestGlobalUsings.g.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "Nerv.IIP.Business.BarcodeLabel.Web.Tests",
            sourceTrees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException(
                "BarcodeLabel WebApplicationFactory 测试源码无法编译："
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

        var declarations = sourceTrees
            .Where(tree => tree.FilePath != "__BarcodeLabelTestGlobalUsings.g.cs")
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Select(declaration => new TypeDeclaration(
                declaration,
                (INamedTypeSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!))
            .ToArray();
        var declarationsByType = declarations
            .GroupBy(declaration => declaration.Symbol, TypeComparer)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Syntax).ToArray(),
                TypeComparer);

        var dependencies = declarationsByType.Keys.ToDictionary(
            symbol => symbol,
            symbol => FindReferencedTypes(symbol, declarationsByType[symbol], compilation),
            TypeComparer);
        var hostRelatedTypes = declarationsByType.Keys
            .Where(IsWebApplicationFactoryForProgram)
            .ToHashSet(TypeComparer);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (symbol, referencedTypes) in dependencies)
            {
                if (!hostRelatedTypes.Contains(symbol)
                    && referencedTypes.Any(referenced => IsWebApplicationFactoryForProgram(referenced)
                        || hostRelatedTypes.Contains(referenced)))
                {
                    hostRelatedTypes.Add(symbol);
                    changed = true;
                }
            }
        }

        return declarationsByType
            .Where(pair => hostRelatedTypes.Contains(pair.Key)
                && IsTestClass(pair.Key)
                && !UsesCanonicalCollection(pair.Key))
            .Select(pair => $"{Path.GetFileName(pair.Value[0].SyntaxTree.FilePath)}:{pair.Key.Name}")
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlySet<INamedTypeSymbol> FindReferencedTypes(
        INamedTypeSymbol symbol,
        IReadOnlyCollection<TypeDeclarationSyntax> declarations,
        CSharpCompilation compilation)
    {
        var referenced = new HashSet<INamedTypeSymbol>(TypeComparer);
        AddType(symbol.BaseType, referenced);
        foreach (var @interface in symbol.AllInterfaces)
        {
            AddType(@interface, referenced);
        }

        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field:
                    AddType(field.Type, referenced);
                    break;
                case IPropertySymbol property:
                    AddType(property.Type, referenced);
                    break;
                case IMethodSymbol method:
                    AddType(method.ReturnType, referenced);
                    foreach (var parameter in method.Parameters)
                    {
                        AddType(parameter.Type, referenced);
                    }

                    break;
            }
        }

        foreach (var declaration in declarations)
        {
            var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var creation in declaration.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                AddType(semanticModel.GetTypeInfo(creation).Type, referenced);
            }
        }

        return referenced;
    }

    private static void AddType(ITypeSymbol? type, ISet<INamedTypeSymbol> destination)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                AddType(array.ElementType, destination);
                return;
            case INamedTypeSymbol named:
                destination.Add(named);
                foreach (var argument in named.TypeArguments)
                {
                    AddType(argument, destination);
                }

                return;
        }
    }

    private static bool IsWebApplicationFactoryForProgram(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.MetadataName == "WebApplicationFactory`1"
                && current.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Mvc.Testing"
                && current.TypeArguments is [INamedTypeSymbol entryPoint]
                && entryPoint.Name == "Program"
                && entryPoint.ContainingNamespace.IsGlobalNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTestClass(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IMethodSymbol>().Any(method => method.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { ContainingNamespace: { } containingNamespace } attributeType
            && containingNamespace.ToDisplayString() == "Xunit"
            && attributeType.Name is "FactAttribute" or "TheoryAttribute"))
        || type.AllInterfaces.Any(@interface =>
            @interface.MetadataName == "IClassFixture`1"
            && @interface.ContainingNamespace.ToDisplayString() == "Xunit");

    private static bool UsesCanonicalCollection(INamedTypeSymbol type) =>
        type.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { Name: "CollectionAttribute", ContainingNamespace: { } containingNamespace }
            && containingNamespace.ToDisplayString() == "Xunit"
            && attribute.ConstructorArguments is [{ Value: CanonicalCollectionName }]);

    private static IReadOnlyCollection<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];
        var testAssemblies = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(
                Path.GetFileNameWithoutExtension(path),
                "Nerv.IIP.Business.BarcodeLabel.Web.Tests",
                StringComparison.Ordinal));
        return trustedPlatformAssemblies
            .Concat(testAssemblies)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record TypeDeclaration(TypeDeclarationSyntax Syntax, INamedTypeSymbol Symbol);

    private sealed class NamedTypeSymbolEqualityComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y) =>
            SymbolEqualityComparer.Default.Equals(x, y);

        public int GetHashCode(INamedTypeSymbol obj) => SymbolEqualityComparer.Default.GetHashCode(obj);
    }
}
