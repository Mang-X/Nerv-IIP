# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses C# test sources and validates the PostgreSQL temporary-database ownership ledger
#     - Mutates in-memory policy and source snapshots to prove the contract fails closed
#   Writes:
#     - None
#   Cleanup:
#     - No external resources are created
#   Requires:
#     - PowerShell 7
#     - .NET SDK with Roslyn compiler assemblies

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$policyPath = Join-Path $repoRoot 'scripts/postgres-test-database-consumers.json'
. (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1')
$allowedStrategies = Get-NervStringSet -Values @('best-effort-dispose', 'encapsulated-explicit-drop', 'explicit-drop-finally', 'factory-forwarder', 'helper-self-test') -Comparer ([StringComparer]::Ordinal)
$requiredOwnerships = @(
    @{ sourcePath = 'backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/WorldHistoryLabelSeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/ErpSalesOrderDemandConsumerTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 3 },
    @{ sourcePath = 'backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/ErpSalesOrderDemandConsumerTests.cs'; strategy = 'encapsulated-explicit-drop'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/WorldHistoryDeviceSeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/WorldHistoryMaintenanceSeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataWorldBibleSeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/LeaderDemoScaleSeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/WorldHistorySeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/WorldBibleSeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/WorldHistoryQualitySeedPostgresTests.cs'; strategy = 'explicit-drop-finally'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs'; strategy = 'factory-forwarder'; factoryCallCount = 1 },
    @{ sourcePath = 'backend/tests/Nerv.IIP.Testing.PostgreSql.Tests/PostgreSqlTestDatabaseTests.cs'; strategy = 'helper-self-test'; factoryCallCount = 6 }
)

function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-SortedOrdinal([string[]]$Values) { $copy = [string[]] @($Values); [Array]::Sort($copy, [StringComparer]::Ordinal); return $copy }
function Test-OrdinalSequenceEqual([string[]]$Left, [string[]]$Right) {
    if ($Left.Count -ne $Right.Count) { return $false }
    for ($index = 0; $index -lt $Left.Count; $index++) {
        if (-not [string]::Equals($Left[$index], $Right[$index], [StringComparison]::Ordinal)) { return $false }
    }
    return $true
}
function Test-OrdinalMember([string[]]$Values, [string]$Expected) {
    foreach ($value in @($Values)) {
        if ([string]::Equals($value, $Expected, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}
function Get-NormalizedSha256([string]$Source) {
    $normalized = $Source.Replace("`r`n", "`n", [StringComparison]::Ordinal)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($normalized))).ToLowerInvariant()
}

function Initialize-CSharpFactoryAnalyzer {
    $codeAnalysis = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { [string]::Equals($_.GetName().Name, 'Microsoft.CodeAnalysis', [StringComparison]::Ordinal) } | Select-Object -First 1
    $csharp = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { [string]::Equals($_.GetName().Name, 'Microsoft.CodeAnalysis.CSharp', [StringComparison]::Ordinal) } | Select-Object -First 1
    Assert-Contract ($null -ne $codeAnalysis -and $null -ne $csharp) 'PowerShell must load its bundled Roslyn assemblies for syntax-aware consumer discovery.'
    $references = [Collections.Generic.List[string]]::new()
    foreach ($reference in ([string] [AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES')).Split([IO.Path]::PathSeparator)) { $references.Add($reference) }
    $references.Add($codeAnalysis.Location); $references.Add($csharp.Location)
    Add-Type -IgnoreWarnings -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ([string[]] @(Get-NervStringsSorted -Values @($references) -Comparer ([StringComparer]::Ordinal) -Unique)) -TypeDefinition @'
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class PostgreSqlFactoryAnalysis
{
    public PostgreSqlFactoryAnalysis(int factoryCallCount, string[] unsupportedForms)
    {
        FactoryCallCount = factoryCallCount;
        UnsupportedForms = unsupportedForms;
    }
    public int FactoryCallCount { get; }
    public string[] UnsupportedForms { get; }
}
public static class PostgreSqlFactorySyntaxAnalyzer
{
    public static PostgreSqlFactoryAnalysis Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException("C# consumer source has syntax errors: " + string.Join("; ", errors.Select(e => e.ToString())));
        var root = tree.GetCompilationUnitRoot();
        var unsupportedUsingForms = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(u => u.Name != null
                && u.Name.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().LastOrDefault()?.Identifier.ValueText == "PostgreSqlTestDatabase"
                && (u.Alias != null || !u.StaticKeyword.IsKind(SyntaxKind.None)))
            .Select(u => u.Alias != null ? "alias" : "using-static");
        var factoryMembers = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(member => member.Name.Identifier.ValueText == "CreateAsync"
                && member.Expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().LastOrDefault()?.Identifier.ValueText == "PostgreSqlTestDatabase")
            .ToArray();
        var unsupported = unsupportedUsingForms
            .Concat(factoryMembers.Where(member => member.Parent is not InvocationExpressionSyntax invocation || !ReferenceEquals(invocation.Expression, member)).Select(_ => "method-group"))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(invocation =>
            invocation.Expression is MemberAccessExpressionSyntax member
            && member.Name.Identifier.ValueText == "CreateAsync"
            && member.Expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().LastOrDefault()?.Identifier.ValueText == "PostgreSqlTestDatabase");
        return new PostgreSqlFactoryAnalysis(calls, unsupported);
    }
}
'@
}
Initialize-CSharpFactoryAnalyzer

function New-SourceSnapshot([string]$Source) {
    $analysis = [PostgreSqlFactorySyntaxAnalyzer]::Analyze($Source)
    return [pscustomobject]@{ Source = $Source; SourceSha256 = Get-NormalizedSha256 $Source; FactoryCallCount = $analysis.FactoryCallCount; UnsupportedForms = [string[]] @($analysis.UnsupportedForms) }
}
function Get-ConsumerSources {
    $sources = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'backend') -Recurse -File -Filter '*.cs') {
        if ($file.FullName -match '[/\\](?:bin|obj)[/\\]') { continue }
        $source = [IO.File]::ReadAllText($file.FullName)
        $snapshot = New-SourceSnapshot $source
        Assert-Contract ($snapshot.UnsupportedForms.Count -eq 0) "PostgreSqlTestDatabase consumers must use the explicit type spelling; unsupported $($snapshot.UnsupportedForms -join ', ') in '$($file.FullName)'."
        if ($snapshot.FactoryCallCount -eq 0) { continue }
        $relative = [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
        $sources.Add($relative, $snapshot)
    }
    return $sources
}
function Assert-ConsumerPolicy([object]$Policy, [Collections.Generic.Dictionary[string, object]]$Sources) {
    Assert-Contract ($Policy.schemaVersion -eq 1) 'The consumer policy must use schemaVersion 1.'
    $consumers = @($Policy.consumers)
    $registeredPaths = [string[]] @($consumers | ForEach-Object { [string] $_.sourcePath })
    Assert-Contract ((Get-NervStringsSorted -Values $registeredPaths -Comparer ([StringComparer]::Ordinal) -Unique).Count -eq $registeredPaths.Count) 'Consumer source paths must be unique.'
    Assert-Contract (Test-OrdinalSequenceEqual $registeredPaths (Get-SortedOrdinal $registeredPaths)) 'Consumer source paths must use stable ordinal ordering.'
    Assert-Contract (Test-OrdinalSequenceEqual $registeredPaths (Get-SortedOrdinal ([string[]] @($Sources.Keys)))) 'Every shared-factory source must appear exactly once in the ownership ledger.'
    foreach ($consumer in $consumers) {
        $path = [string] $consumer.sourcePath
        Assert-Contract ($Sources.ContainsKey($path)) "Registered consumer '$path' is missing."
        $snapshot = $Sources[$path]
        Assert-Contract ([string]::Equals([string] $consumer.sourceSha256, [string] $snapshot.SourceSha256, [StringComparison]::Ordinal)) "Consumer '$path' source hash drifted; review ownership and refresh the ledger intentionally."
        Assert-Contract ($consumer.factoryCallCount -eq $snapshot.FactoryCallCount) "Consumer '$path' factory count drifted: expected=$($consumer.factoryCallCount); actual=$($snapshot.FactoryCallCount)."
        $classified = 0
        foreach ($ownership in @($consumer.ownerships)) {
            Assert-Contract ($allowedStrategies.Contains([string] $ownership.strategy)) "Consumer '$path' uses unsupported strategy '$($ownership.strategy)'."
            Assert-Contract ($ownership.factoryCallCount -is [long] -and $ownership.factoryCallCount -gt 0) "Consumer '$path' ownership count must be a positive integer."
            Assert-Contract (-not [string]::IsNullOrWhiteSpace([string] $ownership.reason)) "Consumer '$path' ownership must explain its governance rationale."
            $classified += $ownership.factoryCallCount
        }
        Assert-Contract ($classified -eq $consumer.factoryCallCount) "Consumer '$path' classified count does not match its source total."
    }
    foreach ($required in $requiredOwnerships) {
        $consumer = @($consumers | Where-Object { [string]::Equals([string] $_.sourcePath, [string] $required.sourcePath, [StringComparison]::Ordinal) })
        $ownership = @($consumer.ownerships | Where-Object { [string]::Equals([string] $_.strategy, [string] $required.strategy, [StringComparison]::Ordinal) -and $_.factoryCallCount -eq $required.factoryCallCount })
        Assert-Contract ($consumer.Count -eq 1 -and $ownership.Count -eq 1) "Consumer '$($required.sourcePath)' must retain required ownership '$($required.strategy)' for $($required.factoryCallCount) call(s)."
    }
}
function Copy-SourcesWithMutation([Collections.Generic.Dictionary[string, object]]$Sources, [string]$Path, [string]$Source) {
    $copy = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Sources.GetEnumerator()) { $copy.Add($entry.Key, $entry.Value) }
    $copy[$Path] = New-SourceSnapshot $Source
    return $copy
}

Assert-Contract (Test-Path -LiteralPath $policyPath -PathType Leaf) 'The consumer policy must exist.'
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json -Depth 100
$sources = Get-ConsumerSources
Assert-ConsumerPolicy $policy $sources

$missing = $policy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100; $missing.consumers = @($missing.consumers | Select-Object -Skip 1)
$rejected = $false; try { Assert-ConsumerPolicy $missing $sources } catch { $rejected = $_.Exception.Message.Contains('exactly once', [StringComparison]::Ordinal) }; Assert-Contract $rejected 'Deleting a ledger entry must fail closed.'
$downgraded = $policy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100; (@($downgraded.consumers | Where-Object { ([string] $_.sourcePath).Contains('ProductEngineering', [StringComparison]::Ordinal) })[0].ownerships[0]).strategy = 'best-effort-dispose'
$rejected = $false; try { Assert-ConsumerPolicy $downgraded $sources } catch { $rejected = $_.Exception.Message.Contains('required ownership', [StringComparison]::Ordinal) }; Assert-Contract $rejected 'Downgrading required explicit ownership must fail closed.'
$targetPath = 'backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/WorldBibleSeedPostgresTests.cs'
$mutatedSource = ([string] $sources[$targetPath].Source).Replace('await database.DropAsync();', 'if (DateTime.UtcNow.Ticks < 0) await database.DropAsync();', [StringComparison]::Ordinal)
$rejected = $false; try { Assert-ConsumerPolicy $policy (Copy-SourcesWithMutation $sources $targetPath $mutatedSource) } catch { $rejected = $_.Exception.Message.Contains('source hash drifted', [StringComparison]::Ordinal) }; Assert-Contract $rejected 'Any governed source mutation must fail closed before syntax tricks can preserve a stale classification.'
$aliasProbe = 'using Pg = Nerv.IIP.Testing.PostgreSql.PostgreSqlTestDatabase; class Probe { Task Run(string value) => Pg.CreateAsync(value, "probe"); }'
Assert-Contract (Test-OrdinalMember -Values ([string[]] @([PostgreSqlFactorySyntaxAnalyzer]::Analyze($aliasProbe).UnsupportedForms)) -Expected 'alias') 'Alias-based factory spelling must be rejected as ambiguous governance input.'
$methodGroupProbe = 'class Probe { async Task Run(string value) { var create = PostgreSqlTestDatabase.CreateAsync; await create(value, "probe"); } }'
Assert-Contract (Test-OrdinalMember -Values ([string[]] @([PostgreSqlFactorySyntaxAnalyzer]::Analyze($methodGroupProbe).UnsupportedForms)) -Expected 'method-group') 'Factory method-group indirection must be rejected as ambiguous governance input.'

Write-Output "PostgreSQL test database consumer contract tests passed: sources=$($sources.Count)."
