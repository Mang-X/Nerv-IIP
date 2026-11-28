# Script-Governance:
#   Category: library
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function Get-ComposeServiceBlocks {
    param([Parameter(Mandatory)][string] $ComposeText)

    # Both repository Compose producers use block mappings with two-space indentation.
    $services = [regex]::Match($ComposeText, '(?ms)^services:[^\S\r\n]*\r?\n(?<body>.*?)(?=^\S|\z)')
    if (-not $services.Success) {
        throw 'Compose 缺少 services 块。'
    }

    $serviceBlocks = [hashtable]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($services.Groups['body'].Value, '(?ms)^  (?<name>[a-z0-9-]+):\r?\n(?<body>.*?)(?=^  [a-z0-9-]+:|^\S|\z)')) {
        $serviceBlocks[$match.Groups['name'].Value] = $match.Groups['body'].Value
    }
    return $serviceBlocks
}

function Assert-ProductionComposeInternalPorts {
    param([Parameter(Mandatory)][hashtable] $ServiceBlocks)

    if (-not $ServiceBlocks.ContainsKey('iam')) {
        throw 'Production Compose 缺少 iam 服务。'
    }

    foreach ($service in $ServiceBlocks.Keys) {
        if ([string]::Equals($service, 'iam', [StringComparison]::Ordinal) -or
            ($service.StartsWith('business-', [StringComparison]::Ordinal) -and
             -not [string]::Equals($service, 'business-gateway', [StringComparison]::Ordinal) -and
             -not [string]::Equals($service, 'business-console', [StringComparison]::Ordinal))) {
            if ($ServiceBlocks[$service] -match '(?m)^    ports:') {
                throw "服务 $service 不得发布宿主机端口。"
            }
        }
    }
}
