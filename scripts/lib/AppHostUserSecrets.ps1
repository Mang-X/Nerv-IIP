# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads the AppHost Program.cs source to discover required local user-secrets
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function Get-AppHostRequiredUserSecretNames {
    param(
        [Parameter(Mandatory)]
        [string] $AppHostProject
    )

    $appHostSource = Join-Path (Split-Path -Parent $AppHostProject) 'Program.cs'
    $sourceText = Get-Content -LiteralPath $appHostSource -Raw
    $explicitSecrets = @(
        [regex]::Matches($sourceText, 'AddParameter\s*\(\s*"(?<name>[^"]+)"\s*,\s*secret\s*:\s*true\s*\)') |
            ForEach-Object { "Parameters:$($_.Groups['name'].Value)" }
    )

    if ($explicitSecrets.Count -eq 0) {
        throw "Could not discover required AppHost secret parameters from $appHostSource."
    }

    $requiredSecrets = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $explicitSecrets) {
        $requiredSecrets.Add($name) | Out-Null
    }

    # Aspire's Postgres integration owns this parameter implicitly; it is not declared
    # through AddParameter(...) in the AppHost source, but local startup still needs it.
    $requiredSecrets.Add('Parameters:postgres-password') | Out-Null
    return [string[]] $requiredSecrets
}
