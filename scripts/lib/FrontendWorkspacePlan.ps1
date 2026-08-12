# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads frontend workspace manifests and unit-test source files
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function Get-NervFrontendManifestValue {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $Section,
        [Parameter(Mandatory)] [string] $Name
    )

    $sectionProperty = $Manifest.PSObject.Properties[$Section]
    if ($null -eq $sectionProperty -or $null -eq $sectionProperty.Value) { return $null }
    $valueProperty = $sectionProperty.Value.PSObject.Properties[$Name]
    if ($null -eq $valueProperty) { return $null }
    return [string]$valueProperty.Value
}

function Get-NervFrontendWorkspacePatterns {
    param([Parameter(Mandatory)] [string] $WorkspaceManifestPath)

    $patterns = [Collections.Generic.List[string]]::new()
    $foundPackages = $false
    $insidePackages = $false
    foreach ($line in Get-Content -LiteralPath $WorkspaceManifestPath) {
        if ($line -match '^packages:\s*(?:#.*)?$') {
            if ($foundPackages) { throw "Frontend pnpm workspace manifest '$WorkspaceManifestPath' declares packages more than once." }
            $foundPackages = $true
            $insidePackages = $true
            continue
        }
        if (-not $insidePackages) { continue }
        if ($line -match '^\S') { break }
        if ($line -match '^\s*(?:#.*)?$') { continue }
        $entryMatch = [regex]::Match($line, '^\s*-\s*(?<value>''[^'']+''|"[^"]+"|[^#\s]+)\s*(?:#.*)?$')
        if (-not $entryMatch.Success) { throw "Frontend pnpm workspace packages entry is not governed: '$line'." }
        $value = $entryMatch.Groups['value'].Value
        if (($value.StartsWith("'", [StringComparison]::Ordinal) -and $value.EndsWith("'", [StringComparison]::Ordinal)) -or
            ($value.StartsWith('"', [StringComparison]::Ordinal) -and $value.EndsWith('"', [StringComparison]::Ordinal))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $patterns.Add($value)
    }
    if (-not $foundPackages) { throw "Frontend pnpm workspace manifest '$WorkspaceManifestPath' has no packages list." }

    $actual = $patterns.ToArray()
    $expected = [string[]]@('apps/*', 'packages/*')
    [Array]::Sort($actual, [StringComparer]::Ordinal)
    [Array]::Sort($expected, [StringComparer]::Ordinal)
    if (-not [string]::Equals(($actual -join '|'), ($expected -join '|'), [StringComparison]::Ordinal)) {
        throw "Frontend pnpm workspace patterns must exactly match the governed set: $($expected -join ', '). Found: $($actual -join ', ')."
    }
    return $actual
}

function Test-NervFrontendIgnoredUnitTestPath {
    param([Parameter(Mandatory)] [string] $RelativePath)

    $normalizedPath = $RelativePath.Replace('\', '/')
    return $normalizedPath.StartsWith('node_modules/', [StringComparison]::Ordinal) -or
        $normalizedPath.Contains('/node_modules/', [StringComparison]::Ordinal)
}

function Get-NervFrontendWorkspaceInventory {
    param(
        [Parameter(Mandatory)] [string] $FrontendRoot,
        [Parameter(Mandatory)] [string] $SkipAllowlistPath
    )

    $resolvedFrontendRoot = (Resolve-Path -LiteralPath $FrontendRoot).Path
    $workspacePatterns = Get-NervFrontendWorkspacePatterns -WorkspaceManifestPath (Join-Path $resolvedFrontendRoot 'pnpm-workspace.yaml')
    $workspaceAreas = @($workspacePatterns | ForEach-Object { $_.Substring(0, $_.Length - 2) })
    $manifestFiles = @(
        foreach ($workspaceArea in $workspaceAreas) {
            Get-ChildItem -LiteralPath (Join-Path $resolvedFrontendRoot $workspaceArea) -Directory |
                ForEach-Object { Join-Path $_.FullName 'package.json' } |
                Where-Object { Test-Path -LiteralPath $_ }
        }
    )
    [Array]::Sort($manifestFiles, [StringComparer]::Ordinal)

    $projects = [Collections.Generic.List[object]]::new()
    $projectByName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $projectByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($manifestPath in $manifestFiles) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $name = [string]$manifest.name
        if ([string]::IsNullOrWhiteSpace($name)) { throw "Frontend manifest '$manifestPath' has no package name." }
        if ($name -notmatch '^@nerv-iip/[a-z0-9]+(?:-[a-z0-9]+)*$') { throw "Frontend workspace package name '$name' is not a governed @nerv-iip identifier." }
        if ($projectByName.ContainsKey($name)) { throw "Duplicate frontend workspace package name '$name'." }

        $projectDirectory = Split-Path -Parent $manifestPath
        $relativePath = [IO.Path]::GetRelativePath($resolvedFrontendRoot, $projectDirectory).Replace('\', '/')
        $scripts = [ordered]@{}
        foreach ($scriptName in @('test', 'typecheck', 'build')) {
            $scriptValue = Get-NervFrontendManifestValue -Manifest $manifest -Section 'scripts' -Name $scriptName
            if (-not [string]::IsNullOrWhiteSpace($scriptValue)) { $scripts[$scriptName] = $scriptValue }
        }

        $dependencyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($sectionName in @('dependencies', 'devDependencies', 'peerDependencies', 'optionalDependencies')) {
            $sectionProperty = $manifest.PSObject.Properties[$sectionName]
            if ($null -eq $sectionProperty -or $null -eq $sectionProperty.Value) { continue }
            foreach ($dependencyProperty in $sectionProperty.Value.PSObject.Properties) {
                if (([string]$dependencyProperty.Value).StartsWith('workspace:', [StringComparison]::Ordinal)) {
                    [void]$dependencyNames.Add([string]$dependencyProperty.Name)
                }
            }
        }
        $dependencies = @($dependencyNames)
        [Array]::Sort($dependencies, [StringComparer]::Ordinal)

        $testFiles = @()
        $sourceRoot = Join-Path $projectDirectory 'src'
        if (Test-Path -LiteralPath $sourceRoot) {
            $testFiles = @(
                Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
                    Where-Object { $_.Name -match '\.(?:test|spec)\.(?:[cm]?[jt]sx?)$' } |
                    ForEach-Object { [IO.Path]::GetRelativePath($resolvedFrontendRoot, $_.FullName).Replace('\', '/') }
            )
            [Array]::Sort($testFiles, [StringComparer]::Ordinal)
        }

        $project = [pscustomobject]@{
            name = $name
            path = $relativePath
            slug = ([regex]::Replace($name.TrimStart('@'), '[/_]+', '-')).ToLowerInvariant()
            scripts = [pscustomobject]$scripts
            dependencies = $dependencies
            test_files = $testFiles
            test_file_count = $testFiles.Count
            skip_count = 0
        }
        $projects.Add($project)
        $projectByName.Add($name, $project)
        $projectByPath.Add($relativePath, $project)
    }

    foreach ($project in $projects) {
        foreach ($dependencyName in $project.dependencies) {
            if (-not $projectByName.ContainsKey($dependencyName)) {
                throw "Frontend workspace '$($project.name)' references undiscovered workspace dependency '$dependencyName'."
            }
        }
        if ($project.test_file_count -gt 0 -and $null -eq $project.scripts.PSObject.Properties['test']) {
            throw "Frontend workspace '$($project.name)' contains unit tests but has no test script."
        }
        if ($null -eq $project.scripts.PSObject.Properties['typecheck']) {
            throw "Frontend workspace '$($project.name)' is not connected to the typecheck graph."
        }
        if ($project.path.StartsWith('apps/', [StringComparison]::Ordinal) -and $null -eq $project.scripts.PSObject.Properties['build']) {
            throw "Frontend app '$($project.name)' is not connected to the build graph."
        }
    }

    $ownedUnitTestPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $projects) {
        foreach ($testFile in $project.test_files) { [void]$ownedUnitTestPaths.Add([string]$testFile) }
    }
    foreach ($workspaceArea in $workspaceAreas) {
        foreach ($unitTestFile in Get-ChildItem -LiteralPath (Join-Path $resolvedFrontendRoot $workspaceArea) -File -Recurse |
                Where-Object { $_.Name -match '\.(?:test|spec)\.(?:[cm]?[jt]sx?)$' }) {
            $relativeUnitTestPath = [IO.Path]::GetRelativePath($resolvedFrontendRoot, $unitTestFile.FullName).Replace('\', '/')
            if (Test-NervFrontendIgnoredUnitTestPath -RelativePath $relativeUnitTestPath) { continue }
            if ($relativeUnitTestPath -match '/e2e(?:[-/])') { continue }
            if (-not $ownedUnitTestPaths.Contains($relativeUnitTestPath)) {
                throw "Frontend unit test '$relativeUnitTestPath' is not owned by a discovered workspace manifest src graph."
            }
        }
    }

    $allowlist = Get-Content -LiteralPath $SkipAllowlistPath -Raw | ConvertFrom-Json
    if ([int]$allowlist.version -ne 1) { throw 'Frontend test skip allowlist must use version 1.' }
    $allowlistByLocation = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($entry in @($allowlist.entries)) {
        $entryPath = [string]$entry.path
        $entryLine = [int]$entry.line
        $key = "$entryPath`:$entryLine"
        if ([string]::IsNullOrWhiteSpace($entryPath) -or $entryLine -le 0 -or
            [string]::IsNullOrWhiteSpace([string]$entry.owner) -or
            [string]::IsNullOrWhiteSpace([string]$entry.reason) -or
            [string]::IsNullOrWhiteSpace([string]$entry.expires)) {
            throw "Frontend test skip allowlist entry '$key' must include path, positive line, owner, reason, and expires."
        }
        $expiresText = [string]$entry.expires
        $expires = [DateOnly]::MinValue
        if ($expiresText -notmatch '^\d{4}-\d{2}-\d{2}$' -or
            -not [DateOnly]::TryParseExact(
                $expiresText,
                'yyyy-MM-dd',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None,
                [ref]$expires)) {
            throw "Frontend test skip allowlist entry '$key' expires must use ISO yyyy-MM-dd."
        }
        if ($expires -lt [DateOnly]::FromDateTime([DateTime]::UtcNow)) { throw "Frontend test skip allowlist entry '$key' is expired." }
        if ($allowlistByLocation.ContainsKey($key)) { throw "Duplicate frontend test skip allowlist entry '$key'." }
        $allowlistByLocation.Add($key, $entry)
    }

    $usedAllowlist = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $projects) {
        $skipCount = 0
        foreach ($relativeTestPath in $project.test_files) {
            $absoluteTestPath = Join-Path $resolvedFrontendRoot $relativeTestPath
            $source = [IO.File]::ReadAllText($absoluteTestPath)
            foreach ($importMatch in [regex]::Matches($source, '(?ms)\bimport\s*\{(?<bindings>.*?)\}\s*from\s*["'']vitest["'']')) {
                foreach ($binding in $importMatch.Groups['bindings'].Value.Split(',')) {
                    $bindingMatch = [regex]::Match($binding.Trim(), '^(?:type\s+)?(?<api>describe|suite|it|test)(?:\s+as\s+(?<alias>[A-Za-z_$][\w$]*))?$')
                    if ($bindingMatch.Success -and $bindingMatch.Groups['alias'].Success) {
                        throw "Aliasing Vitest test API '$($bindingMatch.Groups['api'].Value)' is forbidden in $relativeTestPath."
                    }
                }
            }
            if ($source -match '(?ms)\bimport\s*\*\s+as\s+[A-Za-z_$][\w$]*\s+from\s*["'']vitest["'']') {
                throw "Namespace Vitest imports are forbidden in $relativeTestPath because test skip governance requires named APIs."
            }
            if ($source -match '(?ms)\b(?:const|let|var)\s*\{[^}]*\}\s*=\s*(?:describe|suite|it|test)\b') {
                throw "Aliasing a Vitest test API through destructuring is forbidden in $relativeTestPath."
            }
            if ($source -match '(?m)\b(?:const|let|var)\s+[A-Za-z_$][\w$]*\s*=\s*(?:describe|suite|it|test)\b') {
                throw "Aliasing a Vitest test API through assignment is forbidden in $relativeTestPath."
            }
            # Scan the complete source rather than individual lines so formatting cannot
            # hide a focused/skipped call. Dot, optional-chain, and computed-property
            # spellings and Vitest concurrency/sequence chains are all governed. Renamed
            # or namespace imports are rejected above rather than becoming blind spots.
            $gap = '(?:\s|/\*.*?\*/|//[^\r\n]*(?:\r?\n|$))*'
            $chain = "(?:(?:\?\.|\.)$gap(?:concurrent|sequential|each|for)$gap)*"
            $modifierPattern = "(?ms)\b(?:describe|suite|it|test)$gap$chain(?:(?:\?\.|\.)$gap(?<modifier>only|skip|skipIf|runIf|todo)|(?:\?\.)?\[$gap['\x22](?<modifier>only|skip|skipIf|runIf|todo)['\x22]$gap\])"
            foreach ($match in [regex]::Matches($source, $modifierPattern)) {
                $lineNumber = 1 + ([regex]::Matches($source.Substring(0, $match.Index), '\n')).Count
                $modifier = [string]$match.Groups['modifier'].Value
                if ([string]::Equals($modifier, 'only', [StringComparison]::Ordinal)) {
                    throw "Committed test.only is forbidden at $relativeTestPath`:$lineNumber."
                }

                $skipCount++
                $key = "$relativeTestPath`:$lineNumber"
                if (-not $allowlistByLocation.ContainsKey($key)) {
                    throw "Committed test suppression '$modifier' requires an allowlist entry at $key."
                }
                [void]$usedAllowlist.Add($key)
            }
        }
        $project.skip_count = $skipCount
    }
    foreach ($key in $allowlistByLocation.Keys) {
        if (-not $usedAllowlist.Contains($key)) { throw "Stale frontend test skip allowlist entry '$key'." }
    }

    return [pscustomobject]@{
        schema_version = 1
        projects = $projects.ToArray()
        project_count = $projects.Count
        test_project_count = @($projects | Where-Object { $null -ne $_.scripts.PSObject.Properties['test'] }).Count
        test_file_count = (@($projects | ForEach-Object { $_.test_file_count }) | Measure-Object -Sum).Sum
        skip_count = (@($projects | ForEach-Object { $_.skip_count }) | Measure-Object -Sum).Sum
    }
}

function Get-NervFrontendWorkspacePlan {
    param(
        [Parameter(Mandatory)] [object] $Inventory,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ChangedPaths,
        [Parameter(Mandatory)] [bool] $FrontendImpacted,
        [Parameter(Mandatory)] [ValidateSet('Affected', 'Full')] [string] $Mode
    )

    $projectByName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $projectByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $dependents = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[string]]]::new([StringComparer]::Ordinal)
    foreach ($project in $Inventory.projects) {
        $projectByName.Add([string]$project.name, $project)
        $projectByPath.Add([string]$project.path, $project)
        $dependents.Add([string]$project.name, [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal))
    }
    foreach ($project in $Inventory.projects) {
        foreach ($dependencyName in $project.dependencies) { [void]$dependents[$dependencyName].Add([string]$project.name) }
    }

    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $selectionReason = 'no-frontend-impact'
    if ([string]::Equals($Mode, 'Full', [StringComparison]::Ordinal)) {
        foreach ($project in $Inventory.projects) { [void]$selected.Add([string]$project.name) }
        $selectionReason = 'main-full-workspace'
    }
    elseif ($FrontendImpacted) {
        $seedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $requiresFull = $false
        foreach ($rawPath in $ChangedPaths) {
            $path = $rawPath.Replace('\', '/')
            $matchedProject = $null
            foreach ($projectPath in $projectByPath.Keys) {
                if ($path.StartsWith("frontend/$projectPath/", [StringComparison]::Ordinal) -or
                    [string]::Equals($path, "frontend/$projectPath", [StringComparison]::Ordinal)) {
                    $matchedProject = $projectByPath[$projectPath]
                    break
                }
            }
            if ($null -ne $matchedProject) {
                [void]$seedNames.Add([string]$matchedProject.name)
                continue
            }
            if ($path.StartsWith('frontend/apps/', [StringComparison]::Ordinal) -or
                $path.StartsWith('frontend/packages/', [StringComparison]::Ordinal) -or
                $path.StartsWith('frontend/', [StringComparison]::Ordinal) -or
                $path.StartsWith('.github/workflows/', [StringComparison]::Ordinal) -or
                $path.StartsWith('scripts/get-frontend-workspace-plan.ps1', [StringComparison]::Ordinal) -or
                $path.StartsWith('scripts/lib/FrontendWorkspacePlan.ps1', [StringComparison]::Ordinal) -or
                $path.StartsWith('scripts/tests/frontend-workspace-plan.Tests.ps1', [StringComparison]::Ordinal)) {
                $requiresFull = $true
            }
        }
        if ($requiresFull -or $seedNames.Count -eq 0) {
            foreach ($project in $Inventory.projects) { [void]$selected.Add([string]$project.name) }
            $selectionReason = 'frontend-global-or-conservative-full'
        }
        else {
            function Add-Dependencies {
                param([Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Names)
                $queue = [Collections.Generic.Queue[string]]::new()
                foreach ($name in $Names) { $queue.Enqueue($name) }
                while ($queue.Count -gt 0) {
                    $name = $queue.Dequeue()
                    if (-not $selected.Add($name)) { continue }
                    foreach ($dependencyName in $projectByName[$name].dependencies) { $queue.Enqueue([string]$dependencyName) }
                }
            }

            Add-Dependencies -Names @($seedNames)
            $consumerNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $consumerQueue = [Collections.Generic.Queue[string]]::new()
            foreach ($seedName in $seedNames) { $consumerQueue.Enqueue($seedName) }
            while ($consumerQueue.Count -gt 0) {
                $name = $consumerQueue.Dequeue()
                foreach ($dependentName in $dependents[$name]) {
                    if ($consumerNames.Add($dependentName)) { $consumerQueue.Enqueue($dependentName) }
                }
            }
            Add-Dependencies -Names @($consumerNames)
            $selectionReason = 'affected-workspace-closure'
        }
    }

    $selectedProjects = @($Inventory.projects | Where-Object { $selected.Contains([string]$_.name) })
    $testProjects = @($selectedProjects | Where-Object { $null -ne $_.scripts.PSObject.Properties['test'] })
    $validationProjects = @($selectedProjects | Where-Object {
        $null -ne $_.scripts.PSObject.Properties['typecheck'] -or $null -ne $_.scripts.PSObject.Properties['build']
    })

    return [pscustomobject]@{
        schema_version = 1
        mode = $Mode.ToLowerInvariant()
        selected = $selectedProjects.Count -gt 0
        tests_selected = $testProjects.Count -gt 0
        selection_reason = $selectionReason
        projects = @($selectedProjects | ForEach-Object { $_.name })
        test_matrix = [pscustomobject]@{ include = @($testProjects | ForEach-Object { [pscustomobject]@{ name = $_.name; slug = $_.slug } }) }
        validation_matrix = [pscustomobject]@{ include = @($validationProjects | ForEach-Object {
            [pscustomobject]@{
                name = $_.name
                slug = $_.slug
                run_typecheck = $null -ne $_.scripts.PSObject.Properties['typecheck']
                run_build = $null -ne $_.scripts.PSObject.Properties['build']
            }
        }) }
    }
}
