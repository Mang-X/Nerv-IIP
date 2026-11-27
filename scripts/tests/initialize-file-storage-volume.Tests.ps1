# Script-Governance:
#   Category: check
#   SideEffects:
#     - Exercises the FileStorage volume initializer on a temporary directory
#   Writes:
#     - One owned fixture directory
#   Cleanup:
#     - Removes the fixture in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot '../install/initialize-file-storage-volume.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) "nerv-file-storage-volume-$([guid]::NewGuid().ToString('N'))"
try {
    [void] [IO.Directory]::CreateDirectory($fixture)
    $marker = Join-Path $fixture '.filestorage-volume-v1'
    & $script -TusRoot $fixture -VolumeIdentity 'volume-A'
    if (-not [string]::Equals([IO.File]::ReadAllText($marker), 'volume-A', [StringComparison]::Ordinal)) {
        throw '初始化必须写入配置的卷身份。'
    }

    # 保留现有标记的换行，证明同身份重跑没有重写文件。
    $original = "volume-A`n"
    [IO.File]::WriteAllText($marker, $original)
    & $script -TusRoot $fixture -VolumeIdentity 'volume-A'
    if (-not [string]::Equals([IO.File]::ReadAllText($marker), $original, [StringComparison]::Ordinal)) {
        throw '同身份初始化不得改写标记。'
    }

    $rejected = $false
    try {
        & $script -TusRoot $fixture -VolumeIdentity 'volume-a'
    }
    catch {
        if (-not $_.Exception.Message.Contains('身份不匹配', [StringComparison]::Ordinal)) { throw }
        $rejected = $true
    }
    if (-not $rejected -or -not [string]::Equals([IO.File]::ReadAllText($marker), $original, [StringComparison]::Ordinal)) {
        throw '不同身份必须被拒绝，并保留原始标记。'
    }
    if ([IO.Directory]::GetFileSystemEntries($fixture).Length -ne 1) {
        throw '初始化不得残留临时标记。'
    }
}
finally {
    if ([IO.Directory]::Exists($fixture)) { [IO.Directory]::Delete($fixture, $true) }
}

Write-Host 'FileStorage volume initialization contracts passed.'
