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
  Installs everything needed to BUILD SSMConnect on a Windows PC:
    - .NET 8 SDK
    - WiX Toolset v3.14 (compiler for the MSI and the setup bundle)
  Run from an elevated PowerShell:   powershell -ExecutionPolicy Bypass -File .\install-build-tools.ps1
  build.ps1 calls this automatically when something is missing.
#>
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script from an elevated (Run as administrator) PowerShell window.' }

function Refresh-Env {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:WIX = [Environment]::GetEnvironmentVariable('WIX', 'Machine')
}
Refresh-Env

# ---- .NET 8 SDK ----
$hasSdk8 = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((& dotnet --list-sdks) -match '^8\.')
if ($hasSdk8) { Write-Host '.NET 8 SDK: already installed' }
else {
    Write-Host 'Installing .NET 8 SDK ...'
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements
    } else {
        $s = Join-Path $env:TEMP 'dotnet-install.ps1'
        Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $s
        & $s -Channel 8.0 -InstallDir "$env:ProgramFiles\dotnet"
    }
}

# ---- WiX v3.14 ----
if ($env:WIX -and (Test-Path (Join-Path $env:WIX 'bin\candle.exe'))) { Write-Host 'WiX Toolset v3: already installed' }
else {
    Write-Host 'Installing WiX Toolset v3.14 ...'
    $wixExe = Join-Path $env:TEMP 'wix314.exe'
    Invoke-WebRequest 'https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314.exe' -OutFile $wixExe
    $sig = Get-AuthenticodeSignature $wixExe
    if ($sig.Status -ne 'Valid') { throw "WiX installer signature is not valid ($($sig.Status)); aborting." }
    # wix314.exe is a Burn bundle that needs .NET Framework 3.5 (Windows enables it on demand)
    Start-Process $wixExe -ArgumentList '/install', '/quiet', '/norestart' -Wait
}

Refresh-Env
Write-Host ''
Write-Host ('dotnet : ' + (& dotnet --version))
Write-Host ('WIX    : ' + $env:WIX)
if (-not $env:WIX) { Write-Warning 'WIX variable not visible yet - open a NEW PowerShell window before running build.ps1.' }
