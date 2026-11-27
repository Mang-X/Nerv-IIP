# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Initializes the identity of an operator-verified mounted FileStorage tus root
#   Writes:
#     - .filestorage-volume-v1 and an owned temporary marker in the supplied root
#   Cleanup:
#     - Removes only the temporary marker created by this invocation
#   Requires:
#     - PowerShell 7
#     - Existing mounted tus root writable by the operator

<#
.SYNOPSIS
在已确认挂载的 tus 根目录初始化 FileStorage 卷标记。
.PARAMETER TusRoot
已存在的 tus 根目录绝对路径。部署人员负责核对挂载与目录权限。
.PARAMETER VolumeIdentity
生产部署配置使用的卷标识。默认读取 NERV_IIP_FILE_STORAGE_VOLUME_IDENTITY。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $TusRoot,
    [string] $VolumeIdentity = $env:NERV_IIP_FILE_STORAGE_VOLUME_IDENTITY
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/ScriptAutomation.ps1')

if ([string]::IsNullOrWhiteSpace($VolumeIdentity) -or
    -not [string]::Equals($VolumeIdentity, $VolumeIdentity.Trim(), [StringComparison]::Ordinal)) {
    throw '卷标识必填，且首尾不得包含空白。'
}
if (-not [IO.Path]::IsPathFullyQualified($TusRoot) -or -not [IO.Directory]::Exists($TusRoot)) {
    throw 'TusRoot 必须是已存在的挂载目录绝对路径。'
}

$marker = Join-Path $TusRoot '.filestorage-volume-v1'
$existing = Get-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
if ($null -eq $existing) {
    $temporaryMarker = Join-Path $TusRoot ".filestorage-volume-v1.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporaryMarker, $VolumeIdentity)
        try {
            [IO.File]::Move($temporaryMarker, $marker)
        }
        catch [IO.IOException] {
            # 并发初始化时验证先落盘的标记；其他 I/O 错误继续上报。
            if ($null -eq (Get-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue)) { throw }
        }
    }
    finally {
        [IO.File]::Delete($temporaryMarker)
    }
    $existing = Get-Item -LiteralPath $marker -Force
}

if ($existing.PSIsContainer -or $existing.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or
    -not [string]::Equals([IO.File]::ReadAllText($marker).Trim(), $VolumeIdentity, [StringComparison]::Ordinal)) {
    throw '现有 FileStorage 卷标记无效或身份不匹配。'
}
Write-Diagnostic 'FileStorage 卷标记校验通过。'
