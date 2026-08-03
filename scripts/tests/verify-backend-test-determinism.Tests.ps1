# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the backend test determinism verifier against a disposable stubbed .NET harness
#   Writes:
#     - Temporary harness and evidence files under the operating-system temp directory
#     - artifacts/script-logs/backend-test-determinism-verifier-test-*/**
#   Cleanup:
#     - Removes the disposable harness, evidence, and governed command logs created by this test
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$verifier = Join-Path $repoRoot 'scripts/verify-backend-test-determinism.ps1'
$caseId = [Guid]::NewGuid().ToString('N')
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-backend-test-determinism-verifier-$caseId"
$harnessRoot = Join-Path $tempRoot 'harness'
$artifactRoot = Join-Path $tempRoot 'evidence'
$capturePath = Join-Path $tempRoot 'dotnet-invocations.jsonl'
$scriptLogName = "backend-test-determinism-verifier-test-$caseId"
$scriptLogRoot = Join-Path $repoRoot "artifacts/script-logs/$scriptLogName"

$projects = @(
    'backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj',
    'backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj',
    'backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj',
    'backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj'
)

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [AllowNull()]
        [object] $Expected,

        [AllowNull()]
        [object] $Actual,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if ($Expected -cne $Actual) {
        throw "$Message Expected='$Expected' Actual='$Actual'."
    }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function New-StubbedHarness {
    if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
        throw "Backend test determinism verifier must exist: $verifier"
    }

    $harnessScripts = Join-Path $harnessRoot 'scripts'
    $harnessLibrary = Join-Path $harnessScripts 'lib'
    [System.IO.Directory]::CreateDirectory($harnessLibrary) | Out-Null
    Copy-Item -LiteralPath $verifier -Destination (Join-Path $harnessScripts 'verify-backend-test-determinism.ps1')

    $stubHelper = @'
Set-StrictMode -Version Latest

function Write-Diagnostic {
    param([string] $Message, [string] $Level = 'INFO')
}

function Invoke-WithScopedEnvironment {
    param([hashtable] $Variables, [scriptblock] $ScriptBlock)

    $originals = @{}
    foreach ($key in $Variables.Keys) {
        $originals[$key] = [pscustomobject]@{
            HadValue = Test-Path "Env:$key"
            Value = [Environment]::GetEnvironmentVariable($key, 'Process')
        }
    }

    try {
        foreach ($key in $Variables.Keys) {
            Set-Item "Env:$key" $Variables[$key]
        }
        & $ScriptBlock
    }
    finally {
        foreach ($key in $originals.Keys) {
            if ($originals[$key].HadValue) {
                Set-Item "Env:$key" $originals[$key].Value
            }
            else {
                Remove-Item "Env:$key" -ErrorAction SilentlyContinue
            }
        }
    }
}

function Invoke-DotNet {
    param(
        [string[]] $Arguments,
        [string] $WorkingDirectory,
        [int] $TimeoutSeconds = 600,
        [string] $Name = 'dotnet',
        [int[]] $SensitiveArgumentIndexes = @()
    )

    $record = [ordered]@{
        arguments = @($Arguments)
        seed = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_ORDER_SEED', 'Process')
        name = $Name
        timeoutSeconds = $TimeoutSeconds
    }
    $line = $record | ConvertTo-Json -Depth 5 -Compress
    [System.IO.File]::AppendAllText(
        $env:NERV_MAN662_CAPTURE_PATH,
        "$line$([Environment]::NewLine)",
        [System.Text.UTF8Encoding]::new($false))

    if ($Arguments.Count -ge 2 -and
        $Arguments[0] -ceq 'test' -and
        $Arguments[1] -ceq $env:NERV_MAN662_FAIL_PROJECT -and
        $record.seed -ceq $env:NERV_MAN662_FAIL_SEED) {
        throw "Command 'dotnet' exited with 23 after 00:00:00.001. Logs: stub"
    }

    return [pscustomobject]@{
        ExitCode = 0
        Duration = [TimeSpan]::FromMilliseconds(1)
    }
}
'@
    Write-Utf8NoBom -Path (Join-Path $harnessLibrary 'ScriptAutomation.ps1') -Content $stubHelper
}

function Invoke-VerifierCase {
    param(
        [Parameter(Mandatory)]
        [string] $InvocationId,

        [string] $FailProject,

        [string] $FailSeed
    )

    if (Test-Path -LiteralPath $capturePath) {
        Remove-Item -LiteralPath $capturePath -Force
    }

    $variables = @{
        NERV_MAN662_CAPTURE_PATH = $capturePath
        NERV_MAN662_FAIL_PROJECT = $FailProject
        NERV_MAN662_FAIL_SEED = $FailSeed
    }
    $exitCode = 0
    try {
        Invoke-WithScopedEnvironment -Variables $variables -ScriptBlock {
            Invoke-PwshScript `
                -ScriptPath (Join-Path $harnessRoot 'scripts/verify-backend-test-determinism.ps1') `
                -Arguments @('-ArtifactRoot', $artifactRoot, '-InvocationId', $InvocationId) `
                -WorkingDirectory $harnessRoot `
                -TimeoutSeconds 60 `
                -Name $scriptLogName | Out-Null
        }
    }
    catch {
        if ($_.Exception.Message -notmatch 'exited with (?<exitCode>\d+)') {
            throw
        }
        $exitCode = [int] $Matches['exitCode']
    }

    $records = @(
        Get-Content -LiteralPath $capturePath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json }
    )
    $testRecords = @($records | Where-Object { $_.arguments[0] -ceq 'test' })
    $summaryPath = Join-Path (Join-Path $artifactRoot $InvocationId) 'summary.json'
    Assert-True -Condition (Test-Path -LiteralPath $summaryPath -PathType Leaf) -Message "Summary was not written for '$InvocationId'."

    return [pscustomobject]@{
        ExitCode = $exitCode
        Records = $testRecords
        SummaryPath = $summaryPath
        Summary = @(Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json)
    }
}

function Assert-SixRoundContract {
    param(
        [Parameter(Mandatory)]
        [object] $Case
    )

    Assert-Equal -Expected 24 -Actual $Case.Records.Count -Message 'Verifier must invoke four target projects in each of six rounds.'
    Assert-Equal -Expected 6 -Actual $Case.Summary.Count -Message 'Summary must contain exactly six rows.'

    $expectedFields = @('elapsedMs', 'exitCode', 'profile', 'projectOrder', 'run', 'seed')
    for ($index = 0; $index -lt 6; $index++) {
        $roundNumber = $index + 1
        $seed = 'man662-{0:d2}' -f $roundNumber
        $profile = if ($roundNumber % 2 -eq 1) { 'serial' } else { 'parallel' }
        $threads = if ($profile -ceq 'serial') { '1' } else { '4' }
        $expectedOrder = @(
            for ($offset = 0; $offset -lt $projects.Count; $offset++) {
                $projects[($index + $offset) % $projects.Count]
            }
        )

        $row = $Case.Summary[$index]
        $actualFields = @($row.PSObject.Properties.Name | Sort-Object)
        Assert-Equal -Expected ($expectedFields -join '|') -Actual ($actualFields -join '|') -Message "Round $roundNumber summary fields must stay local-reproduction-only."
        Assert-Equal -Expected $roundNumber -Actual ([int] $row.run) -Message "Round $roundNumber run number is wrong."
        Assert-Equal -Expected $seed -Actual ([string] $row.seed) -Message "Round $roundNumber seed is wrong."
        Assert-Equal -Expected $profile -Actual ([string] $row.profile) -Message "Round $roundNumber profile is wrong."
        Assert-Equal -Expected ($expectedOrder -join '|') -Actual (@($row.projectOrder) -join '|') -Message "Round $roundNumber project order is not the required rotation."
        Assert-True -Condition ([long] $row.elapsedMs -ge 0) -Message "Round $roundNumber elapsedMs must be nonnegative."

        $roundRecords = @($Case.Records | Where-Object { $_.seed -ceq $seed })
        Assert-Equal -Expected 4 -Actual $roundRecords.Count -Message "Round $roundNumber must invoke four seeded test projects."
        Assert-Equal -Expected ($expectedOrder -join '|') -Actual (($roundRecords | ForEach-Object { $_.arguments[1] }) -join '|') -Message "Round $roundNumber invocation order is wrong."

        $settingsPaths = @(
            $roundRecords |
                ForEach-Object {
                    $settingsIndex = [Array]::IndexOf([object[]] $_.arguments, '--settings')
                    Assert-True -Condition ($settingsIndex -ge 0) -Message "Round $roundNumber invocation omitted --settings."
                    [string] $_.arguments[$settingsIndex + 1]
                } |
                Sort-Object -Unique
        )
        Assert-Equal -Expected 1 -Actual $settingsPaths.Count -Message "Round $roundNumber must use one generated runsettings file."
        Assert-True -Condition (Test-Path -LiteralPath $settingsPaths[0] -PathType Leaf) -Message "Round $roundNumber runsettings file is missing."
        [xml] $settings = Get-Content -LiteralPath $settingsPaths[0] -Raw
        Assert-Equal -Expected 'true' -Actual ([string] $settings.RunSettings.xUnit.ParallelizeTestCollections) -Message "Round $roundNumber must enable collection parallelization through VSTest settings."
        Assert-Equal -Expected $threads -Actual ([string] $settings.RunSettings.xUnit.MaxParallelThreads) -Message "Round $roundNumber max thread profile is wrong."
    }
}

[System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null

try {
    New-StubbedHarness

    $successful = Invoke-VerifierCase -InvocationId 'success'
    Assert-Equal -Expected 0 -Actual $successful.ExitCode -Message 'Successful six-round verifier run must exit zero.'
    Assert-SixRoundContract -Case $successful
    Assert-True -Condition (@($successful.Summary | Where-Object { [int] $_.exitCode -ne 0 }).Count -eq 0) -Message 'Successful summary must record exitCode 0 for every round.'

    $failed = Invoke-VerifierCase `
        -InvocationId 'failure' `
        -FailProject $projects[2] `
        -FailSeed 'man662-03'
    Assert-True -Condition ($failed.ExitCode -ne 0) -Message 'A nonzero target project run must make the verifier process nonzero.'
    Assert-SixRoundContract -Case $failed
    Assert-Equal -Expected 23 -Actual ([int] $failed.Summary[2].exitCode) -Message 'The failing project exit code must fail its round.'
    Assert-True -Condition (Test-Path -LiteralPath $successful.SummaryPath -PathType Leaf) -Message 'A later verifier execution must not replace prior evidence.'
    Assert-True -Condition ($successful.SummaryPath -cne $failed.SummaryPath) -Message 'Reruns must be recorded under a new invocation path.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $scriptLogRoot) {
        Remove-Item -LiteralPath $scriptLogRoot -Recurse -Force
    }
}

Write-Host 'Backend test determinism verifier script tests passed.'
