# Script-Governance:
#   Category: check
#   SideEffects:
#     - Creates a temporary backend inventory mirror with mutation projects
#     - Creates a temporary C# Docker-lookalike fixture inside an existing backend test project
#   Writes:
#     - OS temporary directory: backend inventory, workflow, manifest, policy, shard TRX and timing-cache fixtures (temporarily)
#     - backend/tests/Nerv.IIP.Testing.Tests/TemporaryDockerLookalikes-*.cs (temporarily)
#     - artifacts/backend-test-shards-collision-*.cs selector-collision fixture (temporarily)
#     - artifacts/shard-fixture-*.slnf rearranged solution filters (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes every temporary project, Docker-lookalike source, workflow, manifest, policy, TRX, timing-cache, solution filter and collision fixture in finally
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$manifestPath = Join-Path $repoRoot 'scripts/backend-test-shards.json'
$validatorPath = Join-Path $repoRoot 'scripts/verify-backend-test-shards.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$temporaryBackendInventory = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-inventory-{0}" -f [Guid]::NewGuid().ToString('N'))
$temporaryProjectDirectory = Join-Path $temporaryBackendInventory 'tests/Nerv.IIP.TemporaryShardClassification.Tests'
$temporaryProjectPath = Join-Path $temporaryProjectDirectory 'Nerv.IIP.TemporaryShardClassification.Tests.csproj'
$temporaryDirectDockerTestPath = Join-Path $temporaryProjectDirectory 'DirectDockerTests.cs'
$temporaryDirectDockerManifestPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-direct-docker-{0}.json" -f [Guid]::NewGuid().ToString('N'))
$temporaryDockerLookalikePath = Join-Path $repoRoot ("backend/tests/Nerv.IIP.Testing.Tests/TemporaryDockerLookalikes-{0}.cs" -f [Guid]::NewGuid().ToString('N'))
$temporarySolutionMemberDirectory = Join-Path $temporaryBackendInventory 'common/Nerv.IIP.TemporarySolutionMembership'
$temporarySolutionMemberPath = Join-Path $temporarySolutionMemberDirectory 'Nerv.IIP.TemporarySolutionMembership.csproj'
$temporaryWorkflowPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-{0}.yml" -f [Guid]::NewGuid().ToString('N'))
$timeoutResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-timeout-{0}" -f [Guid]::NewGuid().ToString('N'))
$executionTrxDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-execution-{0}" -f [Guid]::NewGuid().ToString('N'))
$temporaryPolicyPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-policy-{0}.json" -f [Guid]::NewGuid().ToString('N'))
$temporaryManifestPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-manifest-{0}.json" -f [Guid]::NewGuid().ToString('N'))
# The validator resolves policy sourcePath against the repository root, so the collision fixture
# must live inside the repo. artifacts/ is gitignored.
$temporaryCollisionRelativePath = "artifacts/backend-test-shards-collision-{0}.cs" -f [Guid]::NewGuid().ToString('N')
$temporaryCollisionSourcePath = Join-Path $repoRoot $temporaryCollisionRelativePath
$runnerPath = Join-Path $repoRoot 'scripts/run-backend-test-shard.ps1'
$diagnosticsPath = Join-Path $repoRoot 'scripts/lib/BackendTestShardDiagnostics.ps1'
$selectorAssertionsPath = Join-Path $repoRoot 'scripts/lib/BackendTestShardSelectors.ps1'
$timingAssertionsPath = Join-Path $repoRoot 'scripts/lib/BackendTestShardTimings.ps1'
$ciWorkflowBudgetsPath = Join-Path $repoRoot 'scripts/lib/CiWorkflowBudgets.ps1'

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$inventoryRelativeToRepo = [IO.Path]::GetRelativePath($repoRoot, $temporaryBackendInventory)
Assert-Contract ($inventoryRelativeToRepo.StartsWith('..', [StringComparison]::Ordinal)) 'Backend mutation fixtures must live outside the tracked repository tree.'
Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'backend/tests/Nerv.IIP.TemporaryShardClassification.Tests'))) 'The unclassified-project fixture must never be planted in the tracked backend tree.'
Assert-Contract (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'backend/common/Nerv.IIP.TemporarySolutionMembership'))) 'The solution-membership fixture must never be planted in the tracked backend tree.'

# Every assertion below is about *what the script under test said*, so it is always run as a real
# process and judged by its exit code plus its output.
#
# The validator reports findings on stdout and exits 1 rather than throwing, which is what lets
# these assertions match whole sentences instead of the short fragments a thrown (and therefore
# width-wrapped) message forced — this file used to also scrape the command log to reassemble that
# text, and both workarounds are gone. Why the shape matters:
# docs/architecture/backend-ci-build-strategy.md ("走查收尾" 第 3 条).
#
# Whitespace is collapsed so that where the script chose to break lines is not part of the
# contract. The assertions are about content, not layout.
#
# One helper, parameterized by script path: the validator, the balance report and the timing
# refresher are three programs invoked identically, and this file previously carried three
# character-identical copies of this body that differed only in which path they hard-coded.
function Invoke-GovernedScript {
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [string] $Name
    )

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments (@('-NoProfile', '-File', $ScriptPath) + $Arguments) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 300 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

$directDockerType = 'Nerv.IIP.TemporaryShardClassification.Tests.DirectDockerTests'
$directDockerFinding = "Real dependency test type '$directDockerType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
$directDockerExcludedType = 'Nerv.IIP.TemporaryShardClassification.Tests.AlreadyExcluded'
$containedDockerType = 'Nerv.IIP.TemporaryShardClassification.Tests.Unexcluded'
$containedDockerFinding = "Real dependency test type '$containedDockerType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
$ordinaryEmptyStringThenDockerType = 'Nerv.IIP.TemporaryShardClassification.Tests.OrdinaryEmptyStringThenDockerTests'
$ordinaryEmptyStringThenDockerFinding = "Real dependency test type '$ordinaryEmptyStringThenDockerType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
$verbatimEmptyStringThenDockerType = 'Nerv.IIP.TemporaryShardClassification.Tests.VerbatimEmptyStringThenDockerTests'
$verbatimEmptyStringThenDockerFinding = "Real dependency test type '$verbatimEmptyStringThenDockerType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
$interpolatedDockerType = 'Nerv.IIP.TemporaryShardClassification.Tests.InterpolatedDockerTests'
$interpolatedDockerFinding = "Real dependency test type '$interpolatedDockerType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
$interpolatedRawDockerType = 'Nerv.IIP.TemporaryShardClassification.Tests.InterpolatedRawDockerTests'
$interpolatedRawDockerFinding = "Real dependency test type '$interpolatedRawDockerType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
$dockerBclEntryTypes = @(
    'Nerv.IIP.TemporaryShardClassification.Tests.TwoArgumentConstructorDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.NamedConstructorDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ReorderedNamedConstructorDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.NestedArgumentConstructorDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.FullyQualifiedConstructorDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.GlobalQualifiedConstructorDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ParenthesizedConstructorDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ObjectInitializerDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.EmptyConstructorInitializerDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.NestedInitializerDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.AssignedFileNameDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.FieldAssignedFileNameDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.AliasAssignedFileNameDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ThisFieldAssignedFileNameDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.GlobalAliasAssignedFileNameDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ProcessStartInfoPropertyChainDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ProcessAliasStartInfoPropertyChainDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ParameterAssignedFileNameDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ProcessAliasStaticStartDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.SingleArgumentStaticProcessStartDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.StaticProcessStartDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.NamedStaticProcessStartDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ReorderedNamedStaticProcessStartDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.NestedArgumentStaticProcessStartDockerTests',
    'Nerv.IIP.TemporaryShardClassification.Tests.ParenthesizedNamedStaticProcessStartDockerTests'
)
try {
    New-Item -ItemType Directory -Path $temporaryProjectDirectory -Force | Out-Null
    Set-Content -LiteralPath $temporaryProjectPath -Value '<Project Sdk="Microsoft.NET.Sdk" />' -NoNewline
    Set-Content -LiteralPath $temporaryDirectDockerTestPath -NoNewline -Value @'
namespace Nerv.IIP.TemporaryShardClassification.Tests;

public sealed class DirectDockerTests
{
    [Fact]
    public void Starts_docker_directly()
    {
        _ = new ProcessStartInfo("docker");
    }
}
'@

    $directDocker = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-direct-docker-contract' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)
    Assert-Contract (-not $directDocker.Passed) 'An unexcluded test type using the audited Docker CLI primitive must fail shard governance.'
    Assert-Contract ($directDocker.Message.Contains($directDockerFinding, [StringComparison]::Ordinal)) 'Shard governance must report a direct Docker call in a single top-level test class.'

    Set-Content -LiteralPath $temporaryDirectDockerTestPath -NoNewline -Value @'
namespace Nerv.IIP.TemporaryShardClassification.Tests;

public sealed class AlreadyExcluded
{
}

public sealed class Unexcluded
{
    [Fact]
    public void Starts_docker_directly()
    {
        _ = new ProcessStartInfo("docker");
    }
}
'@

    $directDockerManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $directDockerShard = @($directDockerManifest.fastShards | Where-Object { [string]::Equals([string]([string] $_.id), [string]('business-core-a'), [StringComparison]::Ordinal) })
    Assert-Contract ($directDockerShard.Count -eq 1) 'The direct Docker containment fixture must resolve business-core-a exactly once.'
    $directDockerShard[0].excludedTestClasses = @(Get-NervStringsSorted -Values @(@($directDockerShard[0].excludedTestClasses) + $directDockerExcludedType) -Comparer ([StringComparer]::Ordinal) -Unique)
    Set-Content -LiteralPath $temporaryDirectDockerManifestPath -Value ($directDockerManifest | ConvertTo-Json -Depth 100) -NoNewline

    $containedDocker = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-direct-docker-containment-contract' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory, '-ManifestPath', $temporaryDirectDockerManifestPath)
    Assert-Contract (-not $containedDocker.Passed) 'A later unexcluded test type using the audited Docker CLI primitive must fail shard governance.'
    Assert-Contract ($containedDocker.Message.Contains($containedDockerFinding, [StringComparison]::Ordinal)) 'Shard governance must map the Docker primitive to the later containing outer test class instead of an earlier excluded class.'

    Set-Content -LiteralPath $temporaryDirectDockerTestPath -NoNewline -Value @'
namespace Nerv.IIP.TemporaryShardClassification.Tests;

public sealed class OrdinaryEmptyStringThenDockerTests
{
    [Fact]
    public void Starts_docker_after_empty_and_quote_like_ordinary_strings()
    {
        _ = $"";
        _ = "\"";
        _ = "" + "";
        _ = new ProcessStartInfo("docker");
    }
}
'@
    $ordinaryEmptyStringThenDocker = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-ordinary-empty-string-then-docker-contract' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)

    Set-Content -LiteralPath $temporaryDirectDockerTestPath -NoNewline -Value @'
namespace Nerv.IIP.TemporaryShardClassification.Tests;

public sealed class VerbatimEmptyStringThenDockerTests
{
    [Fact]
    public void Starts_docker_after_empty_and_quote_like_verbatim_strings()
    {
        _ = @"""";
        _ = @"";
        _ = new ProcessStartInfo("docker");
    }
}
'@
    $verbatimEmptyStringThenDocker = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-verbatim-empty-string-then-docker-contract' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)
    Assert-Contract (-not $verbatimEmptyStringThenDocker.Passed) 'A real Docker call after empty and quote-like verbatim strings must fail shard governance.'
    Assert-Contract ($verbatimEmptyStringThenDocker.Message.Contains($verbatimEmptyStringThenDockerFinding, [StringComparison]::Ordinal)) 'Shard governance must not let a verbatim empty string swallow a later Docker call and must report the exact containing test type.'
    Assert-Contract (-not $ordinaryEmptyStringThenDocker.Passed) 'A real Docker call after empty and quote-like ordinary strings must fail shard governance.'
    Assert-Contract ($ordinaryEmptyStringThenDocker.Message.Contains($ordinaryEmptyStringThenDockerFinding, [StringComparison]::Ordinal)) 'Shard governance must not let an ordinary empty string swallow a later Docker call and must report the exact containing test type.'

    Set-Content -LiteralPath $temporaryDirectDockerTestPath -NoNewline -Value @'
namespace Nerv.IIP.TemporaryShardClassification.Tests;

public sealed class InterpolatedDockerTests
{
    [Fact]
    public void Starts_docker_inside_an_ordinary_interpolation_hole()
    {
        _ = $"{new ProcessStartInfo("docker")}";
    }
}
'@
    $interpolatedDocker = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-interpolated-docker-contract' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)

    Set-Content -LiteralPath $temporaryDirectDockerTestPath -NoNewline -Value @'
namespace Nerv.IIP.TemporaryShardClassification.Tests;

public sealed class InterpolatedRawDockerTests
{
    [Fact]
    public void Starts_docker_inside_a_raw_interpolation_hole()
    {
        _ = $"""{new ProcessStartInfo("docker")}""";
    }
}
'@
    $interpolatedRawDocker = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-interpolated-raw-docker-contract' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)

    Assert-Contract (-not $interpolatedDocker.Passed) 'A real Docker call inside an ordinary interpolation hole must fail shard governance.'
    Assert-Contract ($interpolatedDocker.Message.Contains($interpolatedDockerFinding, [StringComparison]::Ordinal)) 'Shard governance must audit executable ordinary interpolation holes and report the exact containing test type.'
    Assert-Contract (-not $interpolatedRawDocker.Passed) 'A real Docker call inside an interpolated raw string hole must fail shard governance.'
    Assert-Contract ($interpolatedRawDocker.Message.Contains($interpolatedRawDockerFinding, [StringComparison]::Ordinal)) 'Shard governance must audit executable raw interpolation holes and report the exact containing test type.'

    Set-Content -LiteralPath $temporaryDirectDockerTestPath -NoNewline -Value @'
global using GlobalPsi = System.Diagnostics.ProcessStartInfo;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcAlias = System.Diagnostics.Process;
using Psi = System.Diagnostics.ProcessStartInfo;

namespace Nerv.IIP.TemporaryShardClassification.Tests;

public sealed class TwoArgumentConstructorDockerTests
{
    [Fact]
    public void Starts_docker_with_constructor_arguments() =>
        _ = new ProcessStartInfo("docker", "ps");
}

public sealed class NamedConstructorDockerTests
{
    [Fact]
    public void Starts_docker_with_named_constructor_arguments() =>
        _ = new ProcessStartInfo(fileName: "docker", arguments: "ps");
}

public sealed class ReorderedNamedConstructorDockerTests
{
    [Fact]
    public void Starts_docker_with_reordered_named_constructor_arguments() =>
        _ = new ProcessStartInfo(arguments: "ps", fileName: "docker");
}

public sealed class NestedArgumentConstructorDockerTests
{
    [Fact]
    public void Starts_docker_after_a_nested_constructor_argument() =>
        _ = new ProcessStartInfo(arguments: BuildArgs(), fileName: "docker");

    private static string BuildArgs() => "ps";
}

public sealed class FullyQualifiedConstructorDockerTests
{
    [Fact]
    public void Starts_docker_with_a_fully_qualified_constructor() =>
        _ = new System.Diagnostics.ProcessStartInfo("docker");
}

public sealed class GlobalQualifiedConstructorDockerTests
{
    [Fact]
    public void Starts_docker_with_a_global_qualified_constructor() =>
        _ = new global::System.Diagnostics.ProcessStartInfo("docker");
}

public sealed class ParenthesizedConstructorDockerTests
{
    [Fact]
    public void Starts_docker_with_a_parenthesized_file_name() =>
        _ = new System.Diagnostics.ProcessStartInfo(("docker"));
}

public sealed class ObjectInitializerDockerTests
{
    [Fact]
    public void Starts_docker_with_an_object_initializer() =>
        _ = new ProcessStartInfo { FileName = "docker", UseShellExecute = false };
}

public sealed class EmptyConstructorInitializerDockerTests
{
    [Fact]
    public void Starts_docker_with_an_empty_constructor_and_initializer() =>
        _ = new ProcessStartInfo() { FileName = "docker" };
}

public sealed class NestedInitializerDockerTests
{
    [Fact]
    public void Starts_docker_after_a_nested_collection_initializer() =>
        _ = new ProcessStartInfo { ArgumentList = { "ps" }, FileName = "docker" };
}

public sealed class AssignedFileNameDockerTests
{
    [Fact]
    public void Starts_docker_after_assigning_the_file_name_property()
    {
        var processStartInfo = new ProcessStartInfo();
        processStartInfo.FileName = "docker";
        _ = Process.Start(processStartInfo);
    }
}

public sealed class FieldAssignedFileNameDockerTests
{
    private readonly ProcessStartInfo processStartInfo;

    public FieldAssignedFileNameDockerTests()
    {
        processStartInfo = new ProcessStartInfo();
    }

    [Fact]
    public void Starts_docker_after_assigning_a_field_file_name()
    {
        processStartInfo.FileName = "docker";
        _ = Process.Start(processStartInfo);
    }
}

public sealed class AliasAssignedFileNameDockerTests
{
    [Fact]
    public void Starts_docker_after_assigning_an_alias_file_name()
    {
        var processStartInfo = new Psi();
        processStartInfo.FileName = "docker";
        _ = Process.Start(processStartInfo);
    }
}

public sealed class ThisFieldAssignedFileNameDockerTests
{
    private readonly ProcessStartInfo options = new();

    [Fact]
    public void Starts_docker_from_the_explicit_field_despite_a_shadowing_local()
    {
        var options = new CustomLaunchOptions();
        this.options.FileName = "docker";
        _ = Process.Start(this.options);
    }
}

public sealed class GlobalAliasAssignedFileNameDockerTests
{
    [Fact]
    public void Starts_docker_after_assigning_a_global_alias_file_name()
    {
        var processStartInfo = new GlobalPsi();
        processStartInfo.FileName = "docker";
        _ = Process.Start(processStartInfo);
    }
}

public sealed class ParameterAssignedFileNameDockerTests
{
    [Fact]
    public void Configures_a_parameter_for_docker()
    {
        Configure(new ProcessStartInfo());
    }

    private static void Configure(ProcessStartInfo target)
    {
        target.FileName = "docker";
    }
}

public sealed class ProcessStartInfoPropertyChainDockerTests
{
    [Fact]
    public void Starts_docker_through_the_process_start_info_property()
    {
        var process = new Process();
        process.StartInfo.FileName = "docker";
        _ = process.Start();
    }
}

public sealed class ProcessAliasStartInfoPropertyChainDockerTests
{
    [Fact]
    public void Starts_docker_through_an_aliased_process_type()
    {
        var process = new ProcAlias();
        process.StartInfo.FileName = "docker";
        _ = process.Start();
    }
}

public sealed class StaticProcessStartDockerTests
{
    [Fact]
    public void Starts_docker_with_the_static_process_api() =>
        _ = Process.Start("docker", "ps");
}

public sealed class ProcessAliasStaticStartDockerTests
{
    [Fact]
    public void Starts_docker_through_an_aliased_static_process_api() =>
        _ = ProcAlias.Start("docker");
}

public sealed class SingleArgumentStaticProcessStartDockerTests
{
    [Fact]
    public void Starts_docker_with_the_single_argument_static_process_api() =>
        _ = Process.Start("docker");
}

public sealed class NamedStaticProcessStartDockerTests
{
    [Fact]
    public void Starts_docker_with_named_static_process_arguments() =>
        _ = Process.Start(fileName: "docker", arguments: "ps");
}

public sealed class ReorderedNamedStaticProcessStartDockerTests
{
    [Fact]
    public void Starts_docker_with_reordered_named_static_process_arguments() =>
        _ = Process.Start(arguments: "ps", fileName: "docker");
}

public sealed class NestedArgumentStaticProcessStartDockerTests
{
    [Fact]
    public void Starts_docker_after_a_nested_static_process_argument() =>
        _ = Process.Start(arguments: BuildArgs(), fileName: "docker");

    private static string BuildArgs() => "ps";
}

public sealed class ParenthesizedNamedStaticProcessStartDockerTests
{
    [Fact]
    public void Starts_docker_with_a_parenthesized_named_file_name() =>
        _ = System.Diagnostics.Process.Start(fileName: ("docker"));
}
'@
    $dockerBclEntries = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-docker-bcl-entry-contract' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)
    Assert-Contract (-not $dockerBclEntries.Passed) 'Every audited BCL Docker process entry shape in an unexcluded fast-shard project must fail shard governance.'
    foreach ($dockerBclEntryType in $dockerBclEntryTypes) {
        $dockerBclEntryFinding = "Real dependency test type '$dockerBclEntryType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
        Assert-Contract ($dockerBclEntries.Message.Contains($dockerBclEntryFinding, [StringComparison]::Ordinal)) "Shard governance must report Docker BCL entry shape '$dockerBclEntryType'."
    }
}
finally {
    Remove-Item -LiteralPath $temporaryBackendInventory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryDirectDockerManifestPath -Force -ErrorAction SilentlyContinue
}

$dockerLookalikeType = 'Nerv.IIP.Testing.Tests.DockerLookalikeTests'
$dockerLookalikeFinding = "Real dependency test type '$dockerLookalikeType' uses the audited Docker CLI primitive but is not excluded from its fast shard."
Assert-Contract (-not (Test-Path -LiteralPath $temporaryDockerLookalikePath)) 'The Docker-lookalike fixture path must be unused before the test.'
try {
    Set-Content -LiteralPath $temporaryDockerLookalikePath -NoNewline -Value @'
namespace Nerv.IIP.Testing.Tests;

public sealed class DockerLookalikeTests
{
    // new ProcessStartInfo("docker") is documentation, not an invocation.
    /* A block comment containing new ProcessStartInfo("docker") is not an invocation either. */
    // new System.Diagnostics.ProcessStartInfo(arguments: "ps", fileName: "docker") is also documentation.
    private const string Ordinary = "new ProcessStartInfo(\"docker\")";
    private const string Verbatim = @"new ProcessStartInfo(""docker"")";
    private static string Interpolated => $"new ProcessStartInfo(\"docker\") {nameof(DockerLookalikeTests)}";
    private const string StaticStart = """Process.Start(arguments: "ps", fileName: "docker")""";
    private const string Initializer = """new ProcessStartInfo { FileName = "docker" }""";
    private const string Raw = """
        new ProcessStartInfo("docker")
        """;
}

public static class Process
{
    public static object? Start(string fileName) => null;
}

public sealed class CustomProcessTests
{
    [Fact]
    public void Starts_a_custom_process_type() =>
        _ = Process.Start("docker");
}

public sealed class CustomLaunchOptions
{
    public string FileName { get; set; } = "";
}

public sealed class CustomFileNameAssignmentTests
{
    [Fact]
    public void Assigns_a_custom_file_name_property()
    {
        var options = new CustomLaunchOptions();
        options.FileName = "docker";
    }
}

public sealed class CustomParameterFileNameAssignmentTests
{
    [Fact]
    public void Configures_a_custom_parameter()
    {
        Configure(new CustomLaunchOptions());
    }

    private static void Configure(CustomLaunchOptions target)
    {
        target.FileName = "docker";
    }
}

public sealed class CustomProcessWithStartInfo
{
    public CustomLaunchOptions StartInfo { get; } = new();
}

public sealed class CustomProcessStartInfoPropertyChainTests
{
    [Fact]
    public void Assigns_a_custom_start_info_property()
    {
        var process = new CustomProcessWithStartInfo();
        process.StartInfo.FileName = "docker";
    }
}

public sealed class CrossMethodExpressionBodiedParameterLeakTests
{
    private readonly CustomLaunchOptions target = new();

    private static void Earlier(System.Diagnostics.ProcessStartInfo target) => _ = target;

    [Fact]
    public void Assigns_the_custom_field_in_a_later_method()
    {
        target.FileName = "docker";
    }
}

public sealed class ShadowedFileNameAssignmentTests
{
    [Fact]
    public void Creates_a_process_start_info_in_one_scope()
    {
        var options = new ProcessStartInfo();
    }

    [Fact]
    public void Assigns_a_custom_file_name_in_another_scope()
    {
        var options = new CustomLaunchOptions();
        options.FileName = "docker";
    }
}

public sealed class ShadowedFieldFileNameAssignmentTests
{
    private readonly ProcessStartInfo options = new();

    [Fact]
    public void Assigns_a_shadowing_custom_file_name()
    {
        var options = new CustomLaunchOptions();
        options.FileName = "docker";
    }
}

public sealed class CustomFieldSelectedWithThisTests
{
    private readonly CustomLaunchOptions options = new();

    [Fact]
    public void Assigns_the_explicit_custom_field_despite_a_shadowing_bcl_local()
    {
        var options = new ProcessStartInfo();
        this.options.FileName = "docker";
    }
}
'@

    $dockerLookalike = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-docker-lookalike-contract'
    Assert-Contract $dockerLookalike.Passed 'Comments and C# string lookalikes must not fail real backend shard governance.'
    Assert-Contract (-not $dockerLookalike.Message.Contains($dockerLookalikeFinding, [StringComparison]::Ordinal)) 'Comments and C# string lookalikes must not produce a direct Docker finding.'
}
finally {
    Remove-Item -LiteralPath $temporaryDockerLookalikePath -Force -ErrorAction SilentlyContinue
}

Assert-Contract (Test-Path -LiteralPath $manifestPath) 'Backend test shard manifest is missing.'
Assert-Contract (Test-Path -LiteralPath $validatorPath) 'Backend test shard validator is missing.'

Invoke-PwshScript -ScriptPath $validatorPath -WorkingDirectory $repoRoot -Name 'backend-test-shard-validator'

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$fastShards = @($manifest.fastShards)
$heavyLanes = @($manifest.heavyLanes)
Assert-Contract ($fastShards.Count -eq 4) 'Phase 1 must define exactly four fast backend shards.'
Assert-Contract ([string]::Equals([string](((Get-NervStringsSorted -Values @(@($fastShards.id)) -Comparer ([StringComparer]::Ordinal)) -join '|')), [string]('business-core-a|business-core-b|business-gateway|platform'), [StringComparison]::Ordinal)) 'Fast shard IDs must remain the four phase-1 CI jobs.'
Assert-Contract ([string]::Equals([string](((Get-NervStringsSorted -Values @(@($heavyLanes.id)) -Comparer ([StringComparer]::Ordinal)) -join '|')), [string]('full-chain|performance|real-postgres|redis-cap'), [StringComparison]::Ordinal)) 'Heavy lane IDs must remain explicit and separate from fast shards, including the real Redis/CAP transport owner.'
$businessGatewayShard = @($fastShards | Where-Object { [string]::Equals([string]($_.id), [string]('business-gateway'), [StringComparison]::OrdinalIgnoreCase) })
# The BusinessGateway assembly used to be alone in its shard because it cost 869s and serialized
# every other assembly behind it. MAN-663 removed that cost (23s on run 30999368607) and MAN-669
# PR-A rebalanced the shards by measured TRX elapsed, so "exactly one project" is no longer the
# contract — the lane identity is. What must not drift is which shard owns that assembly, because
# MAN-661 maps evidence lane backend-shard-1 to this job's name.
Assert-Contract ($businessGatewayShard.Count -eq 1 -and [Collections.Generic.HashSet[string]]::new([string[]]@(@($businessGatewayShard[0].projects)), [StringComparer]::OrdinalIgnoreCase).Contains([string]('backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj'))) 'The BusinessGateway assembly must stay in the fast shard whose evidence lane is named after it.'
# Which fast shard owns the acceptance suite is a balancing decision (PR-A moved it from
# business-core-b to business-core-a); that it stays inside the *default fast gate* rather than
# drifting into an opt-in heavy lane is the contract.
$acceptanceOwners = @($fastShards | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@(@($_.projects)), [StringComparer]::OrdinalIgnoreCase).Contains([string]('backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj')) })
Assert-Contract ($acceptanceOwners.Count -eq 1) 'Regular business acceptance facts must be part of the default fast gate.'
$excludedSelectors = @(
    foreach ($shard in $fastShards) {
        $classes = $shard.PSObject.Properties['excludedTestClasses']
        $methods = $shard.PSObject.Properties['excludedTests']
        if ($null -ne $classes) { @($classes.Value) }
        if ($null -ne $methods) { @($methods.Value) }
    }
)
Assert-Contract ($excludedSelectors.Count -eq 54) 'Every currently excluded real-dependency test selector must be explicitly classified.'
Assert-Contract ([Collections.Generic.HashSet[string]]::new([string[]]@($excludedSelectors), [StringComparer]::Ordinal).Contains([string]('Nerv.IIP.Business.Inventory.Web.Tests.InventoryDirectoryPostgresTests'))) 'The Inventory directory PostgreSQL test class must be excluded from its fast shard.'
Assert-Contract ([Collections.Generic.HashSet[string]]::new([string[]]@($excludedSelectors), [StringComparer]::OrdinalIgnoreCase).Contains([string]('Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed'))) 'The PostgreSQL test database real selector must remain method-scoped.'
Assert-Contract (-not ([Collections.Generic.HashSet[string]]::new([string[]]@($excludedSelectors), [StringComparer]::OrdinalIgnoreCase).Contains([string]('Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests')))) 'A mixed fast test class must not be excluded wholesale.'
$platformShard = @($fastShards | Where-Object { [string]::Equals([string]($_.id), [string]('platform'), [StringComparison]::OrdinalIgnoreCase) })[0]
$platformExcludedClasses = @($platformShard.excludedTestClasses)
$platformExcludedTestsProperty = $platformShard.PSObject.Properties['excludedTests']
$platformExcludedTests = if ($null -eq $platformExcludedTestsProperty) { @() } else { @($platformExcludedTestsProperty.Value) }
Assert-Contract ([Collections.Generic.HashSet[string]]::new([string[]]@($platformExcludedTests), [StringComparer]::OrdinalIgnoreCase).Contains([string]('Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed'))) 'The PostgreSQL test database real selector must be in excludedTests, not the class selector list.'
Assert-Contract ([Collections.Generic.HashSet[string]]::new([string[]]@($platformExcludedTests), [StringComparer]::OrdinalIgnoreCase).Contains([string]('Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Initializer_failure_drops_database_and_redacts_diagnostics'))) 'Every narrowed PostgreSQL database selector must be method-scoped.'
Assert-Contract (-not ([Collections.Generic.HashSet[string]]::new([string[]]@($platformExcludedClasses), [StringComparer]::OrdinalIgnoreCase).Contains([string]('Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed')))) 'A method selector must not be treated as a class selector.'
$businessCoreBShard = @($fastShards | Where-Object { [string]::Equals([string]($_.id), [string]('business-core-b'), [StringComparison]::OrdinalIgnoreCase) })[0]
$businessCoreBExcludedClasses = [Collections.Generic.HashSet[string]]::new([string[]]@($businessCoreBShard.excludedTestClasses), [StringComparer]::Ordinal)
$businessCoreBExcludedTests = [Collections.Generic.HashSet[string]]::new([string[]]@($businessCoreBShard.excludedTests), [StringComparer]::Ordinal)
$demandPlanningClass = 'Nerv.IIP.Business.DemandPlanning.Web.Tests.ErpSalesOrderDemandConsumerTests'
$demandPlanningOwnedMethods = @(
    "$demandPlanningClass.PostgreSql_concurrent_versions_never_regress_order_watermark_or_demand",
    "$demandPlanningClass.PostgreSql_inbox_and_order_watermark_survive_duplicate_out_of_order_change_and_cancel",
    "$demandPlanningClass.PostgreSql_upgrade_reclassifies_legacy_manual_and_sales_order_collision_without_losing_traceability",
    "$demandPlanningClass.Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail",
    "$demandPlanningClass.Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres"
)
Assert-Contract (-not $businessCoreBExcludedClasses.Contains($demandPlanningClass)) 'The mixed DemandPlanning consumer class must not be excluded wholesale; its four ordinary facts belong to the fast shard.'
Assert-Contract (@($demandPlanningOwnedMethods | Where-Object { -not $businessCoreBExcludedTests.Contains($_) }).Count -eq 0) 'The three PostgreSQL and two Redis/CAP DemandPlanning methods must be handed to their heavy lanes individually.'
Assert-Contract ([string]::Equals([string](((Get-NervStringsSorted -Values @($businessCoreBShard.excludedTestLanes) -Comparer ([StringComparer]::Ordinal)) -join '|')), 'real-postgres|redis-cap', [StringComparison]::Ordinal)) 'Business Core B exclusions must derive exactly the PostgreSQL and Redis/CAP heavy owners.'
Assert-Contract (Test-Path -LiteralPath $diagnosticsPath) 'Timeout diagnostics must use a separately testable helper, not a production command bypass.'
Assert-Contract (Test-Path -LiteralPath $selectorAssertionsPath) 'Real PostgreSQL selector discovery and execution checks must be separately testable.'
. $diagnosticsPath
. $selectorAssertionsPath
. $ciWorkflowBudgetsPath

# An unrecognized status function must stay in the fail-closed evidence tier. PowerShell's
# `-notcontains` folds U+00AD, so `success<U+00AD>()` used to be treated as `success()` and the
# budget gate silently classified it as status-neutral.
$statusFunctionSoftHyphen = [string][char]0x00AD
Assert-Contract (Test-NervCiWorkflowConditionRunsAfterFailure -Condition "success$statusFunctionSoftHyphen()") `
    'A U+00AD-mutated status function must be treated as unknown and therefore evidence-publishing.'

$runnerBypassText = ''
try {
    Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $runnerPath, '-ShardId', 'platform', '-ResultsDirectory', $timeoutResultsDirectory, '-TrxFilePrefix', 'bypass-contract', '-TestCommand', 'Write-Output pass') -WorkingDirectory $repoRoot -Name 'backend-test-shard-command-parameter-contract' | Out-Null
    throw 'The production fast-shard runner must reject a command replacement parameter.'
}
catch {
    $runnerBypassText = $_.Exception.Message
}
Assert-Contract ($runnerBypassText.Contains("A parameter cannot be found that matches parameter name 'TestCommand'", [StringComparison]::Ordinal)) 'The production fast-shard runner must reject a command replacement parameter before test execution.'

$staleSelectorText = ''
try {
    Assert-BackendTestShardSelectorDiscovery -Selector 'Nerv.IIP.Tests.StaleSelector' -MethodSelector $true -DiscoveredTests @()
}
catch {
    $staleSelectorText = $_.Exception.Message
}
Assert-Contract ($staleSelectorText.Contains("Real PostgreSQL selector 'Nerv.IIP.Tests.StaleSelector' discovery must match exactly one test", [StringComparison]::Ordinal)) 'A stale real PostgreSQL selector must fail discovery before execution.'

$classSelector = 'Nerv.IIP.Tests.ClassSelector'
$classDiscovery = @(Assert-BackendTestShardSelectorDiscovery -Selector $classSelector -MethodSelector $false -DiscoveredTests @("$classSelector.CaseOne", "$classSelector.CaseTwo"))
Assert-Contract ($classDiscovery.Count -eq 2) 'A class-scoped real PostgreSQL selector must retain every discovered test.'
Assert-BackendTestShardSelectorExecution -Selector $classSelector -DiscoveredTests $classDiscovery -TrxResults @(
    [pscustomobject]@{ testName = "$classSelector.CaseOne"; outcome = 'Passed' },
    [pscustomobject]@{ testName = "$classSelector.CaseTwo"; outcome = 'Passed' }
)

$notExecutedSelectorText = ''
try {
    Assert-BackendTestShardSelectorExecution -Selector 'Nerv.IIP.Tests.DiscoveredSelector' -DiscoveredTests @('Nerv.IIP.Tests.DiscoveredSelector.Case') -TrxResults @()
}
catch {
    $notExecutedSelectorText = $_.Exception.Message
}
Assert-Contract ($notExecutedSelectorText.Contains("Real PostgreSQL selector 'Nerv.IIP.Tests.DiscoveredSelector' must execute every discovered test as Passed", [StringComparison]::Ordinal)) 'A discovered real PostgreSQL selector without TRX execution must fail closed.'

$runnerSource = Get-Content -LiteralPath $runnerPath -Raw
Assert-Contract (-not $runnerSource.Contains('No test matches the given testcase filter', [StringComparison]::Ordinal)) 'The zero-execution guard must not depend on localized dotnet console text.'
Assert-Contract ($runnerSource.Contains('Assert-BackendTestShardProjectExecution', [StringComparison]::Ordinal)) 'The fast shard runner must prove classified-project execution from the TRX the MAN-661 collector consumes.'
Assert-Contract ($runnerSource.Contains('"FullyQualifiedName!~$_."', [StringComparison]::Ordinal)) 'Class selectors must be anchored with a trailing dot so a sibling class sharing the prefix is not silently excluded.'

New-Item -ItemType Directory -Path $executionTrxDirectory -Force | Out-Null
Set-Content -LiteralPath (Join-Path $executionTrxDirectory 'shard.trx') -NoNewline -Value @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="00000000-0000-0000-0000-000000000001" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>
    <UnitTest id="00000000-0000-0000-0000-000000000002" name="Case" storage="/w/bin/Release/net10.0/Nerv.IIP.Coding.Tests.dll"><TestMethod className="Nerv.IIP.Coding.Tests.CodingTests" name="Case" /></UnitTest>
  </TestDefinitions>
</TestRun>
'@
$executedAssemblies = @(Get-BackendTestShardExecutedAssemblies -ResultsDirectory $executionTrxDirectory)
Assert-Contract ([string]::Equals([string]((@($executedAssemblies) -join '|')), [string]('Nerv.IIP.Coding.Tests.dll'), [StringComparison]::Ordinal)) 'Executed shard assemblies must be read from namespaced TRX storage attributes.'
Assert-BackendTestShardProjectExecution -ShardId 'contract' -ClassifiedProjects @('backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj') -ExecutedAssemblies $executedAssemblies

$zeroExecutionText = ''
try {
    Assert-BackendTestShardProjectExecution -ShardId 'contract' -ClassifiedProjects @('backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj', 'backend/tests/Nerv.IIP.Silent.Tests/Nerv.IIP.Silent.Tests.csproj') -ExecutedAssemblies $executedAssemblies
}
catch {
    $zeroExecutionText = $_.Exception.Message
}
Assert-Contract ($zeroExecutionText.Contains('produced no executed test result for classified projects: Nerv.IIP.Silent.Tests', [StringComparison]::Ordinal)) 'A classified project whose tests were all filtered away must fail closed regardless of console language.'

$driftText = ''
try {
    Assert-BackendTestShardProjectExecution -ShardId 'contract' -ClassifiedProjects @('backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj') -ExecutedAssemblies @($executedAssemblies + 'Nerv.IIP.Drifted.Tests.dll')
}
catch {
    $driftText = $_.Exception.Message
}
Assert-Contract ($driftText.Contains('executed assemblies it does not classify: Nerv.IIP.Drifted.Tests', [StringComparison]::Ordinal)) 'A shard running an assembly it does not classify must fail closed.'

# Ordinal identifier comparison (#1509). Every string these helpers compare is an identifier, and
# PowerShell's defaults are culture-aware: `Sort-Object -Unique` folds two values the collation table
# considers equivalent into one, and `-contains`/`-notcontains` report them as the same value. A
# U+00AD soft hyphen is enough — measured, not assumed. Both probes below pass under a culture-aware
# implementation and only fail under an ordinal one, which is what makes them regressions rather than
# restatements: revert either helper and this block goes red.
#
# Ordinal is the axis under test here; *case* is deliberately not, and the fixture right below this
# block is why. Keep the two apart when reading: the soft-hyphen probes must fail under a
# culture-aware comparer, and the lowercase-storage probe must pass under a case-insensitive one.
$ordinalSoftHyphen = [string][char]0x00AD
$ordinalSelectorShard = [pscustomobject]@{
    excludedTestClasses = @('Nerv.Probe.Ordinal.Alpha', "Nerv.Probe.Ordinal${ordinalSoftHyphen}.Alpha")
    excludedTests = @()
}
$ordinalSelectors = @(Get-BackendTestShardExcludedSelectors -Shard $ordinalSelectorShard)
Assert-Contract ($ordinalSelectors.Count -eq 2) "Two exclusion selectors differing only by an ignorable character are two selectors; deduplication must be ordinal, kept $($ordinalSelectors.Count)."

# The same function's -Kind keyword (#1509 round 3). [ValidateSet] compares culture-aware, so
# `-Kind "all<U+00AD>"` is *accepted* by the attribute; the body's `-in` folded it back to 'all', and
# the two agreed by accident. Making only the body ordinal would turn that into a silent empty
# result — the worst outcome, since an empty exclusion set reads as "this shard excludes nothing" —
# so an unmatched keyword throws. Both halves are asserted: the folded spelling fails loudly, and the
# case-insensitivity [ValidateSet] does promise still works.
$foldedKindText = ''
try {
    [void] (Get-BackendTestShardExcludedSelectors -Shard $ordinalSelectorShard -Kind "all$ordinalSoftHyphen")
}
catch {
    $foldedKindText = $_.Exception.Message
}
Assert-Contract ($foldedKindText.Contains('Unsupported excluded-selector kind', [StringComparison]::Ordinal)) "A selector kind that only matches by culture folding must throw rather than silently select nothing. Reported: '$foldedKindText'."
Assert-Contract (@(Get-BackendTestShardExcludedSelectors -Shard $ordinalSelectorShard -Kind 'Class').Count -eq 2) 'The selector kind keyword stays case-insensitive, which is what [ValidateSet] promises.'

$ordinalExecutionText = ''
try {
    Assert-BackendTestShardProjectExecution `
        -ShardId 'ordinal' `
        -ClassifiedProjects @('backend/tests/Nerv.Probe.Ordinal.Tests/Nerv.Probe.Ordinal.Tests.csproj') `
        -ExecutedAssemblies @("Nerv.Probe.Ordinal${ordinalSoftHyphen}.Tests.dll")
}
catch {
    $ordinalExecutionText = $_.Exception.Message
}
Assert-Contract ($ordinalExecutionText.Contains('produced no executed test result for classified projects: Nerv.Probe.Ordinal.Tests', [StringComparison]::Ordinal)) 'An assembly whose name differs from a classified project by an ignorable character must not be accepted as that project having run; the execution membership test must be ordinal.'

# …and the *other* side of the same guard, which had no probe at all (#1509 round 2): `$unexpected`
# asks whether an executed assembly is one this shard classifies. Its membership test used to be the
# same culture-aware `-contains`, so an assembly differing from a classified project by an ignorable
# character was accepted as classified and the solution-filter/manifest drift went unreported. The
# fixture keeps the exactly-matching assembly present so `$missing` stays empty and the throw can
# only come from the drift branch.
$ordinalDriftText = ''
try {
    Assert-BackendTestShardProjectExecution `
        -ShardId 'ordinal-drift' `
        -ClassifiedProjects @('backend/tests/Nerv.Probe.Drift.Tests/Nerv.Probe.Drift.Tests.csproj') `
        -ExecutedAssemblies @('Nerv.Probe.Drift.Tests.dll', "Nerv.Probe.Drift${ordinalSoftHyphen}.Tests.dll")
}
catch {
    $ordinalDriftText = $_.Exception.Message
}
Assert-Contract ($ordinalDriftText.Contains("executed assemblies it does not classify: Nerv.Probe.Drift${ordinalSoftHyphen}.Tests", [StringComparison]::Ordinal)) "An executed assembly differing from every classified project by an ignorable character is not classified by this shard; the drift membership test must be ordinal. Reported: '$ordinalDriftText'."
# Positive control, so the probe above cannot pass by reporting every executed assembly as drift.
Assert-BackendTestShardProjectExecution `
    -ShardId 'ordinal-drift' `
    -ClassifiedProjects @('backend/tests/Nerv.Probe.Drift.Tests/Nerv.Probe.Drift.Tests.csproj') `
    -ExecutedAssemblies @('Nerv.Probe.Drift.Tests.dll')

# The third ordinal decision in this library: the real-PostgreSQL selector gate. It asks whether every
# discovered test actually appears in the TRX, and that membership used to be `-notcontains`. A test
# whose TRX name differs from the discovered name by an ignorable character never ran under that
# name, so the gate must still report it missing — which is the direction it exists to fail closed on.
$ordinalSelectorExecutionText = ''
try {
    Assert-BackendTestShardSelectorExecution `
        -Selector 'Nerv.Probe.Selector' `
        -DiscoveredTests @('Nerv.Probe.Selector.Alpha') `
        -TrxResults @([pscustomobject]@{ testName = "Nerv.Probe.Selector.Alpha$ordinalSoftHyphen"; outcome = 'Passed' })
}
catch {
    $ordinalSelectorExecutionText = $_.Exception.Message
}
Assert-Contract ($ordinalSelectorExecutionText.Contains("must execute every discovered test as Passed; discovered=1, trx=1, missing=1", [StringComparison]::Ordinal)) 'A TRX test name differing from a discovered test by an ignorable character must not satisfy that test; the selector execution membership must be ordinal.'
# Positive control, so the probe above cannot pass by rejecting everything.
Assert-BackendTestShardSelectorExecution `
    -Selector 'Nerv.Probe.Selector' `
    -DiscoveredTests @('Nerv.Probe.Selector.Alpha') `
    -TrxResults @([pscustomobject]@{ testName = 'Nerv.Probe.Selector.Alpha'; outcome = 'Passed' })

# The *outcome* half of the same gate, which had no probe until #1509 round 3 and was the last
# culture-aware comparison left in the library. It used to read `[string] $_.outcome -ne 'Passed'`,
# and `"Passed$([char]0x00AD)" -ne 'Passed'` is False — so a result whose outcome token is not
# `Passed` folded into the passing set, `$failedResults` came back empty and a heavy-lane run with a
# failing test exited 0. Every neighbouring comparison in this function was already explicit; the one
# that decides pass/fail was not.
#
# Two fixtures, because the two failure modes are different strings: an ignorable-character variant of
# `Passed` (culture folding) and an outright `Failed` (the ordinary case, which the old code did
# catch — kept so the probe cannot pass by rejecting everything that is not literally `Passed`).
$foldedOutcomeText = ''
try {
    Assert-BackendTestShardSelectorExecution `
        -Selector 'Nerv.Probe.Outcome' `
        -DiscoveredTests @('Nerv.Probe.Outcome.Alpha') `
        -TrxResults @([pscustomobject]@{ testName = 'Nerv.Probe.Outcome.Alpha'; outcome = "Passed$ordinalSoftHyphen" })
}
catch {
    $foldedOutcomeText = $_.Exception.Message
}
Assert-Contract ($foldedOutcomeText.Contains('discovered=1, trx=1, missing=0, notPassed=1', [StringComparison]::Ordinal)) "An outcome that is not the literal token 'Passed' must count as not passed even when the collation table folds it into 'Passed'; the outcome comparison must be ordinal. Reported: '$foldedOutcomeText'."

$failedOutcomeText = ''
try {
    Assert-BackendTestShardSelectorExecution `
        -Selector 'Nerv.Probe.Outcome' `
        -DiscoveredTests @('Nerv.Probe.Outcome.Alpha') `
        -TrxResults @([pscustomobject]@{ testName = 'Nerv.Probe.Outcome.Alpha'; outcome = 'Failed' })
}
catch {
    $failedOutcomeText = $_.Exception.Message
}
Assert-Contract ($failedOutcomeText.Contains('notPassed=1', [StringComparison]::Ordinal)) 'A Failed result must still fail the selector execution gate.'

# ...and the case axis, which the fixture above deliberately does not exercise. VSTest writes the
# TRX `storage` path lowercased, so the executed side of this comparison never carries the manifest's
# casing: a real shard reports `nerv.iip.apphub.domain.tests.dll` against a classified
# `Nerv.IIP.AppHub.Domain.Tests.csproj`. This file used to prove the guard only with a hand-written
# fixture that kept the manifest casing, so a case-sensitive comparison passed here and failed every
# real shard — run 31251016878 named all 36 platform projects as unexecuted while all 36 had passed.
# The fixture below is the real shape, so that regression cannot come back green.
$lowercaseStorageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-storage-case-{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $lowercaseStorageDirectory -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $lowercaseStorageDirectory 'shard.trx') -NoNewline -Value @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="00000000-0000-0000-0000-000000000003" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>
    <UnitTest id="00000000-0000-0000-0000-000000000004" name="Case" storage="/home/runner/work/nerv-iip/nerv-iip/backend/tests/nerv.iip.apphub.domain.tests/bin/release/net10.0/nerv.iip.apphub.domain.tests.dll"><TestMethod className="Nerv.IIP.AppHub.Domain.Tests.SomeTests" name="Case" /></UnitTest>
  </TestDefinitions>
</TestRun>
'@
    $lowercaseAssemblies = @(Get-BackendTestShardExecutedAssemblies -ResultsDirectory $lowercaseStorageDirectory)
    # Ordinal, per the #1507 ruling: `-ceq` only disables case-insensitivity and still folds
    # ignorable characters, so it cannot pin an assembly name.
    Assert-Contract ([string]::Equals((@($lowercaseAssemblies) -join '|'), 'nerv.iip.apphub.domain.tests.dll', [StringComparison]::Ordinal)) 'The TRX storage attribute must be read verbatim; VSTest writes it lowercased and this fixture pins that shape.'
    Assert-BackendTestShardProjectExecution `
        -ShardId 'storage-case' `
        -ClassifiedProjects @('backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/Nerv.IIP.AppHub.Domain.Tests.csproj') `
        -ExecutedAssemblies $lowercaseAssemblies

    # Still discriminating: case folding must not turn the guard into "any assembly will do".
    $lowercaseDriftText = ''
    try {
        Assert-BackendTestShardProjectExecution `
            -ShardId 'storage-case' `
            -ClassifiedProjects @('backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj') `
            -ExecutedAssemblies $lowercaseAssemblies
    }
    catch {
        $lowercaseDriftText = $_.Exception.Message
    }
    Assert-Contract ($lowercaseDriftText.Contains('produced no executed test result for classified projects: Nerv.IIP.AppHub.Web.Tests', [StringComparison]::Ordinal)) 'Case-insensitive assembly matching must still reject an assembly that is a different project.'

    # Get-BackendTestShardExecutedAssemblies makes the same two-part decision when it deduplicates,
    # and both halves are pinned here because the two failure modes point in opposite directions:
    #   * culture-aware (`Sort-Object -Unique`) folds the soft-hyphen name into the plain one, so an
    #     assembly that never ran is reported as the one that did — 1 entry instead of 2;
    #   * strictly case-sensitive Ordinal keeps `NERV.…` and `nerv.…` apart, so one real assembly is
    #     counted twice and the shard's executed set drifts from its manifest — 3 entries instead of 2.
    # Only OrdinalIgnoreCase yields 2, which is what makes this one assertion discriminate both ways.
    Set-Content -LiteralPath (Join-Path $lowercaseStorageDirectory 'dedup.trx') -NoNewline -Value @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="00000000-0000-0000-0000-000000000005" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>
    <UnitTest id="00000000-0000-0000-0000-000000000006" name="Lower" storage="/w/bin/release/net10.0/nerv.probe.dedup.tests.dll"><TestMethod className="Nerv.Probe.Dedup.Tests.SomeTests" name="Lower" /></UnitTest>
    <UnitTest id="00000000-0000-0000-0000-000000000007" name="Upper" storage="/w/bin/Release/net10.0/NERV.PROBE.DEDUP.TESTS.dll"><TestMethod className="Nerv.Probe.Dedup.Tests.SomeTests" name="Upper" /></UnitTest>
    <UnitTest id="00000000-0000-0000-0000-000000000008" name="Ignorable" storage="/w/bin/release/net10.0/nerv.probe.dedup${ordinalSoftHyphen}.tests.dll"><TestMethod className="Nerv.Probe.Dedup.Tests.SomeTests" name="Ignorable" /></UnitTest>
  </TestDefinitions>
</TestRun>
"@
    $dedupAssemblies = @(Get-BackendTestShardExecutedAssemblies -ResultsDirectory $lowercaseStorageDirectory)
    $dedupProbe = @($dedupAssemblies | Where-Object { $_.StartsWith('nerv.probe.dedup', [StringComparison]::OrdinalIgnoreCase) })
    Assert-Contract ($dedupProbe.Count -eq 2) "Executed-assembly deduplication must be ordinal *and* case-insensitive: two spellings of one assembly collapse, an ignorable-character variant does not. Kept $($dedupProbe.Count)."
    Assert-Contract (@($dedupProbe | Where-Object { $_.Contains($ordinalSoftHyphen) }).Count -eq 1) 'The ignorable-character assembly must survive deduplication as its own entry.'
    Assert-Contract (@($dedupProbe | Where-Object { -not $_.Contains($ordinalSoftHyphen) }).Count -eq 1) 'The two case spellings of one assembly must collapse into a single entry.'
}
finally {
    if (Test-Path -LiteralPath $lowercaseStorageDirectory) {
        Remove-Item -LiteralPath $lowercaseStorageDirectory -Recurse -Force
    }
}

# ...and the file-wide closing statement, parsed rather than asserted in prose (#1509 round 3).
#
# Three rounds of this review each produced a "the file is now clean" claim and each was measured
# wrong afterwards, the last time on the one comparison that decides whether a heavy-lane failure is
# reported. So the claim is a scan, over the same axes the TestEvidence.ps1 contract uses (`-c*`
# operators, culture-aware operators against string literals, Sort-Object/Group-Object/Compare-Object/
# Select-Object -Unique/Where-Object comparison switches, `switch` on string clauses, string methods
# without an explicit [StringComparison], and a written-out non-ordinal [StringComparison]).
#
# Zero exceptions here, unlike TestEvidence.ps1's one: every string this library handles is an
# identifier. What the scan cannot see is enumerated by Get-NervOrdinalContractBlindSpots and pinned
# against synthetic sources in scripts/tests/test-evidence.Tests.ps1 — the same limits apply to this
# file, and stating them once beats restating them differently in two places.
. (Join-Path $repoRoot 'scripts/lib/OrdinalComparisonContract.ps1')
$selectorSweep = Get-NervOrdinalComparisonFindings -ScriptPath $selectorAssertionsPath -DisplayName 'BackendTestShardSelectors.ps1'
Assert-Contract ($selectorSweep.Findings.Count -eq 0) "scripts/lib/BackendTestShardSelectors.ps1 must compare identifiers ordinally (#1509):`n  $(@($selectorSweep.Findings) -join "`n  ")"
$runnerSweep = Get-NervOrdinalComparisonFindings -ScriptPath $runnerPath -DisplayName 'run-backend-test-shard.ps1'
Assert-Contract ($runnerSweep.Findings.Count -eq 0) "scripts/run-backend-test-shard.ps1 must compare shard identity ordinally (#1512):`n  $(@($runnerSweep.Findings) -join "`n  ")"
$timingSweep = Get-NervOrdinalComparisonFindings -ScriptPath $timingAssertionsPath -DisplayName 'BackendTestShardTimings.ps1'
Assert-Contract ($timingSweep.Findings.Count -eq 0) "scripts/lib/BackendTestShardTimings.ps1 must compare timing identities ordinally (#1512):`n  $(@($timingSweep.Findings) -join "`n  ")"
$validatorSweep = Get-NervOrdinalComparisonFindings -ScriptPath $validatorPath -DisplayName 'verify-backend-test-shards.ps1'
Assert-Contract ($validatorSweep.Findings.Count -eq 0) "scripts/verify-backend-test-shards.ps1 must compare shard-governance identifiers ordinally (#1512):`n  $(@($validatorSweep.Findings) -join "`n  ")"
$aggregateAssertionEquality = '-not [string]::Equals($aggregateRun.Replace("`r`n", "`n").TrimEnd(), $expectedAggregateRun.Replace("`r`n", "`n").TrimEnd(), [StringComparison]::Ordinal)'
$validatorSource = [IO.File]::ReadAllText($validatorPath)
Assert-Contract ([regex]::Matches($validatorSource, [regex]::Escape($aggregateAssertionEquality)).Count -eq 1) 'The aggregate selected/skipped contract must use explicit ordinal equality after line-ending normalization.'

$lastIndexProductionProbeRoot = Join-Path ([IO.Path]::GetTempPath()) ("nerv-iip-production-last-index-ordinal-{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($lastIndexProductionProbeRoot) | Out-Null
    foreach ($lastIndexCase in @(
            [pscustomobject]@{
                Name = 'validator-method-selector'
                SourcePath = $validatorPath
                Original = "`$methodSelector.LastIndexOf('.', [StringComparison]::Ordinal)"
                Mutated = "`$methodSelector.LastIndexOf('.')"
            },
            [pscustomobject]@{
                Name = 'timing-assembly-leaf'
                SourcePath = $timingAssertionsPath
                Original = "`$trimmed.LastIndexOf('/', [StringComparison]::Ordinal)"
                Mutated = "`$trimmed.LastIndexOf('/')"
            }
        )) {
        $lastIndexSource = [IO.File]::ReadAllText([string]$lastIndexCase.SourcePath)
        Assert-Contract ([regex]::Matches($lastIndexSource, [regex]::Escape([string]$lastIndexCase.Original)).Count -eq 1) "LastIndexOf mutation '$($lastIndexCase.Name)' must target exactly one production callsite."
        $lastIndexProbePath = Join-Path $lastIndexProductionProbeRoot "$($lastIndexCase.Name).ps1"
        [IO.File]::WriteAllText($lastIndexProbePath, $lastIndexSource.Replace([string]$lastIndexCase.Original, [string]$lastIndexCase.Mutated), [Text.UTF8Encoding]::new($false))
        $lastIndexMutationFindings = @((Get-NervOrdinalComparisonFindings -ScriptPath $lastIndexProbePath -DisplayName "$($lastIndexCase.Name)-mutation.ps1").Findings)
        Assert-Contract ($lastIndexMutationFindings.Count -eq 1 -and $lastIndexMutationFindings[0].Contains('[string-method-without-ordinal-comparison]', [StringComparison]::Ordinal)) "Weakening production LastIndexOf '$($lastIndexCase.Name)' must make the ordinal scanner fail."
    }
}
finally {
    if (Test-Path -LiteralPath $lastIndexProductionProbeRoot) { Remove-Item -LiteralPath $lastIndexProductionProbeRoot -Recurse -Force }
}

# Exercise the real manifest lookup with the U+00AD value that PowerShell's -eq folds away. The
# canonical runner must reject it before resolving or executing any solution filter. A copied
# production runner with only that comparison weakened must miss this diagnostic, proving the test
# is attached to the call site rather than merely to the scanner implementation.
$runnerOrdinalRoot = Join-Path ([IO.Path]::GetTempPath()) ("nerv-iip-backend-runner-ordinal-{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    $runnerOrdinalManifest = Join-Path $runnerOrdinalRoot 'manifest.json'
    [IO.Directory]::CreateDirectory($runnerOrdinalRoot) | Out-Null
    [IO.File]::WriteAllText($runnerOrdinalManifest, '{"fastShards":[{"id":"platform","solutionFilter":"missing.slnf","projects":[]}]}', [Text.UTF8Encoding]::new($false))
    $softHyphenShardId = "platform$([char]0x00AD)"
    $canonicalFailure = Invoke-GovernedScript -ScriptPath $runnerPath -Arguments @('-ShardId', $softHyphenShardId, '-ManifestPath', $runnerOrdinalManifest, '-ResultsDirectory', (Join-Path $runnerOrdinalRoot 'results'), '-TrxFilePrefix', 'ordinal-probe') -Name 'backend-runner-ordinal-canonical'
    Assert-Contract (-not $canonicalFailure.Passed -and $canonicalFailure.Message.Contains('must be defined exactly once', [StringComparison]::Ordinal)) `
        'The real shard lookup must reject a U+00AD-suffixed identity before resolving the selected shard.'

    $mutatedScripts = Join-Path $runnerOrdinalRoot 'mutated/scripts'
    [IO.Directory]::CreateDirectory((Join-Path $mutatedScripts 'lib')) | Out-Null
    foreach ($libraryName in @('ScriptAutomation.ps1', 'BackendTestShardSelectors.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/lib/$libraryName") -Destination (Join-Path $mutatedScripts "lib/$libraryName")
    }
    $mutatedRunner = Join-Path $mutatedScripts 'run-backend-test-shard.ps1'
    $runnerSource = [IO.File]::ReadAllText($runnerPath)
    $ordinalLookup = '[string]::Equals([string]$_.id, $ShardId, [StringComparison]::Ordinal)'
    Assert-Contract ([regex]::Matches($runnerSource, [regex]::Escape($ordinalLookup)).Count -eq 1) 'Runner lookup mutation must target exactly one production call site.'
    [IO.File]::WriteAllText($mutatedRunner, $runnerSource.Replace($ordinalLookup, '$_.id -eq $ShardId'), [Text.UTF8Encoding]::new($false))
    $mutatedFailure = Invoke-GovernedScript -ScriptPath $mutatedRunner -Arguments @('-ShardId', $softHyphenShardId, '-ManifestPath', $runnerOrdinalManifest, '-ResultsDirectory', (Join-Path $runnerOrdinalRoot 'results'), '-TrxFilePrefix', 'ordinal-probe') -Name 'backend-runner-ordinal-weakened'
    Assert-Contract (-not $mutatedFailure.Message.Contains('must be defined exactly once', [StringComparison]::Ordinal)) `
        'Weakening the real shard lookup to -eq must make the U+00AD mutation probe red.'
}
finally {
    if (Test-Path -LiteralPath $runnerOrdinalRoot) { Remove-Item -LiteralPath $runnerOrdinalRoot -Recurse -Force }
}

$classifiedProjects = @($fastShards.projects | ForEach-Object { [string] $_ }) + @($heavyLanes.projects | ForEach-Object { [string] $_ })
Assert-Contract ((Get-BackendTestShardUniqueSorted -Values $classifiedProjects).Count -eq $classifiedProjects.Count) 'Every backend test project must be classified exactly once.'

# What the deleted assertion guarded, and who guards it now (#1509).
#
# This line used to read `$classifiedProjects.Count -eq 66`. The number is a *measurement* — how
# many backend test projects exist today — so every added test project turned this gate red until a
# human retyped it, which is the same "刷新仪式" #1507 removed from the timing data. But deleting a
# red gate is not the same as removing the need for one, so state the guard explicitly:
#
#   * it caught a project silently dropped out of the manifest — now caught by
#     `$missingFromManifest`, against the very solution the shards build;
#   * it caught a manifest row naming a project that is not part of the backend test inventory —
#     now caught by `$notInSolution`. That is deliberately *not* the same rule as the validator's
#     "Classified projects are not discovered backend test projects": the validator's inventory is a
#     filesystem glob for `**/*.Tests.csproj`, this one is membership of backend/Nerv.IIP.sln. A
#     project that exists on disk but was never added to the solution passes the validator and fails
#     here — and it is the one that matters, because a shard runs a `.slnf` filter over the solution,
#     so an unlisted project is never built or tested no matter how many files sit next to it. Both
#     are kept: they fail on different defects, and the cheaper of the two is the one already run by
#     verify-backend-test-shards.ps1 in a separate job;
#   * it never caught anything else, because "the count is 66" cannot distinguish which 66.
#
# Coverage goes up rather than down: the set comparison also fails when one project is swapped for
# another (count unchanged, inventory wrong), which `-eq 66` could not see.
#
# The expected set is derived from backend/Nerv.IIP.sln rather than from a filesystem glob on
# purpose. The validator already globs `**/*.Tests.csproj`; re-globbing here would assert this
# file's own arithmetic. The solution is the artifact each shard's `.slnf` is a filter over, so a
# project that is in the solution but unclassified is exactly the defect that reaches CI.
#
# The path is taken as the whole quoted run (`[^"]*`), not "up to the first space". A solution file
# always quotes project paths, so the quotes are the real delimiter; excluding the space instead made
# a project whose directory contains a space simply not match, which is a blind spot exactly where
# the coverage check is supposed to be total.
function Get-BackendSolutionTestProjects {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $SolutionLines)

    return @(
        $SolutionLines | ForEach-Object {
            if ($_ -match '"(?<path>[^"]*Tests\.csproj)"') { 'backend/' + ($Matches.path -replace '\\', '/') }
        }
    )
}

# Control: a solution row whose path carries a space must still be derived. Under the old
# `[^" ]*` spelling this line yields nothing and the project silently drops out of the expected set.
$spacedSolutionLine = 'Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Nerv.IIP.Spaced.Tests", "tests\Nerv IIP Spaced.Tests\Nerv.IIP.Spaced.Tests.csproj", "{00000000-0000-0000-0000-000000000009}"'
Assert-Contract ([string]::Equals(((Get-BackendSolutionTestProjects -SolutionLines @($spacedSolutionLine)) -join '|'), 'backend/tests/Nerv IIP Spaced.Tests/Nerv.IIP.Spaced.Tests.csproj', [StringComparison]::Ordinal)) 'A backend test project whose solution path contains a space must still be derived from the solution; the path is delimited by quotes, not by whitespace.'

# …and the suffix is `Tests.csproj`, not `.Tests.csproj` (#1509 round 6). The narrower spelling missed
# `*.IntegrationTests.csproj`-style names entirely, and the miss was one-sided in the dangerous
# direction: `$notInSolution` still catches a manifest row naming a project the solution does not
# have, but `$missingFromManifest` cannot catch an unclassified project it never derived. Today the
# solution contains no such name, so this only widens what a future one is measured against.
$integrationSolutionLine = 'Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Nerv.IIP.Probe.IntegrationTests", "tests\Nerv.IIP.Probe.IntegrationTests\Nerv.IIP.Probe.IntegrationTests.csproj", "{00000000-0000-0000-0000-00000000000A}"'
Assert-Contract ([string]::Equals(((Get-BackendSolutionTestProjects -SolutionLines @($integrationSolutionLine)) -join '|'), 'backend/tests/Nerv.IIP.Probe.IntegrationTests/Nerv.IIP.Probe.IntegrationTests.csproj', [StringComparison]::Ordinal)) 'A backend test project named *IntegrationTests.csproj must be derived from the solution; deriving only *.Tests.csproj drops it out of the expected set with nothing else catching it.'

$solutionTestProjects = Get-BackendTestShardUniqueSorted -Values @(
    Get-BackendSolutionTestProjects -SolutionLines @(Get-Content -LiteralPath (Join-Path $repoRoot 'backend/Nerv.IIP.sln'))
)
Assert-Contract ($solutionTestProjects.Count -gt 0) 'The backend solution must list backend test projects; an empty derivation would make the coverage comparison below vacuous.'
$classifiedProjectSet = Get-BackendTestShardMembershipSet -Values $classifiedProjects
$solutionTestProjectSet = Get-BackendTestShardMembershipSet -Values $solutionTestProjects
$missingFromManifest = @($solutionTestProjects | Where-Object { -not $classifiedProjectSet.Contains($_) })
$notInSolution = @($classifiedProjects | Where-Object { -not $solutionTestProjectSet.Contains($_) })
Assert-Contract ($missingFromManifest.Count -eq 0) "Every backend test project in backend/Nerv.IIP.sln must be classified by the shard manifest; unclassified: $($missingFromManifest -join ', ')."
Assert-Contract ($notInSolution.Count -eq 0) "Every classified shard project must be a backend test project in backend/Nerv.IIP.sln; stale: $($notInSolution -join ', ')."
# Report-only, deliberately: the inventory size is a measurement to read, never a gate to satisfy.
Write-Host "  [report-only] classified backend test projects: $($classifiedProjects.Count)"
Assert-Contract ([Collections.Generic.HashSet[string]]::new([string[]]@(@($fastShards | Where-Object { [string]::Equals([string]($_.id), [string]('platform'), [StringComparison]::OrdinalIgnoreCase) })[0].projects), [StringComparer]::OrdinalIgnoreCase).Contains([string]('backend/tests/Nerv.IIP.Testing.Tests/Nerv.IIP.Testing.Tests.csproj'))) 'MAN-662 shared test-infrastructure facts must run in the default fast gate.'
Assert-Contract ([Collections.Generic.HashSet[string]]::new([string[]]@(@($fastShards | Where-Object { [string]::Equals([string]($_.id), [string]('platform'), [StringComparison]::OrdinalIgnoreCase) })[0].projects), [StringComparer]::OrdinalIgnoreCase).Contains([string]('backend/tests/Nerv.IIP.FastEndpoints.ProcessIsolation.Tests/Nerv.IIP.FastEndpoints.ProcessIsolation.Tests.csproj'))) 'MAN-662 FastEndpoints process-isolation facts must run in the default fast gate.'
Assert-Contract ([string]::Equals([string](((Get-NervStringsSorted -Values @(@($fastShards.evidenceLane)) -Comparer ([StringComparer]::Ordinal)) -join '|')), [string]('backend-shard-1|backend-shard-2|backend-shard-3|backend-shard-4'), [StringComparison]::Ordinal)) 'Every fast shard must own one MAN-661 schema-v1 backend shard lane.'
Assert-Contract ((Get-NervStringsSorted -Values @(@($fastShards.jobName)) -Comparer ([StringComparer]::Ordinal) -Unique).Count -eq $fastShards.Count) 'Every fast shard evidence lane must be owned by exactly one CI job name.'
. (Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1')
$laneJobs = Get-NervTestEvidenceLaneJobs
foreach ($shard in $fastShards) {
    Assert-Contract ($laneJobs.Contains([string] $shard.evidenceLane)) "Fast shard evidence lane '$($shard.evidenceLane)' must be allowlisted for MAN-661 rerun and baseline authority."
    Assert-Contract ([string]::Equals([string]([string] $laneJobs[[string] $shard.evidenceLane]), [string]([string] $shard.jobName), [StringComparison]::Ordinal)) "Fast shard evidence lane '$($shard.evidenceLane)' must be bound to its own CI job name."
}

foreach ($shard in $fastShards) {
    $filterPath = Join-Path $repoRoot $shard.solutionFilter
    $filter = Get-Content -LiteralPath $filterPath -Raw | ConvertFrom-Json
    Assert-Contract ([string]::Equals([string]($filter.solution.path), [string]('../Nerv.IIP.sln'), [StringComparison]::OrdinalIgnoreCase)) "Solution filter $($shard.solutionFilter) must target the backend solution."
    Assert-Contract ((@($filter.solution.projects | Where-Object { $_ -match '^\.\./' })).Count -eq 0) "Solution filter $($shard.solutionFilter) project paths must be relative to backend/Nerv.IIP.sln."
}

# Solution membership must be enforced for *non-test* backend projects too. A project reachable only
# as a transitive ProjectReference has no entry in the solution configuration map, so a
# `--configuration Release` shard emits it into bin/Debug and every shard silently tests Release
# assemblies linked against a Debug dependency. Planting a non-test project proves the check is the
# general one and not the pre-existing `*.Tests.csproj`-only rule: this fixture is invisible to that
# rule, so if the general check is weakened away the validator passes and this contract goes red.
$solutionMembership = $null
try {
    New-Item -ItemType Directory -Path $temporarySolutionMemberDirectory -Force | Out-Null
    Set-Content -LiteralPath $temporarySolutionMemberPath -Value '<Project Sdk="Microsoft.NET.Sdk" />' -NoNewline

    $solutionMembership = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-solution-membership' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)
}
finally {
    if (Test-Path -LiteralPath $temporarySolutionMemberDirectory) {
        Remove-Item -LiteralPath $temporarySolutionMemberDirectory -Recurse -Force
    }
}
Assert-Contract (-not $solutionMembership.Passed) 'A backend project outside backend/Nerv.IIP.sln must fail shard governance.'
Assert-Contract ($solutionMembership.Message.Contains('bin/Debug', [StringComparison]::Ordinal)) 'Shard governance must reject a backend project that is not a solution member, naming the Release/Debug consequence.'
Assert-Contract ($solutionMembership.Message.Contains('backend/common/Nerv.IIP.TemporarySolutionMembership/Nerv.IIP.TemporarySolutionMembership.csproj', [StringComparison]::Ordinal)) 'The solution-membership failure must identify the offending project path.'
Assert-Contract (-not $solutionMembership.Message.Contains('Unclassified backend test', [StringComparison]::Ordinal)) 'The solution-membership contract must be tripped by a non-test project, not by the test classification rule.'
Assert-Contract (@(Get-Content -LiteralPath (Join-Path $repoRoot 'backend/Nerv.IIP.sln') | Where-Object { $_ -match 'Nerv\.IIP\.Contracts\.Mes\.csproj' }).Count -eq 1) 'Nerv.IIP.Contracts.Mes must stay a solution member; outside the solution every Release shard builds it as Debug.'

try {
    New-Item -ItemType Directory -Path $temporaryProjectDirectory -Force | Out-Null
    Set-Content -LiteralPath $temporaryProjectPath -Value '<Project Sdk="Microsoft.NET.Sdk" />' -NoNewline

    $unclassified = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-unclassified-project' -Arguments @('-BackendInventoryRoot', $temporaryBackendInventory)
    Assert-Contract (-not $unclassified.Passed) 'An unclassified temporary backend test project must fail classification.'
    Assert-Contract ($unclassified.Message.Contains('Unclassified backend test', [StringComparison]::Ordinal)) 'Unclassified project failure must identify the classification error.'
    Assert-Contract ($unclassified.Message.Contains('backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/Nerv.IIP.TemporaryShardClassification.Tests.csproj', [StringComparison]::Ordinal)) 'Unclassified project failure must identify the temporary project path.'

    $workflowContent = Get-Content -LiteralPath $workflowPath -Raw
    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^\s+- backend-tests-business-core-b\r?\n', '') -NoNewline
    $workflowValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-workflow-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $workflowValidation.Passed) 'A workflow with a missing aggregate dependency must fail structured shard governance.'
    Assert-Contract ($workflowValidation.Message.Contains('Backend Tests aggregate must need exactly the impact plan, governance, and four fast shard jobs.', [StringComparison]::Ordinal)) 'Structured workflow validation must reject an aggregate with a missing shard dependency.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent.Replace("  backend-test-shard-governance:$([Environment]::NewLine)", "  backend-test-shard-governance-missing:$([Environment]::NewLine)")) -NoNewline
    $missingGovernanceValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-missing-governance-job' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $missingGovernanceValidation.Passed) 'A missing backend shard governance job must fail structured shard governance.'
    Assert-Contract ($missingGovernanceValidation.Message.Contains("CI workflow is missing backend execution job 'backend-test-shard-governance'.", [StringComparison]::Ordinal)) 'Structured workflow validation must identify the missing backend shard governance job.'

    # The aggregate needs list is an exact job-identity set. U+00AD must not be folded into the
    # real platform job name by a culture-aware joined-string comparison.
    $platformNeed = '      - backend-tests-platform'
    $workflowWithMutatedPlatformNeed = $workflowContent.Replace($platformNeed, "${platformNeed}$statusFunctionSoftHyphen")
    Assert-Contract (-not [string]::Equals($workflowWithMutatedPlatformNeed, $workflowContent, [StringComparison]::Ordinal)) 'The aggregate-needs U+00AD mutation must target the canonical platform job line.'
    Set-Content -LiteralPath $temporaryWorkflowPath -Value $workflowWithMutatedPlatformNeed -NoNewline
    $mutatedNeedValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-aggregate-needs-ordinal-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $mutatedNeedValidation.Passed) 'A U+00AD-mutated aggregate need must fail exact shard governance.'
    Assert-Contract ($mutatedNeedValidation.Message.Contains('Backend Tests aggregate must need exactly the impact plan, governance, and four fast shard jobs.', [StringComparison]::Ordinal)) 'Structured workflow validation must reject a U+00AD-mutated aggregate need.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "\$expected_result"', 'echo "${{ needs.backend-tests-platform.result }}"') -NoNewline
    $noOpValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-noop-aggregate-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $noOpValidation.Passed) 'A no-op aggregate dependency expression must fail structured shard governance.'
    Assert-Contract ($noOpValidation.Message.Contains('Backend Tests aggregate must retain the fail-closed selected-success and unselected-skipped contract and audit reason.', [StringComparison]::Ordinal)) 'Structured workflow validation must reject a non-failing aggregate dependency expression.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "\$expected_result"', 'test "${{ needs.backend-tests-platform.result }}" = "$expected_result" || true') -NoNewline
    $maskedFailureValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-masked-aggregate-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $maskedFailureValidation.Passed) 'An aggregate assertion masked with || true must fail structured shard governance.'
    Assert-Contract ($maskedFailureValidation.Message.Contains('Backend Tests aggregate must retain the fail-closed selected-success and unselected-skipped contract and audit reason.', [StringComparison]::Ordinal)) 'Structured workflow validation must reject a masked aggregate dependency assertion.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent.Replace('            expected_result="success"', '            expected_result="skipped"')) -NoNewline
    $selectedAllowsSkipValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-selected-allows-skip' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $selectedAllowsSkipValidation.Passed) 'The selected Backend Tests policy must reject skipped execution jobs.'
    Assert-Contract ($selectedAllowsSkipValidation.Message.Contains('Backend Tests aggregate must retain the fail-closed selected-success and unselected-skipped contract and audit reason.', [StringComparison]::Ordinal)) 'Selected Backend Tests must only accept successful execution jobs.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent.Replace('          expected_result="skipped"', '          expected_result="success"')) -NoNewline
    $unselectedAllowsSuccessValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-unselected-allows-success' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $unselectedAllowsSuccessValidation.Passed) 'The unselected Backend Tests policy must reject unexpectedly successful execution jobs.'
    Assert-Contract ($unselectedAllowsSuccessValidation.Message.Contains('Backend Tests aggregate must retain the fail-closed selected-success and unselected-skipped contract and audit reason.', [StringComparison]::Ordinal)) 'Unselected Backend Tests must only accept precisely skipped execution jobs.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^(\s+- name: Require all backend fast shards\r?\n)', ('$1        continue-on-error: true' + [Environment]::NewLine)) -NoNewline
    $continueOnErrorValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-continue-on-error-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $continueOnErrorValidation.Passed) 'An aggregate step with continue-on-error must fail structured shard governance.'
    Assert-Contract ($continueOnErrorValidation.Message.Contains("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.", [StringComparison]::Ordinal)) 'Structured workflow validation must reject an aggregate continue-on-error configuration.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^(    if: always\(\)\r?\n)', ('$1    continue-on-error: true' + [Environment]::NewLine)) -NoNewline
    $jobContinueOnErrorValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-job-continue-on-error-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $jobContinueOnErrorValidation.Passed) 'An aggregate job with continue-on-error must fail structured shard governance.'
    Assert-Contract ($jobContinueOnErrorValidation.Message.Contains("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.", [StringComparison]::Ordinal)) 'Structured workflow validation must reject an aggregate job continue-on-error configuration.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)(-TrxFilePrefix backend-tests-platform)', '$1 -TestCommand "Write-Output pass"') -NoNewline
    $bypassValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-command-bypass-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $bypassValidation.Passed) 'A fast shard command replacement parameter must fail structured shard governance.'
    Assert-Contract ($bypassValidation.Message.Contains("Fast shard job 'backend-tests-platform' must not supply a command replacement parameter.", [StringComparison]::Ordinal)) 'Structured workflow validation must reject a command replacement parameter.'

    foreach ($evidenceMutation in @(
            @{
                Name = 'raw-artifact-upload'
                Pattern = '(?m)^(\s+)path: \$\{\{ steps\.collect-shard-evidence\.outputs\.evidence-path \}\}'
                Replacement = '$1path: artifacts/test-evidence-raw/${{ github.run_id }}/attempt-${{ github.run_attempt }}/backend-shard-1'
                Expected = 'must upload only the collector-published redacted evidence path'
            },
            @{
                Name = 'sibling-lane-claim'
                Pattern = '-SelectedLanes backend-shard-1'
                Replacement = '-SelectedLanes backend-shard-2'
                Expected = "must not claim the sibling evidence lane 'backend-shard-2'"
            },
            @{
                Name = 'piped-shard-runner'
                Pattern = '(?m)^(\s+)-TrxFilePrefix backend-tests-platform$'
                Replacement = '$1-TrxFilePrefix backend-tests-platform | tee shard.log'
                Expected = 'must not wrap the shard runner in a shell pipeline'
            },
            @{
                Name = 'best-effort-collection'
                Pattern = '(?m)^(\s+)id: collect-shard-evidence\r?\n(\s+)if: always\(\)'
                Replacement = '$1id: collect-shard-evidence' + [Environment]::NewLine + '$2if: success()'
                Expected = 'evidence collection must run with if: always()'
            }
        )) {
        Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace $evidenceMutation.Pattern, $evidenceMutation.Replacement) -NoNewline
        $evidenceValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name "backend-test-shard-evidence-$($evidenceMutation.Name)-contract" -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
        Assert-Contract (-not $evidenceValidation.Passed) "Evidence mutation '$($evidenceMutation.Name)' must fail structured shard governance."
        Assert-Contract ($evidenceValidation.Message.Contains($evidenceMutation.Expected)) "Structured workflow validation must reject the '$($evidenceMutation.Name)' evidence mutation."
    }

    $policy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    foreach ($rule in @($policy.rules)) {
        if ([string]::Equals([string]([string] $rule.requiredLane), [string]('postgres'), [StringComparison]::Ordinal)) {
            $rule.testIdentities = @()
            $rule.expectedRuntimeTestCount = 0
            break
        }
    }
    Set-Content -LiteralPath $temporaryPolicyPath -Value ($policy | ConvertTo-Json -Depth 100) -NoNewline
    $policyCoverage = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-policy-coverage-contract' -Arguments @('-PolicyPath', $temporaryPolicyPath)
    Assert-Contract (-not $policyCoverage.Passed) 'A fast shard exclusion without a MAN-661 registered skip must fail shard governance.'
    Assert-Contract ($policyCoverage.Message.Contains('is not registered in the MAN-661 evidence policy as an environment-gated real-dependency skip', [StringComparison]::Ordinal)) 'Shard governance must reject an exclusion the evidence policy does not register.'

    $directorySelector = 'Nerv.IIP.Business.Inventory.Web.Tests.InventoryDirectoryPostgresTests'
    $directoryManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $directoryShard = @($directoryManifest.fastShards | Where-Object { [string]::Equals([string]([string] $_.id), [string]('business-core-a'), [StringComparison]::Ordinal) })
    Assert-Contract ($directoryShard.Count -eq 1) 'The Inventory directory PostgreSQL selector mutation must resolve business-core-a exactly once.'
    $directoryShard[0].excludedTestClasses = @($directoryShard[0].excludedTestClasses | Where-Object { -not [string]::Equals([string]([string] $_), $directorySelector, [StringComparison]::Ordinal) })
    Set-Content -LiteralPath $temporaryManifestPath -Value ($directoryManifest | ConvertTo-Json -Depth 100) -NoNewline
    $missingDirectorySelector = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-inventory-directory-selector-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
    $directoryFinding = "Real dependency test type '$directorySelector' uses the audited Docker CLI primitive but is not excluded from its fast shard."
    Assert-Contract (-not $missingDirectorySelector.Passed) 'Removing the Inventory directory PostgreSQL selector must fail shard governance.'
    Assert-Contract ($missingDirectorySelector.Message.Contains($directoryFinding, [StringComparison]::Ordinal)) 'Removing the Inventory directory PostgreSQL selector must report the complete direct Docker finding.'

    $wrongShardDirectoryManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $wrongShardDirectoryOwner = @($wrongShardDirectoryManifest.fastShards | Where-Object { [string]::Equals([string]([string] $_.id), [string]('business-core-a'), [StringComparison]::Ordinal) })
    $wrongShardDirectoryTarget = @($wrongShardDirectoryManifest.fastShards | Where-Object { [string]::Equals([string]([string] $_.id), [string]('platform'), [StringComparison]::Ordinal) })
    Assert-Contract ($wrongShardDirectoryOwner.Count -eq 1) 'The wrong-shard Inventory selector mutation must resolve business-core-a exactly once.'
    Assert-Contract ($wrongShardDirectoryTarget.Count -eq 1) 'The wrong-shard Inventory selector mutation must resolve platform exactly once.'
    $wrongShardDirectoryOwner[0].excludedTestClasses = @($wrongShardDirectoryOwner[0].excludedTestClasses | Where-Object { -not [string]::Equals([string]([string] $_), $directorySelector, [StringComparison]::Ordinal) })
    $wrongShardDirectoryTarget[0].excludedTestClasses = @(Get-NervStringsSorted -Values @(@($wrongShardDirectoryTarget[0].excludedTestClasses) + $directorySelector) -Comparer ([StringComparer]::Ordinal) -Unique)
    Set-Content -LiteralPath $temporaryManifestPath -Value ($wrongShardDirectoryManifest | ConvertTo-Json -Depth 100) -NoNewline
    $wrongShardDirectorySelector = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-inventory-directory-wrong-owner-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
    Assert-Contract (-not $wrongShardDirectorySelector.Passed) 'Relocating the Inventory directory PostgreSQL selector to a non-owning fast shard must fail shard governance.'
    Assert-Contract ($wrongShardDirectorySelector.Message.Contains($directoryFinding, [StringComparison]::Ordinal)) 'Relocating the Inventory directory PostgreSQL selector must report the complete direct Docker finding for its owning shard.'

    $directoryPolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    $directoryPolicy.rules = @($directoryPolicy.rules | Where-Object { -not [string]::Equals([string]([string] $_.id), [string]('inventory-directory-postgres'), [StringComparison]::Ordinal) })
    Set-Content -LiteralPath $temporaryPolicyPath -Value ($directoryPolicy | ConvertTo-Json -Depth 100) -NoNewline
    $missingDirectoryPolicy = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-inventory-directory-policy-contract' -Arguments @('-PolicyPath', $temporaryPolicyPath)
    Assert-Contract (-not $missingDirectoryPolicy.Passed) 'Removing the Inventory directory PostgreSQL policy rule must fail shard governance.'
    Assert-Contract ($missingDirectoryPolicy.Message.Contains("Fast shard exclusion '$directorySelector' is not registered in the MAN-661 evidence policy as an environment-gated real-dependency skip.", [StringComparison]::Ordinal)) 'Removing the Inventory directory PostgreSQL policy rule must report the unregistered environment-gated skip finding.'

    # The under-declaration has to be planted on whichever shard currently owns the Redis/CAP
    # exclusions; pinning that to a shard id made this negative
    # test silently pass the moment MAN-669 PR-A moved that exclusion to another shard.
    $laneManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $redisCapShards = @($laneManifest.fastShards | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@(@($_.excludedTestLanes | ForEach-Object { [string] $_ })), [StringComparer]::OrdinalIgnoreCase).Contains([string]('redis-cap')) })
    Assert-Contract ($redisCapShards.Count -eq 1) 'Exactly one fast shard must own the Redis/CAP exclusions for the lane-attribution contract to be able to under-declare it.'
    $redisCapShards[0].excludedTestLanes = @('real-postgres')
    Set-Content -LiteralPath $temporaryManifestPath -Value ($laneManifest | ConvertTo-Json -Depth 100) -NoNewline
    $laneAttribution = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-lane-attribution-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
    Assert-Contract (-not $laneAttribution.Passed) 'A shard that under-declares its excluded test lanes must fail shard governance.'
    Assert-Contract ($laneAttribution.Message.Contains('must declare excludedTestLanes [real-postgres, redis-cap]', [StringComparison]::Ordinal)) 'Shard governance must derive owner lanes from the MAN-661 requiredLane instead of trusting the declaration.'

    # MAN-669 PR-B: no shard may fall back to building the whole solution. backend/Nerv.IIP.sln is a
    # readable file and would otherwise be reported as a malformed solution filter rather than as
    # the thing it is, so the rejection has to be explicit — and therefore has to be tested.
    #
    # Every spelling below names the same file, and each one must land in the *whole-solution*
    # branch rather than in the downstream "invalid JSON" report — the misleading diagnostic that
    # branch exists to prevent. The first four were covered from the start; the last four are the
    # ones a hand-written `^\./` strip let through (#1494 review, 微瑕 1: "`backend//Nerv.IIP.sln`
    # 或绝对路径拼法会绕过新分支、落回「JSON 非法」误报"), and they are why the comparison now
    # canonicalizes with GetFullPath instead of trimming one prefix.
    #
    # Both halves are asserted per spelling: the run must fail, AND it must fail with the
    # whole-solution finding rather than with "invalid JSON" — a failure-only assertion would be
    # green for all eight even with the branch deleted, because every spelling fails either way.
    $solutionSpelling = [string] (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).solution
    foreach ($wholeSolutionSpelling in @(
            $solutionSpelling,
            "./$solutionSpelling",
            ($solutionSpelling -replace '/', '\'),
            $solutionSpelling.ToLowerInvariant(),
            ($solutionSpelling -replace '/', '//'),
            ($solutionSpelling -replace '/', '/./'),
            ("$(Split-Path -Parent $solutionSpelling)/../$solutionSpelling"),
            ((Join-Path $repoRoot $solutionSpelling) -replace '\\', '/')
        )) {
        $wholeSolutionManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $wholeSolutionManifest.fastShards[0].solutionFilter = $wholeSolutionSpelling
        Set-Content -LiteralPath $temporaryManifestPath -Value ($wholeSolutionManifest | ConvertTo-Json -Depth 100) -NoNewline
        $wholeSolution = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-whole-solution-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
        Assert-Contract (-not $wholeSolution.Passed) "A fast shard pointed at the whole backend solution ('$wholeSolutionSpelling') must fail shard governance."
        Assert-Contract ($wholeSolution.Message.Contains('must build its own solution filter, not the whole backend solution', [StringComparison]::Ordinal)) "Shard governance must reject a fast shard that rebuilds the entire backend solution, however '$wholeSolutionSpelling' is spelled."
        Assert-Contract (-not $wholeSolution.Message.Contains('solution filter is invalid JSON', [StringComparison]::Ordinal)) "'$wholeSolutionSpelling' must be diagnosed as the whole solution, not as a malformed solution filter."
    }

    $collisionSelector = 'Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed'
    $lastIndexTarget = "    `$collisionMethod = `$collisionSelector.Substring(`$collisionSelector.LastIndexOf('.', [StringComparison]::Ordinal) + 1)"
    $backendTestSource = [IO.File]::ReadAllText($PSCommandPath)
    Assert-Contract ([regex]::Matches($backendTestSource, [regex]::Escape($lastIndexTarget)).Count -eq 1) 'The selector suffix extraction must bind the explicit ordinal string overload exactly once.'
    $lastIndexProbeRoot = Join-Path ([IO.Path]::GetTempPath()) ("nerv-iip-last-index-ordinal-{0}" -f [Guid]::NewGuid().ToString('N'))
    try {
        [IO.Directory]::CreateDirectory($lastIndexProbeRoot) | Out-Null
        $lastIndexProbePath = Join-Path $lastIndexProbeRoot 'probe.ps1'
        [IO.File]::WriteAllText($lastIndexProbePath, $backendTestSource.Replace($lastIndexTarget, "    `$collisionMethod = `$collisionSelector.Substring(`$collisionSelector.LastIndexOf('.') + 1)"), [Text.UTF8Encoding]::new($false))
        $lastIndexMutationFindings = @((Get-NervOrdinalComparisonFindings -ScriptPath $lastIndexProbePath -DisplayName 'backend-test-shards-last-index-mutation.ps1').Findings)
        Assert-Contract ($lastIndexMutationFindings.Count -eq 1 -and $lastIndexMutationFindings[0].Contains('[string-method-without-ordinal-comparison]', [StringComparison]::Ordinal)) 'Removing the explicit ordinal comparer from the selector suffix extraction must make the ordinal scanner fail.'
    }
    finally {
        if (Test-Path -LiteralPath $lastIndexProbeRoot) { Remove-Item -LiteralPath $lastIndexProbeRoot -Recurse -Force }
    }
    $softHyphenLastIndex = [string][char]0x00AD
    $implicitStringLastIndex = [string].GetMethod('LastIndexOf', [Type[]]@([string]))
    Assert-Contract ($null -ne $implicitStringLastIndex) 'The focused culture probe must bind the one-argument string overload.'
    Assert-Contract ([int]$implicitStringLastIndex.Invoke('ab', [object[]]@($softHyphenLastIndex)) -eq 2) 'The implicit one-character string call is culture-sensitive and must not be treated as a char overload.'
    Assert-Contract (('ab').LastIndexOf($softHyphenLastIndex, [StringComparison]::Ordinal) -eq -1) 'The explicit ordinal string overload must not fold U+00AD into a non-existent suffix.'
    $collisionMethod = $collisionSelector.Substring($collisionSelector.LastIndexOf('.', [StringComparison]::Ordinal) + 1)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $temporaryCollisionSourcePath) | Out-Null
    Set-Content -LiteralPath $temporaryCollisionSourcePath -NoNewline -Value "public sealed class Fixture { public void $collisionMethod() { } public void ${collisionMethod}Extra() { } }"
    $collisionPolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    $collisionSourceIds = @($collisionPolicy.rules | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@(@($_.testIdentities)), [StringComparer]::Ordinal).Contains([string]($collisionSelector)) } | ForEach-Object { [string] $_.sourceId })
    foreach ($collisionSource in @($collisionPolicy.sources)) {
        if ([Collections.Generic.HashSet[string]]::new([string[]]@($collisionSourceIds), [StringComparer]::OrdinalIgnoreCase).Contains([string]([string] $collisionSource.id))) {
            $collisionSource.sourcePath = $temporaryCollisionRelativePath
        }
    }
    Set-Content -LiteralPath $temporaryPolicyPath -Value ($collisionPolicy | ConvertTo-Json -Depth 100) -NoNewline
    $collision = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-selector-collision-contract' -Arguments @('-PolicyPath', $temporaryPolicyPath)
    Assert-Contract (-not $collision.Passed) 'A method selector that substring-excludes a sibling member must fail shard governance.'
    Assert-Contract ($collision.Message.Contains('would also substring-exclude a sibling member', [StringComparison]::Ordinal)) 'Shard governance must reject a method selector that swallows a prefix-sharing sibling.'

    $timeoutText = ''
    $timedOut = $false
    $timeoutDiagnostics = ''
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-Command', '[Console]::Out.WriteLine("partial-diagnostic Password=super-secret"); [Console]::Out.Flush(); Start-Sleep -Seconds 3') -WorkingDirectory $repoRoot -TimeoutSeconds 1 -Name 'backend-test-shard-timeout-contract' | Out-Null
    }
    catch {
        $timedOut = $true
        $timeoutText = $_.Exception.Message
        $timeoutDiagnostics = Get-BackendTestShardFailureDiagnostics -ErrorRecord $_ -TrxFilePrefix 'timeout-contract'
    }
    Assert-Contract ($timedOut -and -not [string]::IsNullOrWhiteSpace($timeoutText)) 'The bounded timeout diagnostic helper contract must time out.'
    Assert-Contract ($timeoutDiagnostics.Contains('partial-diagnostic', [StringComparison]::Ordinal)) 'The bounded timeout diagnostic helper contract must preserve buffered stdout content.'
    Assert-Contract (-not $timeoutDiagnostics.Contains('super-secret', [StringComparison]::Ordinal)) 'Buffered shard diagnostics must be redacted before they reach any retained log.'
    Assert-Contract (-not (Test-Path -LiteralPath $timeoutResultsDirectory)) 'Buffered shard diagnostics must stay in the job log instead of an uploaded results directory.'
}
finally {
    Remove-Item -LiteralPath $temporaryBackendInventory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryWorkflowPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $timeoutResultsDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $executionTrxDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryPolicyPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryManifestPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryCollisionSourcePath -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------------------------
# #1507 — timing is a report-only cache keyed by assembly; policy keys carry no lane/shard dimension.
#
# The failure being regression-tested is concrete: MAN-663 changed the shared host and MAN-669 PR-A
# re-homed assemblies between shards. Neither touched a test, yet both invalidated keys in a
# committed timing snapshot, and clearing that required a human to regenerate and re-commit it. The
# assertions below fix both halves of the cause — the measurement is no longer keyed on topology,
# and a gap in it can no longer turn anything red.
# ---------------------------------------------------------------------------------------------
. (Join-Path $repoRoot 'scripts/lib/BackendTestShardTimings.ps1')

$balanceScript = Join-Path $repoRoot 'scripts/report-backend-test-shard-balance.ps1'
$timingUpdateScript = Join-Path $repoRoot 'scripts/update-backend-test-shard-timings.ps1'
Assert-Contract (Test-Path -LiteralPath $balanceScript -PathType Leaf) 'The report-only shard balance entry point is missing.'
Assert-Contract (Test-Path -LiteralPath $timingUpdateScript -PathType Leaf) 'The shard timing cache refresher is missing.'

# The policy gate and the timing report are deliberately separate programs, and this asserts the
# dependency direction between the two files. It is deliberately an **AST** judgement rather than a
# `Contains()` over the raw source: the previous spelling scanned raw text, so writing the words
# `test-evidence-baseline.json` in a *comment* inside the validator — explaining why it does not
# read that file, which is exactly the comment someone would write — turned this contract red over
# a sentence. Comments are not dependencies. What the AST checks instead is what a dependency
# actually looks like in PowerShell: dot-sourcing the timing library, calling one of the functions
# it defines, or naming a timing file in a string literal the script can act on.
#
# The behavioural half — a gap in timing data still exits 0 — is asserted below.
$validatorAst = [System.Management.Automation.Language.Parser]::ParseFile($validatorPath, [ref] $null, [ref] $null)
$timingLibraryPath = Join-Path $repoRoot 'scripts/lib/BackendTestShardTimings.ps1'
$timingLibraryAst = [System.Management.Automation.Language.Parser]::ParseFile($timingLibraryPath, [ref] $null, [ref] $null)
$timingFunctionNames = @(
    Get-NervStringsSorted -Values @($timingLibraryAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) |
        ForEach-Object { [string] $_.Name }) -Comparer ([StringComparer]::Ordinal) -Unique
)
Assert-Contract ($timingFunctionNames.Count -gt 0) 'The timing library must define functions for the dependency-boundary assertion to have anything to look for.'
# Ordinal set membership, for the same reason the policy identity comparison is ordinal: a function
# name is an identifier, and PowerShell's `-ccontains` is culture-aware, not ordinal.
$timingFunctionNameSet = [System.Collections.Generic.HashSet[string]]::new([string[]] $timingFunctionNames, [StringComparer]::Ordinal)

# The scan covers the gate **and every repository library it dot-sources**. Scanning only the entry
# point leaves the hole open exactly one hop down: a call to a timing function placed inside
# BackendTestShardSelectors.ps1 is just as much a dependency of the gate, and the entry point's AST
# cannot see it. The library set is derived from the gate's own dot-source statements rather than
# listed here, so a new library joins the scan by being dot-sourced.
$repoLibraries = @(Get-NervItemsSortedByString -Items @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts/lib') -Filter '*.ps1' -File) -KeySelector { param($row) [string]$row.FullName } -Comparer ([StringComparer]::Ordinal))
$boundaryScanPaths = [System.Collections.Generic.List[string]]::new()
[void] $boundaryScanPaths.Add([string] $validatorPath)
foreach ($command in @($validatorAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true))) {
    if ($command.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Dot) { continue }
    $dotSourceText = ($command.Extent.Text -replace '\\', '/')
    foreach ($library in $repoLibraries) {
        if ($dotSourceText.Contains([string] $library.BaseName)) { [void] $boundaryScanPaths.Add([string] $library.FullName) }
    }
}
$boundaryScanPaths = @(Get-NervStringsSorted -Values @($boundaryScanPaths) -Comparer ([StringComparer]::Ordinal) -Unique)
Assert-Contract (@($boundaryScanPaths).Count -gt 1) 'The dependency-boundary scan must reach at least one library the gate dot-sources; otherwise it silently degraded to scanning the entry point alone.'

foreach ($scanPath in $boundaryScanPaths) {
    $scanName = [System.IO.Path]::GetFileName($scanPath)
    $scanAst = [System.Management.Automation.Language.Parser]::ParseFile($scanPath, [ref] $null, [ref] $null)
    foreach ($command in @($scanAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true))) {
        $commandName = [string] $command.GetCommandName()
        Assert-Contract (-not $timingFunctionNameSet.Contains($commandName)) "The shard policy hard gate must not call the timing library function '$commandName' (found in $scanName); timing lives in the report-only balance script."
        # A dot-source is a CommandAst whose invocation operator is `.`; its single argument is the path.
        if ($command.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Dot) { continue }
        $dotSourced = ($command.Extent.Text -replace '\\', '/')
        Assert-Contract (-not $dotSourced.Contains('BackendTestShardTimings', [StringComparison]::Ordinal)) "The shard policy hard gate must not dot-source the timing library (found in $scanName); timing lives in the report-only balance script."
    }

    # String literals only — the parser hands back the *value* of a literal and never the text of a
    # comment, so this is precise where the old raw-text scan was not.
    $scanLiterals = @(
        $scanAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
            $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]
        }, $true) | ForEach-Object { [string] $_.Extent.Text }
    )
    foreach ($timingToken in @('test-evidence-baseline.json', 'backend-test-shard-timings', 'elapsedMilliseconds')) {
        $offending = @($scanLiterals | Where-Object { $_.Contains($timingToken) })
        Assert-Contract ($offending.Count -eq 0) "The shard policy hard gate must not name timing data ('$timingToken') in an evaluated string (found in $scanName); timing lives in the report-only balance script."
    }
}

$timingFixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-shard-timing-fixture-{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $timingFixtureRoot -Force | Out-Null
    $absentFallback = Join-Path $timingFixtureRoot 'no-such-snapshot.json'
    $snapshot = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-baseline.json') -Raw | ConvertFrom-Json
    $allObservations = @(
        foreach ($row in @(Get-NervShardTimingRowsFromEvidenceSummary -Summary $snapshot)) {
            [pscustomobject]@{ runId = 'fixture-run-1'; assembly = [string] $row.assembly; lane = [string] $row.lane; elapsedMilliseconds = [double] $row.elapsedMilliseconds }
        }
    )
    Assert-Contract ($allObservations.Count -gt 0) 'The timing fixture needs at least one observation to be meaningful.'

    # (a) Remove one assembly's timing data. The balance must degrade to a named report-only warning
    #     plus an estimate and exit 0 — never red.
    $businessCoreB = @($fastShards | Where-Object { [string]::Equals([string] $_.id, 'business-core-b', [StringComparison]::Ordinal) })[0]
    $droppedAssembly = Get-NervShardTimingAssemblyKey -Name ([string] @($businessCoreB.projects)[0])
    $reducedObservations = @($allObservations | Where-Object { -not [string]::Equals([string] $_.assembly, $droppedAssembly, [StringComparison]::Ordinal) })
    Assert-Contract ($reducedObservations.Count -lt $allObservations.Count) "The missing-timing fixture must actually remove '$droppedAssembly'."
    $reducedCachePath = Join-Path $timingFixtureRoot 'reduced-timings.json'
    Set-Content -LiteralPath $reducedCachePath -NoNewline -Value (
        (New-NervShardTimingCache -Observations $reducedObservations -Runs @([pscustomobject]@{ workflowRunId = 'fixture-run-1' })) | ConvertTo-Json -Depth 20
    )
    $reducedBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-missing-assembly-timing' -Arguments @(
        '-TimingCachePath', $reducedCachePath, '-FallbackEvidencePath', $absentFallback, '-NoRefresh'
    )
    Assert-Contract ($reducedBalance.Passed) 'A shard assembly with no timing observation must stay report-only and exit 0.'
    Assert-Contract ($reducedBalance.Message.Contains('timing-assembly-missing', [StringComparison]::Ordinal)) 'Missing timing data must be reported with its structured warning code.'
    Assert-Contract ($reducedBalance.Message.Contains($droppedAssembly)) 'The missing-timing warning must name the assembly it estimated.'
    Assert-Contract ($reducedBalance.Message.Contains('report-only', [StringComparison]::Ordinal)) 'The missing-timing warning must say it is report-only.'

    # No timing data at all — the offline / no-token / expired-artifact path — is also report-only.
    $emptyCachePath = Join-Path $timingFixtureRoot 'empty-timings.json'
    Set-Content -LiteralPath $emptyCachePath -NoNewline -Value (
        (New-NervShardTimingCache -Observations @() -Runs @()) | ConvertTo-Json -Depth 20
    )
    $emptyBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-no-timing-source' -Arguments @(
        '-TimingCachePath', $emptyCachePath, '-FallbackEvidencePath', $absentFallback, '-NoRefresh'
    )
    Assert-Contract ($emptyBalance.Passed) 'A completely unavailable timing source must still exit 0.'
    Assert-Contract ($emptyBalance.Message.Contains('timing-source-unavailable', [StringComparison]::Ordinal)) 'A completely unavailable timing source must be named, not silently estimated.'

    # The committed snapshot is the offline fallback. What is asserted here is that the fallback
    # *source* is selected and that the report it produces is structurally complete — one priced row
    # per fast shard plus the spread line.
    #
    # What is deliberately NOT asserted is that the snapshot covers every classified assembly. That
    # assertion existed, and it was the deleted red gate growing back in a wider form: the snapshot
    # is a committed file, so any *new backend test project* — a change that touches no timing code
    # and breaks nothing — has no row in it and produced a `timing-assembly-missing` warning, which
    # this contract then turned into a red Backend Test Shard Governance job until a human
    # regenerated and re-committed the snapshot. That is the exact human refresh ceremony #1507
    # deleted, re-imposed by a test, over a warning whose own text says "This is report-only".
    # docs/architecture/test-evidence-governance.md states the same rule in prose: coverage gaps are
    # report-only warnings, and the committed snapshot is never required to be complete.
    #
    # The gap count is printed instead of asserted, so a human reading the job log can see the
    # coverage drift that is worth knowing about and worthless as a gate.
    $fallbackBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-committed-fallback' -Arguments @(
        '-TimingCachePath', (Join-Path $timingFixtureRoot 'no-such-cache.json'),
        '-FallbackEvidencePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json'),
        '-NoRefresh'
    )
    Assert-Contract ($fallbackBalance.Passed) 'The balance report must fall back to the committed snapshot without failing.'
    Assert-Contract ($fallbackBalance.Message.Contains('committed-evidence-snapshot', [StringComparison]::Ordinal)) 'The balance report must name the fallback timing source it used.'
    foreach ($shard in $fastShards) {
        $shardRowPattern = "$([regex]::Escape([string] $shard.id)) [0-9,]+ ms over [0-9]+ assemblies \([0-9]+ measured, [0-9]+ estimated\) \[$([regex]::Escape([string] $shard.evidenceLane))\]"
        Assert-Contract ($fallbackBalance.Message -cmatch $shardRowPattern) "The committed-snapshot fallback report must price fast shard '$($shard.id)' with a measured/estimated split."
    }
    Assert-Contract ($fallbackBalance.Message -cmatch 'spread \(max-min\)/mean: [0-9.]+%') 'The committed-snapshot fallback report must still report the spread it was asked for.'
    $fallbackCoverageGaps = @([regex]::Matches($fallbackBalance.Message, 'timing-assembly-missing')).Count
    Write-Host "  [report-only] committed-snapshot fallback coverage gaps: $fallbackCoverageGaps"

    # The positive form of the same rule, which is what the deleted assertion should have been: a
    # backend test project the committed snapshot has never seen must be *balanced and reported*,
    # not punished. Nothing needs to exist on disk — the balance report prices whatever the manifest
    # classifies — so this is the cheapest possible stand-in for "someone added a test project".
    $newProjectManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $newProjectRelativePath = 'backend/tests/Nerv.IIP.BrandNew.Tests/Nerv.IIP.BrandNew.Tests.csproj'
    $newProjectAssembly = Get-NervShardTimingAssemblyKey -Name $newProjectRelativePath
    $newProjectShard = @($newProjectManifest.fastShards | Where-Object { [string]::Equals([string] $_.id, 'business-gateway', [StringComparison]::Ordinal) })[0]
    $newProjectShard.projects = @(@($newProjectShard.projects) + @($newProjectRelativePath))
    $newProjectManifestPath = Join-Path $timingFixtureRoot 'manifest-with-new-project.json'
    Set-Content -LiteralPath $newProjectManifestPath -NoNewline -Value ($newProjectManifest | ConvertTo-Json -Depth 100)
    $newProjectBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-new-test-project' -Arguments @(
        '-ManifestPath', $newProjectManifestPath,
        '-TimingCachePath', (Join-Path $timingFixtureRoot 'no-such-cache.json'),
        '-FallbackEvidencePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json'),
        '-NoRefresh'
    )
    Assert-Contract ($newProjectBalance.Passed) 'Adding a backend test project must not turn the shard balance report red; a coverage gap is report-only by construction.'
    Assert-Contract ($newProjectBalance.Message.Contains($newProjectAssembly)) 'A backend test project with no measurement must be named in a report-only warning rather than silently estimated.'
    Assert-Contract ($newProjectBalance.Message.Contains('This is report-only.', [StringComparison]::Ordinal)) 'The coverage-gap warning must say it is report-only.'

    # The aggregation口径 itself, offline: two runs' extracted evidence bundles in, one median per
    # assembly out. Also pins the two rules that are easy to get silently wrong — a bundle whose
    # collection failed carries diagnostics rather than measurements and must not become a sample,
    # and an assembly observed in two lanes of the *same* run is one sample of the summed work, not
    # two samples of half of it.
    $evidenceFixture = Join-Path $timingFixtureRoot 'evidence'
    $runOne = Join-Path $evidenceFixture 'run-1'
    $runTwo = Join-Path $evidenceFixture 'run-2'
    New-Item -ItemType Directory -Path (Join-Path $runOne 'lane-a') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runOne 'lane-b') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runOne 'lane-failed') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runTwo 'lane-a') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $runOne 'lane-a/summary.json') -NoNewline -Value (@{
        collectionStatus = 'succeeded'; lane = 'backend-shard-1'
        assemblies = @(
            @{ lane = 'backend-shard-1'; assembly = 'Split.Tests.dll'; elapsedMilliseconds = 40 },
            @{ lane = 'backend-shard-1'; assembly = 'Solo.Tests.dll'; elapsedMilliseconds = 1000 }
        )
    } | ConvertTo-Json -Depth 10)
    Set-Content -LiteralPath (Join-Path $runOne 'lane-b/summary.json') -NoNewline -Value (@{
        collectionStatus = 'succeeded'; lane = 'backend-shard-2'
        assemblies = @(@{ lane = 'backend-shard-2'; assembly = 'Split.Tests.dll'; elapsedMilliseconds = 60 })
    } | ConvertTo-Json -Depth 10)
    Set-Content -LiteralPath (Join-Path $runOne 'lane-failed/summary.json') -NoNewline -Value (@{
        collectionStatus = 'failed'; lane = 'backend-shard-3'
        assemblies = @(@{ lane = 'backend-shard-3'; assembly = 'Solo.Tests.dll'; elapsedMilliseconds = 999999 })
    } | ConvertTo-Json -Depth 10)
    Set-Content -LiteralPath (Join-Path $runTwo 'lane-a/summary.json') -NoNewline -Value (@{
        collectionStatus = 'succeeded'; lane = 'backend-shard-1'
        assemblies = @(
            @{ lane = 'backend-shard-1'; assembly = 'Split.Tests.dll'; elapsedMilliseconds = 200 },
            @{ lane = 'backend-shard-1'; assembly = 'Solo.Tests.dll'; elapsedMilliseconds = 3000 }
        )
    } | ConvertTo-Json -Depth 10)

    $aggregated = @(Merge-NervShardTimingObservations -Observations @(
        @(Get-NervShardTimingObservationsFromEvidenceDirectory -Path $runOne -RunId 'run-1') +
        @(Get-NervShardTimingObservationsFromEvidenceDirectory -Path $runTwo -RunId 'run-2')
    ))
    $splitRow = @($aggregated | Where-Object { [string]::Equals([string] $_.assembly, 'split.tests.dll', [StringComparison]::Ordinal) })
    $soloRow = @($aggregated | Where-Object { [string]::Equals([string] $_.assembly, 'solo.tests.dll', [StringComparison]::Ordinal) })
    Assert-Contract ($splitRow.Count -eq 1 -and $soloRow.Count -eq 1) 'Aggregation must produce exactly one row per assembly across runs.'
    Assert-Contract ((([double] $splitRow[0].elapsedMilliseconds) -eq (150.0))) "An assembly split across two lanes of one run must be summed first, then medianed; got $($splitRow[0].elapsedMilliseconds)."
    Assert-Contract ((([int] $splitRow[0].observationCount) -eq (2))) 'Two lanes of one run must count as one observation, not two.'
    Assert-Contract ((([double] $soloRow[0].elapsedMilliseconds) -eq (2000.0))) "Two runs must produce the median of the two values; got $($soloRow[0].elapsedMilliseconds)."
    Assert-Contract ((([int] $soloRow[0].observationCount) -eq (2))) 'A failed-collection bundle must not become a third observation.'

    # (b) Simulate a shard rearrangement and prove the keys survive it.
    #
    # The fixture deliberately moves a project that *owns exclusions*, and moves its exclusion
    # selectors and derived `excludedTestLanes` with it. A rearrangement that only shuffles project
    # paths never reaches the one place in the policy gate where a shard id and a MAN-661 lane meet
    # — the `excludedTestLanes` derivation in verify-backend-test-shards.ps1 — so it could not
    # distinguish a lane-free policy key from a lane-coupled one, which is the entire claim under
    # test. Which project that is stays *derived* rather than pinned: pinning it to a shard id is
    # how the neighbouring lane-attribution fixture once went silently vacuous when MAN-669 PR-A
    # moved an exclusion to another shard.
    $evidencePolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    $policyLaneToHeavyLane = @{}
    foreach ($heavyLane in @($manifest.heavyLanes)) { $policyLaneToHeavyLane[[string] $heavyLane.policyLane] = [string] $heavyLane.id }

    function Get-ShardDerivedExcludedTestLanes {
        # The production derivation, restated only as a fixture helper: the heavy lanes a shard's
        # exclusions require. verify-backend-test-shards.ps1 computes the same thing and compares it
        # to what the shard declares, which is what makes a fixture that forgets to move
        # excludedTestLanes fail — see the negative control below.
        param(
            [Parameter(Mandatory)] [object] $Shard,
            [Parameter(Mandatory)] [object] $EvidencePolicy,
            [Parameter(Mandatory)] [hashtable] $PolicyLaneToHeavyLane
        )

        $lanes = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $Shard)) {
            foreach ($match in @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($EvidencePolicy.rules))) {
                $policyLane = [string] $match.requiredLane
                if ($PolicyLaneToHeavyLane.ContainsKey($policyLane)) { [void] $lanes.Add([string] $PolicyLaneToHeavyLane[$policyLane]) }
            }
        }
        return @(Get-NervStringsSorted -Values @($lanes) -Comparer ([StringComparer]::Ordinal))
    }

    $rearranged = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    # Donor: the fast shard that owns the one exclusion whose required lane is not the PostgreSQL
    # one, so the move actually changes both shards' derived excludedTestLanes rather than leaving
    # them identical by coincidence.
    $donorCandidates = @(
        foreach ($shard in @($rearranged.fastShards)) {
            $shardLanes = @(Get-ShardDerivedExcludedTestLanes -Shard $shard -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane)
            if (@($shardLanes).Count -gt 1) { $shard }
        }
    )
    Assert-Contract ($donorCandidates.Count -eq 1) 'Exactly one fast shard must own exclusions from more than one heavy lane, otherwise the rearrangement fixture cannot move a lane between shards.'
    $donor = $donorCandidates[0]
    $receiver = @($rearranged.fastShards | Where-Object { -not [string]::Equals([string] $_.id, [string] $donor.id, [StringComparison]::Ordinal) })[0]

    # The moved project is the donor project whose assembly owns the multi-lane exclusion.
    $donorExtraLane = @(@(Get-ShardDerivedExcludedTestLanes -Shard $donor -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane) | Where-Object { -not [string]::Equals($_, 'real-postgres', [StringComparison]::Ordinal) })[0]
    $donorExtraLaneSelectors = Get-NervStringsSorted -Values @(@(
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $donor)) {
            foreach ($match in @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($evidencePolicy.rules))) {
                if ([string]::Equals([string] $policyLaneToHeavyLane[[string] $match.requiredLane], $donorExtraLane, [StringComparison]::Ordinal)) { $selector }
            }
        }
    )) -Comparer ([StringComparer]::Ordinal) -Unique
    Assert-Contract (@($donorExtraLaneSelectors).Count -gt 0) "The rearrangement fixture must find the donor selectors that require heavy lane '$donorExtraLane'."
    $movedProjects = Get-NervStringsSorted -Values @(@(
        foreach ($project in @($donor.projects)) {
            $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension([string] $project)
            foreach ($selector in @($donorExtraLaneSelectors)) {
                if (([string] $selector).StartsWith("$assemblyName.", [StringComparison]::Ordinal)) { [string] $project }
            }
        }
    )) -Comparer ([StringComparer]::Ordinal) -Unique
    Assert-Contract (@($movedProjects).Count -eq 1) "The rearrangement fixture must resolve heavy lane '$donorExtraLane' to exactly one donor project; got $(@($movedProjects).Count)."
    $movedProject = [string] @($movedProjects)[0]
    $movedAssemblyName = [System.IO.Path]::GetFileNameWithoutExtension($movedProject)
    $movedSelectors = @(Get-BackendTestShardExcludedSelectors -Shard $donor | Where-Object { ([string] $_).StartsWith("$movedAssemblyName.", [StringComparison]::Ordinal) })
    Assert-Contract (@($movedSelectors).Count -gt 0) 'The moved project must carry at least one exclusion selector, otherwise the policy half of this fixture is inert.'

    function Move-ShardExclusionSelectors {
        # Splits one shard's excludedTestClasses/excludedTests into "stays" and "moves", by the
        # assembly the selector belongs to. Both lists are optional properties, so both directions
        # have to tolerate an absent property rather than assume an empty array.
        param(
            [Parameter(Mandatory)] [object] $FromShard,
            [Parameter(Mandatory)] [object] $ToShard,
            [Parameter(Mandatory)] [string] $AssemblyName
        )

        foreach ($propertyName in @('excludedTestClasses', 'excludedTests')) {
            $fromProperty = $FromShard.PSObject.Properties[$propertyName]
            if ($null -eq $fromProperty) { continue }
            $all = @(@($fromProperty.Value) | ForEach-Object { [string] $_ })
            $moving = @($all | Where-Object { $_.StartsWith("$AssemblyName.", [StringComparison]::Ordinal) })
            if ($moving.Count -eq 0) { continue }
            $fromProperty.Value = @($all | Where-Object { -not $_.StartsWith("$AssemblyName.", [StringComparison]::Ordinal) })
            $toProperty = $ToShard.PSObject.Properties[$propertyName]
            if ($null -eq $toProperty) {
                $ToShard | Add-Member -NotePropertyName $propertyName -NotePropertyValue (Get-NervStringsSorted -Values @(@($moving)) -Comparer ([StringComparer]::Ordinal) -Unique) -Force
            }
            else {
                $toProperty.Value = Get-NervStringsSorted -Values @(@(@(@($toProperty.Value) | ForEach-Object { [string] $_ }) + $moving)) -Comparer ([StringComparer]::Ordinal) -Unique
            }
        }
    }

    $donor.projects = @(@($donor.projects) | Where-Object { -not [string]::Equals([string] $_, $movedProject, [StringComparison]::Ordinal) })
    $receiver.projects = Get-NervStringsSorted -Values @(@(@($receiver.projects) + @($movedProject))) -Comparer ([StringComparer]::Ordinal) -Unique
    Move-ShardExclusionSelectors -FromShard $donor -ToShard $receiver -AssemblyName $movedAssemblyName
    $donor.excludedTestLanes = @(Get-ShardDerivedExcludedTestLanes -Shard $donor -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane)
    $receiver.excludedTestLanes = @(Get-ShardDerivedExcludedTestLanes -Shard $receiver -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane)
    Assert-Contract (@($receiver.projects) -contains $movedProject -and -not (@($donor.projects) -contains $movedProject)) 'The rearrangement fixture must actually move a project between shards.'
    Assert-Contract ((@($receiver.excludedTestLanes) -contains $donorExtraLane) -and -not (@($donor.excludedTestLanes) -contains $donorExtraLane)) "The rearrangement fixture must move heavy lane '$donorExtraLane' with the project that requires it, otherwise it never reaches the excludedTestLanes coupling point."

    $fullTimings = Get-NervShardTimingLookup -CachePath (Join-Path $timingFixtureRoot 'no-such-cache.json') -FallbackEvidencePath (Join-Path $repoRoot 'scripts/test-evidence-baseline.json')

    # What a rearrangement must not do is **lose a key**: an assembly must resolve to the same timing
    # key, and that key to the same measurement, whichever shard happens to hold it.
    #
    # That is a different statement from "every assembly has a measurement". A *coverage gap*
    # (`timing-assembly-missing`) means the source never observed that assembly at all — which is
    # exactly what adding a backend test project produces, and exactly what the balance report is
    # allowed to estimate over. Asserting zero gaps here is the deleted red gate wearing another
    # costume: it would turn "someone added a test project" into a red Backend Test Shard Governance
    # job until a human regenerated and re-committed the snapshot, which is the #1507 ceremony. The
    # same rule is stated in prose in docs/architecture/test-evidence-governance.md.
    #
    # So the gap count is printed for a human reading the job log, and only key stability is asserted.
    $keyResolutionByLayout = [ordered]@{}
    foreach ($case in @(
            @{ Name = 'original'; Manifest = ($manifest) },
            @{ Name = 'rearranged'; Manifest = $rearranged }
        )) {
        $report = Get-NervShardBalanceReport -Manifest $case.Manifest -Timings $fullTimings
        $coverageGaps = @($report.warnings | Where-Object { [string]::Equals([string] $_.code, 'timing-assembly-missing', [StringComparison]::Ordinal) })
        Write-Host "  [report-only] shard layout '$($case.Name)' timing coverage gaps: $($coverageGaps.Count)"

        $resolution = [ordered]@{}
        foreach ($shard in @($case.Manifest.fastShards)) {
            foreach ($project in @($shard.projects)) {
                $timingKey = Get-NervShardTimingAssemblyKey -Name ([string] $project)
                $resolution[$timingKey] = if ($fullTimings.rows.ContainsKey($timingKey)) {
                    ([double] $fullTimings.rows[$timingKey]).ToString([System.Globalization.CultureInfo]::InvariantCulture)
                }
                else { '<no-observation>' }
            }
        }
        $keyResolutionByLayout[[string] $case.Name] = $resolution
    }

    $originalResolution = $keyResolutionByLayout['original']
    $rearrangedResolution = $keyResolutionByLayout['rearranged']
    $originalKeyText = (Get-NervStringsSorted -Values @(@($originalResolution.Keys)) -Comparer ([StringComparer]::Ordinal)) -join "`n"
    $rearrangedKeyText = (Get-NervStringsSorted -Values @(@($rearrangedResolution.Keys)) -Comparer ([StringComparer]::Ordinal)) -join "`n"
    Assert-Contract (@($originalResolution.Keys).Count -gt 0) 'The timing-key stability check must resolve at least one key, otherwise it is vacuous.'
    Assert-Contract ([string]::Equals($originalKeyText, $rearrangedKeyText, [StringComparison]::Ordinal)) 'A shard rearrangement must not change the set of timing keys the layout resolves to.'
    foreach ($timingKey in @($originalResolution.Keys)) {
        Assert-Contract ([string]::Equals([string] $originalResolution[$timingKey], [string] $rearrangedResolution[$timingKey], [StringComparison]::Ordinal)) "Timing key '$timingKey' must resolve to the same measurement before and after the rearrangement; got '$($originalResolution[$timingKey])' vs '$($rearrangedResolution[$timingKey])'."
    }

    # The non-trivial half of the same claim, and the reason the loop above is not just "the same
    # project set produces the same keys": the moved assembly's measurement must move *with it*,
    # exactly. Run against a synthetic lookup that prices every classified assembly distinctly, so
    # this can never depend on how complete the committed snapshot happens to be (a gap would make
    # the shard totals include estimates and the shift inexact — i.e. it would re-couple this
    # assertion to snapshot coverage, which is the thing being removed). A lookup reduced to a no-op,
    # to a constant, or one that dropped the moved row cannot produce the exact expected shift.
    $syntheticRows = @{}
    $syntheticPrice = 1000.0
    foreach ($shard in @($manifest.fastShards)) {
        foreach ($project in @($shard.projects)) {
            $syntheticRows[(Get-NervShardTimingAssemblyKey -Name ([string] $project))] = $syntheticPrice
            $syntheticPrice += 7.0
        }
    }
    $syntheticTimings = [pscustomobject][ordered]@{ source = 'synthetic-fixture'; sourceDetail = ''; generatedAtUtc = $null; rows = $syntheticRows }
    $movedAssemblyPrice = [double] $syntheticRows[(Get-NervShardTimingAssemblyKey -Name $movedProject)]
    $syntheticBefore = Get-NervShardBalanceReport -Manifest $manifest -Timings $syntheticTimings
    $syntheticAfter = Get-NervShardBalanceReport -Manifest $rearranged -Timings $syntheticTimings
    Assert-Contract (@($syntheticBefore.warnings).Count -eq 0 -and @($syntheticAfter.warnings).Count -eq 0) 'The synthetic attribution fixture must price every classified assembly, otherwise an estimate would make the expected shift inexact.'
    foreach ($shardId in @(@($syntheticBefore.shards | ForEach-Object { [string] $_.id }))) {
        $beforeTotal = [double] @($syntheticBefore.shards | Where-Object { [string]::Equals([string] $_.id, $shardId, [StringComparison]::Ordinal) })[0].totalMilliseconds
        $afterTotal = [double] @($syntheticAfter.shards | Where-Object { [string]::Equals([string] $_.id, $shardId, [StringComparison]::Ordinal) })[0].totalMilliseconds
        $expectedDelta = if ([string]::Equals($shardId, [string] $donor.id, [StringComparison]::Ordinal)) { - $movedAssemblyPrice }
            elseif ([string]::Equals($shardId, [string] $receiver.id, [StringComparison]::Ordinal)) { $movedAssemblyPrice }
            else { 0.0 }
        Assert-Contract ([Math]::Abs(($afterTotal - $beforeTotal) - $expectedDelta) -lt 0.05) "Shard '$shardId' must change by exactly the moved assembly's measurement across the rearrangement; expected $expectedDelta ms, got $($afterTotal - $beforeTotal) ms."
    }

    # Control: the *old* lane+assembly key would have lost keys on exactly this rearrangement. Without
    # this the assertion above would still pass if timing lookup were reduced to a no-op, and it is
    # what goes red if anyone puts the lane back into the key.
    $laneKeyedSnapshot = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($row in @($snapshot.assemblies)) {
        [void] $laneKeyedSnapshot.Add("$([string] $row.lane)|$(Get-NervShardTimingAssemblyKey -Name ([string] $row.assembly))")
    }
    $laneKeyedLost = @(
        foreach ($shard in @($rearranged.fastShards)) {
            foreach ($project in @($shard.projects)) {
                $laneKey = "$([string] $shard.evidenceLane)|$(Get-NervShardTimingAssemblyKey -Name ([string] $project))"
                if (-not $laneKeyedSnapshot.Contains($laneKey)) { $laneKey }
            }
        }
    )
    Assert-Contract ($laneKeyedLost.Count -gt 0) 'The rearrangement fixture must be one that the old lane+assembly key would have failed on, otherwise the assembly-keyed assertion is vacuous.'

    # Policy keys must be identical across the rearrangement. This is the "政策门禁零失键" acceptance:
    # every fast-shard exclusion still resolves to exactly the same MAN-661 source/rule/test
    # identities. The key derivation is the **production** one — Get-BackendTestShardPolicyIdentity*
    # from scripts/lib/BackendTestShardSelectors.ps1, the same pair verify-backend-test-shards.ps1
    # runs. A key set rebuilt inside this file would have asserted its own arithmetic and stayed
    # green even with the lane put back into the production key.
    function Get-ShardPolicyKeySet {
        param(
            [Parameter(Mandatory)] [object] $ShardManifest,
            [Parameter(Mandatory)] [object] $EvidencePolicy,
            # Control switch. When set, the shard's evidence lane is spliced into each key — i.e.
            # what the key would look like if policy were coupled to the shard topology the way
            # timing used to be. Nothing in production takes this path; it exists so the assertion
            # below has something that demonstrably *does* break.
            [switch] $KeyOnLane
        )

        $keys = [System.Collections.Generic.List[string]]::new()
        foreach ($shard in @($ShardManifest.fastShards)) {
            foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
                foreach ($match in @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($EvidencePolicy.rules))) {
                    $key = Get-BackendTestShardPolicyIdentityKey -Match $match
                    if ($KeyOnLane) { $key = "$([string] $shard.evidenceLane)|$key" }
                    [void] $keys.Add($key)
                }
            }
        }

        return @(Get-NervStringsSorted -Values @($keys) -Comparer ([StringComparer]::Ordinal) -Unique)
    }

    $policyKeysBefore = @(Get-ShardPolicyKeySet -ShardManifest $manifest -EvidencePolicy $evidencePolicy)
    $policyKeysAfter = @(Get-ShardPolicyKeySet -ShardManifest $rearranged -EvidencePolicy $evidencePolicy)
    Assert-Contract ($policyKeysBefore.Count -gt 0) 'The policy key set must be non-empty, otherwise its stability is vacuous.'
    Assert-Contract ([string]::Equals(($policyKeysBefore -join "`n"), ($policyKeysAfter -join "`n"), [StringComparison]::Ordinal)) 'A shard rearrangement must not change a single MAN-661 policy key.'

    # Control, and the reason the assertion above is not a tautology: run the *same* derivation with
    # the lane spliced back into the key and the same rearrangement does lose keys. Without this, a
    # key set that happened to be topology-invariant for an unrelated reason — or a rearrangement
    # too weak to move anything a key could see — would read as a passing contract.
    $laneKeyedPolicyBefore = @(Get-ShardPolicyKeySet -ShardManifest $manifest -EvidencePolicy $evidencePolicy -KeyOnLane)
    $laneKeyedPolicyAfter = [System.Collections.Generic.HashSet[string]]::new([string[]] @(Get-ShardPolicyKeySet -ShardManifest $rearranged -EvidencePolicy $evidencePolicy -KeyOnLane))
    $laneKeyedPolicyLost = @($laneKeyedPolicyBefore | Where-Object { -not $laneKeyedPolicyAfter.Contains([string] $_) })
    Assert-Contract ($laneKeyedPolicyLost.Count -gt 0) 'The rearrangement must be one a lane-coupled policy key would have failed on, otherwise the lane-free assertion above proves nothing.'

    # Discrimination controls for the key derivation itself.
    #
    # The two assertions above compare *sets* across a rearrangement, and a set comparison is blind
    # to most ways of breaking a key: a key that dropped `identity` and degenerated to
    # `source|rule` is still perfectly rearrangement-invariant, and so is one that returned a
    # constant for every match. Mutation testing on scripts/lib/BackendTestShardSelectors.ps1 found
    # exactly that — four of six mutations survived. The checks below close it by asserting the key's
    # *structure*, its *cardinality*, and then the *match* that feeds it: ordinality, sibling
    # containment, and the blank-identity guard. Each was added because a mutation of the production
    # function survived this file; nothing here is a restatement of the docstring.

    # (1) Structure: the key is reversibly sourceId|ruleId|identity, in that order, and nothing else.
    #     Test identities, rule ids and source ids are C#/ kebab identifiers and never contain `|`,
    #     which is what makes the split a faithful inverse. This is what catches a key that returns a
    #     constant, an empty string, drops a segment, or splices in an extra field such as
    #     `requiredLane` — the last of which is why the "carries no lane" claim in
    #     docs/architecture/test-evidence-governance.md is now enforced rather than merely written.
    $structuralKeyChecks = 0
    foreach ($shard in @($manifest.fastShards)) {
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
            foreach ($match in @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($evidencePolicy.rules))) {
                $key = Get-BackendTestShardPolicyIdentityKey -Match $match
                $segments = @([string] $key -split '\|')
                Assert-Contract (@($segments).Count -eq 3) "Policy key for '$([string] $match.identity)' must be exactly three segments (sourceId|ruleId|identity); got $(@($segments).Count) in '$key'."
                Assert-Contract ([string]::Equals([string] $segments[0], [string] $match.sourceId, [StringComparison]::Ordinal)) "Policy key segment 1 must be the registering sourceId; expected '$([string] $match.sourceId)', got '$([string] $segments[0])'."
                Assert-Contract ([string]::Equals([string] $segments[1], [string] $match.ruleId, [StringComparison]::Ordinal)) "Policy key segment 2 must be the ruleId; expected '$([string] $match.ruleId)', got '$([string] $segments[1])'."
                Assert-Contract ([string]::Equals([string] $segments[2], [string] $match.identity, [StringComparison]::Ordinal)) "Policy key segment 3 must be the frozen test identity; expected '$([string] $match.identity)', got '$([string] $segments[2])'."
                Assert-Contract (-not ([string] $key -cmatch '-shard-[0-9]')) "Policy key '$key' must carry no shard topology."
                $structuralKeyChecks++
            }
        }
    }
    Assert-Contract ($structuralKeyChecks -gt 0) 'The structural policy-key contract must actually evaluate a key.'

    # (2) Cardinality: distinct (sourceId, ruleId, identity) triples must produce distinct keys. A key
    #     that dropped a segment would collapse triples onto one another, which the structural check
    #     above already catches but this one catches independently of the key's internal spelling —
    #     it is the property the two set comparisons actually rely on.
    $allPolicyTriples = [System.Collections.Generic.List[string]]::new()
    $allPolicyKeys = [System.Collections.Generic.List[string]]::new()
    foreach ($rule in @($evidencePolicy.rules)) {
        foreach ($identity in @(Get-BackendTestShardOptionalArray -Object $rule -PropertyName 'testIdentities')) {
            $syntheticMatch = [pscustomobject][ordered]@{
                selector = [string] $identity
                sourceId = [string] $rule.sourceId
                ruleId = [string] $rule.id
                identity = [string] $identity
                requiredLane = [string] $rule.requiredLane
                classification = [string] $rule.classification
            }
            [void] $allPolicyTriples.Add("$([string] $rule.sourceId)`u{241F}$([string] $rule.id)`u{241F}$([string] $identity)")
            [void] $allPolicyKeys.Add([string] (Get-BackendTestShardPolicyIdentityKey -Match $syntheticMatch))
        }
    }
    $distinctTriples = @([System.Collections.Generic.HashSet[string]]::new([string[]] @($allPolicyTriples), [System.StringComparer]::Ordinal)).Count
    $distinctKeys = @([System.Collections.Generic.HashSet[string]]::new([string[]] @($allPolicyKeys), [System.StringComparer]::Ordinal)).Count
    Assert-Contract ($distinctTriples -gt 1) 'The policy must carry more than one distinct identity triple for key cardinality to be assertable.'
    Assert-Contract ($distinctKeys -eq $distinctTriples) "Every distinct (sourceId, ruleId, identity) triple must produce its own key; $distinctTriples triples collapsed to $distinctKeys keys."

    # (3) Case sensitivity — and ordinality — of the match. `Get-BackendTestShardPolicyIdentityMatches` is what decides
    #     *which* identities a selector governs, so a comparison relaxed to OrdinalIgnoreCase would
    #     silently widen every exclusion — and no set comparison across a rearrangement can see that,
    #     because it widens both sides equally. Asserted on a probe rule rather than on the real
    #     policy so the case-only variants are guaranteed to exist.
    $caseProbeRule = [pscustomobject][ordered]@{
        id = 'probe-rule'
        sourceId = 'probe-source'
        classification = 'environment-gated'
        requiredLane = 'postgres'
        testIdentities = @('Nerv.Probe.Alpha.Beta')
    }
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector 'Nerv.Probe.Alpha.Beta' -Rules @($caseProbeRule)).Count -eq 1) 'An exact method selector must match its frozen identity.'
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector 'Nerv.Probe.Alpha' -Rules @($caseProbeRule)).Count -eq 1) 'A class selector must match the identities beneath it.'
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector 'nerv.probe.alpha.beta' -Rules @($caseProbeRule)).Count -eq 0) 'A selector differing only in case must not match; policy identities are ordinal identifiers, not case-folded names.'
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector 'nerv.probe.alpha' -Rules @($caseProbeRule)).Count -eq 0) 'A class selector differing only in case must not match either.'
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector 'Nerv.Probe.AlphaOther' -Rules @($caseProbeRule)).Count -eq 0) 'A longer sibling selector must not match an identity it merely shares a prefix with.'

    #     The docstring claims the comparison is *ordinal*, which is strictly stronger than
    #     case-sensitive: a culture-aware comparison — PowerShell's default, `-ceq` included — folds
    #     ignorable characters, so a selector carrying a soft hyphen compares equal to the identity
    #     without one and would silently widen the exclusion while every case assertion above stayed
    #     green. Probed on both branches of the match: the exact-equality one and the prefix one.
    $softHyphen = [string][char]0x00AD
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector "Nerv.Probe.Alpha.Be${softHyphen}ta" -Rules @($caseProbeRule)).Count -eq 0) 'An exact selector differing by an ignorable character must not match; the equality branch is ordinal, not culture-aware.'
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector "Nerv.Probe.Al${softHyphen}pha" -Rules @($caseProbeRule)).Count -eq 0) 'A class selector differing by an ignorable character must not match; the prefix branch is ordinal too.'

    # (4) Sibling containment. The docstring promises the prefix test carries a trailing dot precisely
    #     so that `Foo.BarTests` does not swallow `Foo.BarTestsExtra.*` — and nothing asserted it:
    #     deleting the dot left this whole file green. The check above (`Nerv.Probe.AlphaOther`) probes
    #     the harmless direction, where the *selector* is the longer sibling and no prefix rule of any
    #     spelling would match. The direction that matters is the reverse one, asserted here over a
    #     probe rule that carries the class row, a genuine member and a same-prefix sibling at once, so
    #     that widening the rule (dot deleted, or "always match") and disabling it ("never match") all
    #     fail on the same cardinality.
    $siblingProbeRule = [pscustomobject][ordered]@{
        id = 'probe-sibling-rule'
        sourceId = 'probe-source'
        classification = 'environment-gated'
        requiredLane = 'postgres'
        testIdentities = @('Nerv.Probe.BarTests', 'Nerv.Probe.BarTests.SomeMethod', 'Nerv.Probe.BarTestsExtra.SomeMethod')
    }
    $siblingMatches = @(Get-BackendTestShardPolicyIdentityMatches -Selector 'Nerv.Probe.BarTests' -Rules @($siblingProbeRule))
    $siblingMatchedIdentities = @($siblingMatches | ForEach-Object { [string] $_.identity })
    Assert-Contract ($siblingMatches.Count -eq 2) "A class selector must cover exactly its own row and the members beneath it; matched $($siblingMatches.Count): $($siblingMatchedIdentities -join ', ')."
    foreach ($coveredIdentity in @('Nerv.Probe.BarTests', 'Nerv.Probe.BarTests.SomeMethod')) {
        Assert-Contract (@($siblingMatchedIdentities | Where-Object { [string]::Equals([string] $_, $coveredIdentity, [StringComparison]::Ordinal) }).Count -eq 1) "A class selector must cover '$coveredIdentity' exactly once; matched [$($siblingMatchedIdentities -join ', ')]."
    }
    Assert-Contract (@($siblingMatchedIdentities | Where-Object { [string]::Equals([string] $_, 'Nerv.Probe.BarTestsExtra.SomeMethod', [StringComparison]::Ordinal) }).Count -eq 0) "A sibling class sharing the selector's prefix must not be swallowed; that is what the trailing dot in the prefix test buys, and matched [$($siblingMatchedIdentities -join ', ')]."

    # (5) The blank-identity guard, likewise deletable while this file stayed green. A policy row
    #     carrying an empty, whitespace-only or null identity must be covered by nothing at all — and
    #     the selector that exposes the guard is the blank one, because that is the only selector a
    #     blank identity can compare equal to. A null identity must also not throw on the way through;
    #     an exception here fails the run, which is the assertion.
    $blankProbeRule = [pscustomobject][ordered]@{
        id = 'probe-blank-rule'
        sourceId = 'probe-source'
        classification = 'environment-gated'
        requiredLane = 'postgres'
        testIdentities = @('', '   ', $null)
    }
    foreach ($blankProbeSelector in @('', '   ', 'Nerv.Probe.BarTests')) {
        $blankMatches = @(Get-BackendTestShardPolicyIdentityMatches -Selector $blankProbeSelector -Rules @($blankProbeRule))
        Assert-Contract ($blankMatches.Count -eq 0) "A blank policy identity must never be covered by any selector; selector '$blankProbeSelector' matched $($blankMatches.Count)."
    }

    # (6) Identity padding, the last undefined corner of this derivation (#1509). A *blank* identity is
    #     covered by nothing (above); a *padded* one used to be undefined — adding `.Trim()` to the
    #     identity left this entire file green, so neither "compare as written" nor "normalize first"
    #     was actually the contract. The ruling is compare-as-written, and it is asserted from both
    #     ends so that a `.Trim()` on either side fails here: the unpadded selector must not reach the
    #     padded identity, and the padded selector must not reach the unpadded identity. The
    #     complementary half of the ruling — padding is rejected where the policy is authored — is
    #     asserted against Test-NervTestEvidencePolicy in scripts/tests/test-evidence.Tests.ps1.
    $paddedProbeRule = [pscustomobject][ordered]@{
        id = 'probe-padded-rule'
        sourceId = 'probe-source'
        classification = 'environment-gated'
        requiredLane = 'postgres'
        testIdentities = @(' Nerv.Probe.Padded.Leading', 'Nerv.Probe.Padded.Trailing ')
    }
    foreach ($paddedProbeSelector in @('Nerv.Probe.Padded.Leading', 'Nerv.Probe.Padded.Trailing')) {
        $paddedMatches = @(Get-BackendTestShardPolicyIdentityMatches -Selector $paddedProbeSelector -Rules @($paddedProbeRule))
        Assert-Contract ($paddedMatches.Count -eq 0) "A policy identity carrying leading or trailing whitespace is compared as written, so the unpadded selector '$paddedProbeSelector' must not reach it; matched $($paddedMatches.Count)."
    }
    #     The class-prefix branch is the asymmetric case and is pinned rather than waved past: a
    #     *trailing*-padded identity still sits under its class prefix (the padding is past the dot),
    #     while a *leading*-padded one does not, so the class selector must cover exactly one of the
    #     two. Trimming the identity would make it cover both.
    $paddedClassMatches = @(Get-BackendTestShardPolicyIdentityMatches -Selector 'Nerv.Probe.Padded' -Rules @($paddedProbeRule))
    $paddedClassIdentities = @($paddedClassMatches | ForEach-Object { [string] $_.identity })
    Assert-Contract ($paddedClassMatches.Count -eq 1) "A class selector must cover the trailing-padded identity and not the leading-padded one; matched $($paddedClassMatches.Count): [$($paddedClassIdentities -join ', ')]."
    Assert-Contract ([string]::Equals([string] $paddedClassIdentities[0], 'Nerv.Probe.Padded.Trailing ', [StringComparison]::Ordinal)) "The covered identity must be carried through with its padding intact, not normalized; got '$([string] $paddedClassIdentities[0])'."
    $unpaddedProbeRule = [pscustomobject][ordered]@{
        id = 'probe-unpadded-rule'
        sourceId = 'probe-source'
        classification = 'environment-gated'
        requiredLane = 'postgres'
        testIdentities = @('Nerv.Probe.Unpadded.Method')
    }
    foreach ($paddedSelector in @(' Nerv.Probe.Unpadded.Method', 'Nerv.Probe.Unpadded.Method ', ' Nerv.Probe.Unpadded')) {
        $paddedSelectorMatches = @(Get-BackendTestShardPolicyIdentityMatches -Selector $paddedSelector -Rules @($unpaddedProbeRule))
        Assert-Contract ($paddedSelectorMatches.Count -eq 0) "A padded selector must not be trimmed into a match against an unpadded identity; selector '$paddedSelector' matched $($paddedSelectorMatches.Count)."
    }
    Assert-Contract (@(Get-BackendTestShardPolicyIdentityMatches -Selector 'Nerv.Probe.Unpadded.Method' -Rules @($unpaddedProbeRule)).Count -eq 1) 'The padding assertions must be discriminating: the unpadded selector still matches its unpadded identity.'

    # The gate itself, over the rearranged topology. The assertions above compare derivations; this
    # runs the real policy gate as a process and requires it to be satisfied by a shard layout it has
    # never seen, including the excludedTestLanes derivation that is the only place a shard id meets
    # a MAN-661 lane. Solution filters are regenerated for the two touched shards because the gate
    # also requires filter and manifest to agree project-for-project, and a filter mismatch would
    # fail the run for a reason that has nothing to do with policy keys.
    function New-ShardFixtureSolutionFilter {
        param(
            [Parameter(Mandatory)] [object] $Shard,
            [Parameter(Mandatory)] [string] $Directory,
            [Parameter(Mandatory)] [string] $RepositoryRelativeDirectory
        )

        # Project entries in a .slnf are relative to the solution the filter points at, which is
        # backend/Nerv.IIP.sln, so a manifest path is the same string minus its `backend/` prefix.
        $projects = @(Get-NervStringsSorted -Values @(@($Shard.projects) | ForEach-Object { ([string] $_) -replace '^backend/', '' }) -Comparer ([StringComparer]::Ordinal) -Unique)
        $fileName = "shard-fixture-{0}-{1}.slnf" -f ([string] $Shard.id), ([Guid]::NewGuid().ToString('N'))
        $filterPath = Join-Path $Directory $fileName
        Set-Content -LiteralPath $filterPath -NoNewline -Value ([pscustomobject]@{
            solution = [pscustomobject]@{ path = '../backend/Nerv.IIP.sln'; projects = $projects }
        } | ConvertTo-Json -Depth 10)
        return "$RepositoryRelativeDirectory/$fileName"
    }

    $fixtureFilterDirectory = Join-Path $repoRoot 'artifacts'
    New-Item -ItemType Directory -Path $fixtureFilterDirectory -Force | Out-Null
    $fixtureFilterPaths = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($shard in @($donor, $receiver)) {
            $relativeFilter = New-ShardFixtureSolutionFilter -Shard $shard -Directory $fixtureFilterDirectory -RepositoryRelativeDirectory 'artifacts'
            [void] $fixtureFilterPaths.Add((Join-Path $repoRoot $relativeFilter))
            $shard.solutionFilter = $relativeFilter
        }

        $rearrangedManifestPath = Join-Path $timingFixtureRoot 'rearranged-manifest.json'
        Set-Content -LiteralPath $rearrangedManifestPath -NoNewline -Value ($rearranged | ConvertTo-Json -Depth 100)
        $rearrangedGate = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-rearranged-policy-gate' -Arguments @('-ManifestPath', $rearrangedManifestPath)
        Assert-Contract ($rearrangedGate.Passed) "A shard rearrangement that moves a project with its exclusions must lose zero policy keys and satisfy the policy gate unchanged; the gate reported: $($rearrangedGate.Message)"

        # Negative control for the fixture, not for the product: with excludedTestLanes left behind,
        # the same rearrangement must be rejected at exactly the coupling point. This is what proves
        # the fixture reaches that rule instead of passing it by.
        $underDeclaredManifest = Get-Content -LiteralPath $rearrangedManifestPath -Raw | ConvertFrom-Json
        $underDeclaredReceiver = @($underDeclaredManifest.fastShards | Where-Object { [string]::Equals([string] $_.id, [string] $receiver.id, [StringComparison]::Ordinal) })[0]
        $underDeclaredReceiver.excludedTestLanes = @(@($underDeclaredReceiver.excludedTestLanes) | Where-Object { -not [string]::Equals([string] $_, $donorExtraLane, [StringComparison]::Ordinal) })
        $underDeclaredManifestPath = Join-Path $timingFixtureRoot 'rearranged-manifest-under-declared.json'
        Set-Content -LiteralPath $underDeclaredManifestPath -NoNewline -Value ($underDeclaredManifest | ConvertTo-Json -Depth 100)
        $underDeclaredGate = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-rearranged-under-declared-lane' -Arguments @('-ManifestPath', $underDeclaredManifestPath)
        Assert-Contract (-not $underDeclaredGate.Passed) 'The rearrangement fixture must actually exercise the excludedTestLanes derivation; a shard that keeps a moved exclusion lane must fail.'
        Assert-Contract ($underDeclaredGate.Message.Contains('must declare excludedTestLanes', [StringComparison]::Ordinal)) 'The under-declared control must fail at the excludedTestLanes coupling point, not somewhere else.'
    }
    finally {
        foreach ($fixtureFilterPath in $fixtureFilterPaths) { Remove-Item -LiteralPath $fixtureFilterPath -Force -ErrorAction SilentlyContinue }
    }

    # The three semantic hard gates are derived from policy plus runtime records, and take no shard
    # manifest at all — so re-homing a project can only reach them through the *lane* a shard
    # certifies. Evaluated with the production engine (Get-NervTestEvidenceViolations), under the
    # donor's lane and the receiver's lane, over the same synthetic runtime skip for each moved
    # identity: the verdict per test identity must be byte-identical.
    $movedIdentities = @(
        foreach ($selector in @($movedSelectors)) {
            Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($evidencePolicy.rules)
        }
    )
    Assert-Contract (@($movedIdentities).Count -gt 0) 'The moved exclusions must resolve to policy identities for the hard-gate comparison to have inputs.'

    function Get-HardGateVerdicts {
        param(
            [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Matches,
            [Parameter(Mandatory)] [object] $EvidencePolicy,
            [Parameter(Mandatory)] [string] $Lane
        )

        $records = @(
            foreach ($match in @($Matches)) {
                $rule = @($EvidencePolicy.rules | Where-Object { [string]::Equals([string] $_.id, [string] $match.ruleId, [StringComparison]::Ordinal) })[0]
                # The reason patterns registered for these rules are fully anchored literals, so the
                # literal they accept is the pattern with its anchors and escapes removed. Asserted
                # rather than assumed, so a future non-literal pattern fails loudly here instead of
                # silently producing a record that matches nothing.
                $reason = (([string] $rule.reasonPattern) -replace '^\^', '' -replace '\$$', '') -replace '\\(.)', '$1'
                Assert-Contract ($reason -cmatch [string] $rule.reasonPattern) "The hard-gate fixture must synthesize a skip reason that rule '$($rule.id)' actually accepts."
                [pscustomobject]@{
                    lane = $Lane
                    testName = [string] $match.identity
                    outcome = 'skipped'
                    skipReason = $reason
                }
            }
        )

        $violations = @(Get-NervTestEvidenceViolations -Records $records -Policy $EvidencePolicy -SelectedLanes @($Lane) -RunnerOs 'Linux')
        return @(Get-NervStringsSorted -Values @($violations | ForEach-Object { "$([string] $_.code)|$([string] $_.id)" }) -Comparer ([StringComparer]::Ordinal))
    }

    $donorLaneVerdicts = @(Get-HardGateVerdicts -Matches $movedIdentities -EvidencePolicy $evidencePolicy -Lane ([string] $donor.evidenceLane))
    $receiverLaneVerdicts = @(Get-HardGateVerdicts -Matches $movedIdentities -EvidencePolicy $evidencePolicy -Lane ([string] $receiver.evidenceLane))
    Assert-Contract ([string]::Equals(($donorLaneVerdicts -join "`n"), ($receiverLaneVerdicts -join "`n"), [StringComparison]::Ordinal)) "Moving a project between shards must not change a single unregistered-skip / illegal-quarantine / zero-execution verdict; donor lane reported [$($donorLaneVerdicts -join ', ')] and receiver lane [$($receiverLaneVerdicts -join ', ')]."
    Assert-Contract (@($donorLaneVerdicts | Where-Object { $_.StartsWith('unregistered-skip|', [StringComparison]::Ordinal) }).Count -eq 0) 'A registered skip must stay registered under the shard lane that owns it.'

    # Control: both verdict sets above are legitimately *empty* — a registered skip in an allowed
    # lane is not a violation — and two empty sets compare equal no matter what the engine does.
    # This proves the fixture is live: the same records under a lane the rules do not allow do
    # produce `unregistered-skip`, so "identical and empty" is a result rather than a dead input.
    $foreignLaneVerdicts = @(Get-HardGateVerdicts -Matches $movedIdentities -EvidencePolicy $evidencePolicy -Lane 'connector-host')
    $foreignUnregisteredSkips = @($foreignLaneVerdicts | Where-Object { $_.StartsWith('unregistered-skip|', [StringComparison]::Ordinal) })
    Assert-Contract ($foreignUnregisteredSkips.Count -gt 0) 'The hard-gate fixture must be able to produce a violation at all, otherwise the equal-verdicts assertion above compares two empty sets for free.'
    Write-Host "  [live-verdict] unregistered-skip: $($foreignUnregisteredSkips.Count)"

    # The fixture above only ever emits `unregistered-skip`. `illegal-quarantine` and
    # `zero-execution` were, in every lane it evaluates, empty on both sides — so those two thirds of
    # "the three hard gates are unchanged" were two empty sets agreeing, which is not a test. #1507's
    # acceptance is per gate, so each gate gets a fixture that actually emits its own verdict, plus a
    # control proving the fixture responds to its input rather than returning a constant.

    # --- Hard gate 2: illegal-quarantine. ---
    # Quarantine legality is decided from the policy row's own metadata (issue, exit condition, an
    # unexpired ISO date) and reads no lane at all, so the shard dimension cannot reach it. Asserted
    # rather than assumed: the same expired row is evaluated under the donor lane and the receiver
    # lane. The probe row's patterns and identity deliberately match nothing real, so it cannot also
    # become a second applicable rule for some genuine skip and turn this into an unregistered-skip
    # fixture by accident.
    function New-ProbeQuarantineRule {
        param([Parameter(Mandatory)] [AllowNull()] [string] $ExpiresOn)

        return [pscustomobject][ordered]@{
            id = 'probe-quarantine'
            sourceId = 'probe-quarantine'
            classification = 'quarantined'
            testPattern = '^Nerv\.Probe\.Quarantine\..+$'
            reasonPattern = '^Probe quarantine fixture\.$'
            allowedLanes = @('backend')
            requiredLane = ''
            allowedOperatingSystems = @()
            responsibilityIssue = 'MAN-1507'
            expiresOn = $ExpiresOn
            exitCondition = 'Probe fixture only; never satisfied.'
            testIdentities = @('Nerv.Probe.Quarantine.Frozen')
            expectedRuntimeTestCount = 1
        }
    }

    function Get-ProbeQuarantineVerdicts {
        param(
            [Parameter(Mandatory)] [object] $QuarantineRule,
            [Parameter(Mandatory)] [string] $Lane
        )

        $probePolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
        $probePolicy.rules = @(@($probePolicy.rules) + @($QuarantineRule))
        return @(
            Get-NervStringsSorted -Values @(Get-NervTestEvidenceViolations -Records @() -Policy $probePolicy -SelectedLanes @($Lane) -RunnerOs 'Linux' |
                ForEach-Object { "$([string] $_.code)|$([string] $_.id)" }) -Comparer ([StringComparer]::Ordinal)
        )
    }

    $expiredQuarantineRule = New-ProbeQuarantineRule -ExpiresOn '2000-01-01'
    $donorQuarantineVerdicts = @(Get-ProbeQuarantineVerdicts -QuarantineRule $expiredQuarantineRule -Lane ([string] $donor.evidenceLane))
    $receiverQuarantineVerdicts = @(Get-ProbeQuarantineVerdicts -QuarantineRule $expiredQuarantineRule -Lane ([string] $receiver.evidenceLane))
    $illegalQuarantineVerdicts = @($donorQuarantineVerdicts | Where-Object { $_.StartsWith('illegal-quarantine|', [StringComparison]::Ordinal) })
    Assert-Contract ($illegalQuarantineVerdicts.Count -gt 0) 'The illegal-quarantine fixture must actually produce that verdict, otherwise the cross-lane comparison below is two empty sets.'
    Assert-Contract ([string]::Equals(($donorQuarantineVerdicts -join "`n"), ($receiverQuarantineVerdicts -join "`n"), [StringComparison]::Ordinal)) "Moving a project between shards must not change an illegal-quarantine verdict; donor lane reported [$($donorQuarantineVerdicts -join ', ')] and receiver lane [$($receiverQuarantineVerdicts -join ', ')]."
    Write-Host "  [live-verdict] illegal-quarantine: $($illegalQuarantineVerdicts.Count)"

    # Control: the same row with valid unexpired metadata must produce nothing. Without it the
    # assertion above would still pass if the gate had degenerated into "always report".
    $liveQuarantineRule = New-ProbeQuarantineRule -ExpiresOn ([DateTimeOffset]::UtcNow.AddDays(30).UtcDateTime.ToString('yyyy-MM-dd'))
    $liveQuarantineVerdicts = @(Get-ProbeQuarantineVerdicts -QuarantineRule $liveQuarantineRule -Lane ([string] $donor.evidenceLane))
    Assert-Contract (@($liveQuarantineVerdicts | Where-Object { $_.StartsWith('illegal-quarantine|', [StringComparison]::Ordinal) }).Count -eq 0) 'A quarantine row with an issue, an exit condition and an unexpired date must not be reported illegal; otherwise the fixture proves only that the gate always fires.'

    # --- Hard gate 3: zero-execution. ---
    # This gate speaks only about `realDependency: true` lanes, so it cannot be reached through the
    # donor/receiver fast-shard lanes — those are `realDependency: false` by contract. The shard
    # dimension reaches it through the lane a *record* carries: re-homing the work that certifies a
    # real-dependency lane must not change whether that lane counts as having executed.
    function Get-ProbeZeroExecutionVerdicts {
        param(
            [Parameter(Mandatory)] [object] $EvidencePolicy,
            [Parameter(Mandatory)] [AllowEmptyString()] [string] $ExecutedRecordLane,
            [Parameter(Mandatory)] [string] $SelectedLane
        )

        $records = @(
            if (-not [string]::IsNullOrEmpty($ExecutedRecordLane)) {
                [pscustomobject][ordered]@{
                    lane = $ExecutedRecordLane
                    testName = 'Nerv.Probe.RealDependency.Executes'
                    outcome = 'passed'
                    skipReason = ''
                }
            }
        )
        return @(
            Get-NervStringsSorted -Values @(Get-NervTestEvidenceViolations -Records $records -Policy $EvidencePolicy -SelectedLanes @($SelectedLane) -RunnerOs 'Linux' |
                ForEach-Object { "$([string] $_.code)|$([string] $_.id)" }) -Comparer ([StringComparer]::Ordinal)
        )
    }

    $realDependencyLane = 'postgres'
    $zeroExecutionFromShard1 = @(Get-ProbeZeroExecutionVerdicts -EvidencePolicy $evidencePolicy -ExecutedRecordLane 'backend-shard-1' -SelectedLane $realDependencyLane)
    $zeroExecutionFromShard4 = @(Get-ProbeZeroExecutionVerdicts -EvidencePolicy $evidencePolicy -ExecutedRecordLane 'backend-shard-4' -SelectedLane $realDependencyLane)
    $zeroExecutionVerdicts = @($zeroExecutionFromShard1 | Where-Object { $_.StartsWith('zero-execution|', [StringComparison]::Ordinal) })
    Assert-Contract ($zeroExecutionVerdicts.Count -gt 0) 'The zero-execution fixture must actually produce that verdict, otherwise the cross-shard comparison below is two empty sets.'
    Assert-Contract ([string]::Equals(($zeroExecutionFromShard1 -join "`n"), ($zeroExecutionFromShard4 -join "`n"), [StringComparison]::Ordinal)) "Which fast shard ran the unrelated work must not change a zero-execution verdict for a selected real-dependency lane; got [$($zeroExecutionFromShard1 -join ', ')] vs [$($zeroExecutionFromShard4 -join ', ')]."
    Write-Host "  [live-verdict] zero-execution: $($zeroExecutionVerdicts.Count)"

    # Controls. First that the gate is not a constant: work attributed to the selected lane clears
    # it. Then the positive form of the same re-homing invariance — the logical lane and every shard
    # spelling of it certify identically, which is the property that lets `-shard-N` exist at all.
    $zeroExecutionSatisfied = @(Get-ProbeZeroExecutionVerdicts -EvidencePolicy $evidencePolicy -ExecutedRecordLane $realDependencyLane -SelectedLane $realDependencyLane)
    Assert-Contract (@($zeroExecutionSatisfied | Where-Object { $_.StartsWith('zero-execution|', [StringComparison]::Ordinal) }).Count -eq 0) 'A passed result in the selected real-dependency lane must clear zero-execution; otherwise the fixture proves only that the gate always fires.'
    foreach ($shardSpelling in @("$realDependencyLane-shard-1", "$realDependencyLane-shard-2", "$realDependencyLane-shard-11")) {
        $spelledVerdicts = @(Get-ProbeZeroExecutionVerdicts -EvidencePolicy $evidencePolicy -ExecutedRecordLane $shardSpelling -SelectedLane $realDependencyLane)
        Assert-Contract ([string]::Equals(($spelledVerdicts -join "`n"), ($zeroExecutionSatisfied -join "`n"), [StringComparison]::Ordinal)) "A real-dependency lane certified from shard spelling '$shardSpelling' must produce the same verdicts as the logical lane; got [$($spelledVerdicts -join ', ')]."
    }

    # The same statement about the policy file itself: a rule matches on test identity and reason, and
    # its lane fields are logical lanes. A shard-suffixed lane in a rule would re-couple the two.
    foreach ($rule in @($evidencePolicy.rules)) {
        foreach ($laneValue in @(@($rule.allowedLanes) + @([string] $rule.requiredLane))) {
            Assert-Contract (-not ([string] $laneValue -cmatch '-shard-[0-9]')) "Evidence policy rule '$($rule.id)' must key on a logical lane, never on a shard: '$laneValue'."
        }
        Assert-Contract (@($rule.testIdentities).Count -gt 0 -or [string]::Equals([string] $rule.classification, 'quarantined', [StringComparison]::Ordinal)) "Evidence policy rule '$($rule.id)' must key on explicit test identities."
    }

    # Lane is a rule's *applicability condition*, never part of its identity key, and the two are
    # easy to confuse because `Test-NervRuleApplies` does read `allowedLanes`/`requiredLane`. What it
    # reads is the **logical** lane: it strips any `-shard-N` suffix before matching, so the shard
    # dimension is gone by the time any comparison happens. That is what keeps the third hard gate
    # ("a selected real-dependency lane executed nothing") meaningful while leaving a re-shard unable
    # to change a verdict. Asserted rather than described: every rule must decide identically for a
    # logical lane and for every shard spelling of it.
    # Narrative: docs/architecture/test-evidence-governance.md, "Timing data is a cache, not a
    # governed asset" (lane as applicability condition versus identity key).
    $logicalLanesUnderTest = @('backend', 'connector-host', 'postgres', 'full-chain', 'performance', 'redis-cap')
    $laneSuffixCases = 0
    foreach ($rule in @($evidencePolicy.rules)) {
        foreach ($runnerOs in @('Linux', 'Windows')) {
            foreach ($logicalLane in $logicalLanesUnderTest) {
                $logicalVerdict = [bool] (Test-NervRuleApplies -Rule $rule -SelectedLanes @($logicalLane) -RunnerOs $runnerOs)
                foreach ($shardIndex in @(1, 2, 3, 4, 17)) {
                    $shardedVerdict = [bool] (Test-NervRuleApplies -Rule $rule -SelectedLanes @("$logicalLane-shard-$shardIndex") -RunnerOs $runnerOs)
                    Assert-Contract ($shardedVerdict -eq $logicalVerdict) "Rule '$($rule.id)' must decide identically for logical lane '$logicalLane' and its shard spelling '$logicalLane-shard-$shardIndex' on $runnerOs; got $logicalVerdict vs $shardedVerdict."
                    $laneSuffixCases++
                }
            }
        }
    }
    Assert-Contract ($laneSuffixCases -gt 0) 'The lane-suffix independence contract must actually evaluate rules.'

    # The refresher's degradation path: with a `gh` that fails, the cache is simply not refreshed and
    # the entry point still exits 0. Asserted with a stub on PATH rather than by calling GitHub, so
    # the case is deterministic and offline. Restricted to the platforms the Backend Test Shard
    # Governance job and local development actually run on: on Windows a PATH stub has to satisfy
    # PATHEXT resolution through `Process.Start`, which is a different mechanism and would make this
    # a test of the stub rather than of the degradation.
    if (-not $IsWindows) {
        $stubBin = Join-Path $timingFixtureRoot 'stub-bin'
        New-Item -ItemType Directory -Path $stubBin -Force | Out-Null
        $stubGh = Join-Path $stubBin 'gh'
        Set-Content -LiteralPath $stubGh -NoNewline -Value "#!/bin/sh`necho 'stub gh: unavailable' 1>&2`nexit 1`n"
        Invoke-NativeCommandOutput -Command 'chmod' -Arguments @('+x', $stubGh) -WorkingDirectory $repoRoot -Name 'shard-timings-stub-chmod' | Out-Null

        $degradedCachePath = Join-Path $timingFixtureRoot 'degraded-timings.json'
        $originalPath = $env:PATH
        $degraded = $null
        try {
            $env:PATH = "$stubBin$([IO.Path]::PathSeparator)$originalPath"
            $degraded = Invoke-GovernedScript -ScriptPath $timingUpdateScript -Name 'shard-timings-degraded-refresh' -Arguments @('-OutputPath', $degradedCachePath)
        }
        finally {
            $env:PATH = $originalPath
        }

        Assert-Contract ($degraded.Passed) 'An unavailable GitHub CLI must leave the timing refresher at exit 0; a timing cache miss is not a repository defect.'
        Assert-Contract ($degraded.Message.Contains('was not refreshed', [StringComparison]::Ordinal)) 'The timing refresher must say it did not refresh instead of pretending it did.'
        Assert-Contract (-not (Test-Path -LiteralPath $degradedCachePath)) 'A failed refresh must not write a cache file at all, so the previous cache stays authoritative.'
    }

    # Determinism debt rows are keyed on source path + pattern + line hash. No lane, no shard.
    $determinismBaseline = Get-Content -LiteralPath (Join-Path $repoRoot 'backend/test-determinism-baseline.json') -Raw | ConvertFrom-Json
    Assert-Contract (@($determinismBaseline.exceptions).Count -gt 0) 'The determinism baseline must carry rows for its key shape to be assertable.'
    foreach ($exception in @($determinismBaseline.exceptions)) {
        foreach ($property in @($exception.PSObject.Properties.Name)) {
            Assert-Contract ($property -cnotmatch '(?i)lane|shard') "Determinism debt row '$($exception.path)' must not carry a lane or shard dimension: '$property'."
        }
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string] $exception.path)) 'Every determinism debt row must key on a source path.'
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string] $exception.lineTextSha256)) 'Every determinism debt row must key on its line hash.'
    }
}
finally {
    Remove-Item -LiteralPath $timingFixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
Assert-Contract (-not (Test-Path -LiteralPath $timingFixtureRoot)) 'The shard timing fixtures must be cleaned up.'

Write-Host 'Backend test shard manifest contract tests passed.'
