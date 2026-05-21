# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and test commands only
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#   Cleanup:
#     - None required
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

dotnet restore backend/Nerv.IIP.sln
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests

Write-Host "Business master data realignment verified."
