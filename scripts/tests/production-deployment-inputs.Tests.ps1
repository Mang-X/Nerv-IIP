# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes release-install input validation paths that fail before Aspire starts
#     - Validates deployment input contracts as repository text
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$startScript = Join-Path $repoRoot 'scripts/install/start-nerv-iip-apphost.ps1'
$startText = Get-Content -LiteralPath $startScript -Raw
$appHostText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs') -Raw
$dependenciesText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/compose/nerv-iip.dependencies.yml') -Raw
$platformText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/compose/nerv-iip.platform.yml') -Raw
$environmentExampleText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/compose/nerv-iip.production.env.example') -Raw
$releaseRehearsalText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/verify-production-release-rehearsal.ps1') -Raw
$environmentArtifactVerifierText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/verify-aspire-apphost-environment-artifacts.ps1') -Raw
$ciWorkflowText = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw

function Assert-ContainsOrdinal {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw $Message
    }
}

function Assert-StartFails {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $ExpectedMarker
    )

    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $startScript @Arguments 2>&1
    $renderedOutput = $output | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $renderedOutput.Contains($ExpectedMarker, [StringComparison]::Ordinal)) {
        throw "Expected release-install validation to fail with marker '$ExpectedMarker'. Output: $renderedOutput"
    }
}

$requiredProductionPublishInputs = @(
    "Messaging__Provider = 'Redis'",
    "Security__Cors__AllowedOrigins = 'https://console.example.test,https://business.example.test'",
    "ConnectorHost__ConnectorHostId = 'verify-connector-host'",
    "ConnectorHost__OrganizationId = 'verify-organization'",
    "ConnectorHost__EnvironmentId = 'verify-environment'"
)
$missingProductionPublishInputs = @($requiredProductionPublishInputs | Where-Object {
        -not $environmentArtifactVerifierText.Contains($_, [StringComparison]::Ordinal)
    })
if ($missingProductionPublishInputs.Count -gt 0) {
    throw "Production AppHost artifact publish must inject minimal compliant inputs. Missing: $($missingProductionPublishInputs -join ', ')"
}

Assert-StartFails -Arguments @('-EnvironmentName', 'Production', '-MessagingProvider', 'InMemory') `
    -ExpectedMarker '-MessagingProvider InMemory'
Assert-StartFails -Arguments @('-EnvironmentName', 'Production', '-MessagingProvider', 'Redis') `
    -ExpectedMarker '-RedisPassword is required'
Assert-StartFails -Arguments @(
    '-EnvironmentName', 'Production',
    '-MessagingProvider', 'RabbitMQ',
    '-IamJwtSigningKeyId', 'kid',
    '-IamJwtPrivateKeyPem', 'private-key',
    '-IamJwtJwksJson', '{"keys":[]}',
    '-IamSecretsPepper', 'pepper',
    '-IamEnterpriseIdentityMfaCode', '654321'
) -ExpectedMarker '-RedisPassword is required'
Assert-StartFails -Arguments @(
    '-EnvironmentName', 'Production',
    '-MessagingProvider', 'RabbitMQ',
    '-RedisPassword', 'redis-password',
    '-IamJwtSigningKeyId', 'kid',
    '-IamJwtPrivateKeyPem', 'private-key',
    '-IamJwtJwksJson', '{"keys":[]}',
    '-IamSecretsPepper', 'pepper',
    '-IamEnterpriseIdentityMfaCode', '000000'
) -ExpectedMarker '-IamEnterpriseIdentityMfaCode must override'

foreach ($contract in @(
    @($startText, '[ValidateSet("InMemory", "RabbitMQ", "Redis")]', 'release-install must accept Redis messaging'),
    @($startText, '$environment["Parameters__redis-password"] = $RedisPassword', 'release-install must map the Redis password to the AppHost parameter'),
    @($startText, '$environment["Parameters__iam-enterprise-identity-mfa-code"] = $IamEnterpriseIdentityMfaCode', 'release-install must map the MFA override to the AppHost parameter'),
    @($appHostText, 'AddParameter("iam-enterprise-identity-mfa-code", secret: true)', 'AppHost MFA override must be a secret parameter'),
    @($appHostText, 'Iam__EnterpriseIdentity__Mfa__DevelopmentCode', 'AppHost must deliver the MFA override to IAM'),
    @($dependenciesText, '--requirepass', 'legacy Redis must require authentication'),
    @($platformText, 'password=${NERV_IIP_REDIS_PASSWORD:?set NERV_IIP_REDIS_PASSWORD}', 'legacy services must authenticate to Redis'),
    @($platformText, 'Iam__Secrets__Pepper: ${NERV_IIP_IAM_SECRETS_PEPPER:?set NERV_IIP_IAM_SECRETS_PEPPER}', 'legacy IAM must receive its pepper'),
    @($platformText, 'Iam__EnterpriseIdentity__Mfa__DevelopmentCode: ${NERV_IIP_IAM_ENTERPRISE_IDENTITY_MFA_CODE:?set NERV_IIP_IAM_ENTERPRISE_IDENTITY_MFA_CODE}', 'legacy IAM must receive its MFA override'),
    @($environmentExampleText, 'NERV_IIP_REDIS_PASSWORD=change-me-strong-redis-password', 'production env example must declare the Redis password'),
    @($environmentExampleText, 'NERV_IIP_IAM_SECRETS_PEPPER=change-me-strong-iam-secrets-pepper', 'production env example must declare the IAM pepper'),
    @($environmentExampleText, 'NERV_IIP_IAM_ENTERPRISE_IDENTITY_MFA_CODE=change-me-non-development-mfa-code', 'production env example must declare the MFA override'),
    @($releaseRehearsalText, 'redis-cli -a "$NERV_IIP_REDIS_PASSWORD" --no-auth-warning ping', 'release rehearsal must read the Redis password inside the container'),
    @($ciWorkflowText, './scripts/tests/production-deployment-inputs.Tests.ps1', 'Script Governance must run the production deployment input contract tests')
)) {
    Assert-ContainsOrdinal -Text $contract[0] -Expected $contract[1] -Message $contract[2]
}

if ($releaseRehearsalText.Contains('$Environment.NERV_IIP_REDIS_PASSWORD,', [StringComparison]::Ordinal)) {
    throw 'Release rehearsal must not place the Redis password in logged Docker CLI arguments.'
}

if ($startText.Contains('throw "-IamSeedAdminPassword is required outside Development."', [StringComparison]::Ordinal)) {
    throw 'Production startup must not require a seed password when IAM seed is disabled outside Development.'
}

Write-Host 'Production deployment input contracts passed.'
