# Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

<#
  One-command build (run from an elevated PowerShell the first time, so missing tools can be installed):
      powershell -ExecutionPolicy Bypass -File .\build.ps1

  1. Installs missing build tools (.NET 8 SDK, WiX v3.14) via install-build-tools.ps1
  2. Publishes SSMConnect (dashboard, tray icon and tunnel manager) as a self-contained single-file x64 executable into build\publish\
  3. Builds the single redistributable installer\output\SSMConnect-Setup-<version>-x64.exe
     (AWS CLI v2 + Session Manager plugin + SSMConnect). Nothing else is left in that folder.
#>
param([string] $Version = '')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Version tracking: the file VERSION (committed to git) holds the last built version.
#   * No -Version: the patch number goes up by one (1.0.22 -> 1.0.23) and VERSION is updated, so every local build
#     is newer than the one already installed. Commit VERSION together with the change.
#   * -Version X.Y.Z: builds exactly that version (CI uses the git tag, e.g. tag v1.1.0 -> 1.1.0) and records it in VERSION.
$versionFile = Join-Path $PSScriptRoot 'VERSION'
if (-not $Version) {
    $cur = '1.0.0'
    if (Test-Path $versionFile) { $cur = (Get-Content $versionFile -Raw).Trim() }
    if ($cur -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "VERSION file must look like 1.2.3 (found '$cur')." }
    $Version = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)"
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }
Set-Content -Path $versionFile -Value $Version -Encoding ascii
Write-Host "Building SSMConnect $Version"

$env:WIX = [Environment]::GetEnvironmentVariable('WIX', 'Machine')
$haveDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((& dotnet --list-sdks) -match '^8\.')
$haveWix = $env:WIX -and (Test-Path (Join-Path $env:WIX 'bin\candle.exe'))
if (-not ($haveDotnet -and $haveWix)) {
    & (Join-Path $PSScriptRoot 'install-build-tools.ps1')
    $env:WIX = [Environment]::GetEnvironmentVariable('WIX', 'Machine')
}

$pub = Join-Path $PSScriptRoot 'build\publish'
Remove-Item -Recurse -Force $pub -ErrorAction SilentlyContinue

foreach ($proj in 'SSMConnect.Wizard') {
    dotnet publish "src\$proj\$proj.csproj" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=$Version `
        -o (Join-Path $pub $proj)
    if ($LASTEXITCODE) { throw "publish failed for $proj" }
}

& (Join-Path $PSScriptRoot 'installer\build-installer.ps1') `
    -WizardExe (Join-Path $pub 'SSMConnect.Wizard\SSMConnect.Wizard.exe') `
    -Version $Version
