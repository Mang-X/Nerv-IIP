# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads the backend shard manifest and project references
#   Writes:
#     - None
#   Requires:
#     - PowerShell 7

function Get-NervBackendShardModulePlan {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string[]] $BusinessServices
    )

    $manifest = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'scripts/backend-test-shards.json') -Raw |
        ConvertFrom-Json -Depth 20
    $requestedModules = [Collections.Generic.HashSet[string]]::new($BusinessServices, [StringComparer]::Ordinal)
    $referenceCache = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($shard in $manifest.fastShards) {
        $matchingProjects = [Collections.Generic.List[object]]::new()
        $unresolved = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
        foreach ($project in $shard.projects) {
            $pending = [Collections.Generic.Queue[string]]::new()
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $modules = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
            $pending.Enqueue($project)
            while ($pending.Count -gt 0) {
                $path = $pending.Dequeue()
                if (-not $seen.Add($path)) { continue }
                if ($path -cmatch '^backend/services/Business/([^/]+)/') {
                    $module = ConvertTo-NervCiImpactServiceId -Name $Matches[1]
                    if ($requestedModules.Contains($module)) { [void]$modules.Add($module) }
                }
                if (-not $referenceCache.ContainsKey($path)) {
                    $absolutePath = Join-Path $RepositoryRoot $path
                    $referenceCache[$path] = if (Test-Path -LiteralPath $absolutePath -PathType Leaf) {
                        [xml]$document = Get-Content -LiteralPath $absolutePath -Raw
                        @($document.SelectNodes("//*[local-name()='ProjectReference'][@Include]") |
                            ForEach-Object { $_.GetAttribute('Include').Replace('\', '/') })
                    }
                    else { @('') }
                }
                foreach ($reference in $referenceCache[$path]) {
                    if ([string]::IsNullOrWhiteSpace($reference) -or $reference -match '[$@%()*?;]') {
                        [void]$unresolved.Add($path)
                        continue
                    }
                    $absoluteReference = [IO.Path]::GetFullPath($reference, (Split-Path -Parent (Join-Path $RepositoryRoot $path)))
                    $relativeReference = [IO.Path]::GetRelativePath($RepositoryRoot, $absoluteReference).Replace('\', '/')
                    if (-not $relativeReference.StartsWith('backend/', [StringComparison]::Ordinal)) {
                        [void]$unresolved.Add($path)
                        continue
                    }
                    $pending.Enqueue($relativeReference)
                }
            }
            if ($modules.Count -gt 0) {
                $matchingProjects.Add([pscustomobject]@{ project = $project; modules = @($modules) })
            }
        }
        [pscustomobject]@{
            shard = $shard.id
            matching_projects = $matchingProjects.ToArray()
            unresolved_projects = @($unresolved)
        }
    }
}
