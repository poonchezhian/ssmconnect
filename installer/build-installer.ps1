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
  Builds, with WiX Toolset v3:
    installer\output\SSMConnect-Setup-<version>-x64.exe  the ONLY file to redistribute: AWS CLI v2 + Session Manager plugin + SSMConnect
  Normally called by ..\build.ps1, which publishes the two executables first.
  The AWS installers are downloaded once into installer\prereqs\ and their Authenticode signatures are checked.
#>
param(
    [Parameter(Mandatory)] [string] $WizardExe,
    [string] $IconFile = (Join-Path $PSScriptRoot '..\src\SSMConnect.Wizard\Assets\ssmconnect.ico'),
    [switch] $UpdatePrereqs,
    [string] $Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'

if (-not $env:WIX) { $env:WIX = [Environment]::GetEnvironmentVariable('WIX', 'Machine') }
if (-not $env:WIX) { throw 'WiX Toolset v3 not found. Run ..\install-build-tools.ps1 first.' }
$bin = Join-Path $env:WIX 'bin'
$here = $PSScriptRoot
$obj = Join-Path $here 'obj'
$out = Join-Path $here 'output'
$pre = Join-Path $here 'prereqs'
New-Item -ItemType Directory -Force -Path $obj, $out, $pre | Out-Null

# ---- 1. LICENSE -> RTF for the licence page (single source of truth) ----
$licenseRtf = Join-Path $obj 'License.rtf'
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Calibri;}}\viewkind4\uc1\pard\f0\fs20 ')
foreach ($line in (Get-Content -LiteralPath (Join-Path $here '..\LICENSE'))) {
    foreach ($ch in $line.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '\' -or $ch -eq '{' -or $ch -eq '}') { [void]$sb.Append('\').Append($ch) }
        elseif ($code -gt 127) { [void]$sb.Append("\u$code?") }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('\par ')
}
[void]$sb.Append('}')
[System.IO.File]::WriteAllText($licenseRtf, $sb.ToString(), [System.Text.Encoding]::ASCII)

# ---- 2. MSI ----
# The MSI is only an intermediate: it is embedded in the setup EXE and is not shipped on its own.
$msi = Join-Path $obj "SSMConnect-$Version-x64.msi"
& "$bin\candle.exe" -nologo -arch x64 -ext WixUtilExtension `
    "-dVersion=$Version" "-dWizardExe=$WizardExe" "-dLicenseRtf=$licenseRtf" "-dIconFile=$IconFile" `
    -out "$obj\" (Join-Path $here 'Product.wxs')
if ($LASTEXITCODE) { throw 'candle failed (Product.wxs)' }
& "$bin\light.exe" -nologo -sw1076 -ext WixUIExtension -ext WixUtilExtension -cultures:en-us -pdbout "$obj\SSMConnect.msi.wixpdb" -out $msi "$obj\Product.wixobj"
if ($LASTEXITCODE) { throw 'light failed (MSI)' }

# ---- 3. Prerequisites (downloaded once, signature-checked) ----
function Get-Prereq([string] $Url, [string] $Name) {
    $path = Join-Path $pre $Name
    if (-not (Test-Path $path)) {
        Write-Host "Downloading $Name ..."
        Invoke-WebRequest -Uri $Url -OutFile $path -UseBasicParsing
    }
    $sig = Get-AuthenticodeSignature $path
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Amazon') {
        Remove-Item $path -Force
        throw "$Name failed the Authenticode check (status: $($sig.Status)). Deleted; re-run to download again."
    }
    # Trust-on-first-use pin: the first build records each file's SHA-256 in prereqs.lock.json (commit it).
    # Later builds fail if the download changed, so a build never silently picks up a different installer.
    $lockFile = Join-Path $here 'prereqs.lock.json'
    $lock = @{}
    if (Test-Path $lockFile) { (Get-Content $lockFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $lock[$_.Name] = $_.Value } }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($lock.ContainsKey($Name) -and $lock[$Name] -ne $hash -and -not $UpdatePrereqs) {
        Remove-Item $path -Force
        throw "$Name differs from the pinned version in prereqs.lock.json. If AWS published a new release and you trust it, re-run with -UpdatePrereqs."
    }
    if (-not $lock.ContainsKey($Name) -or $UpdatePrereqs) {
        $lock[$Name] = $hash
        $lock | ConvertTo-Json | Set-Content $lockFile -Encoding UTF8
    }
    return $path
}
$awsMsi = Get-Prereq 'https://awscli.amazonaws.com/AWSCLIV2.msi' 'AWSCLIV2.msi'
$pluginExe = Get-Prereq 'https://s3.amazonaws.com/session-manager-downloads/plugin/latest/windows/SessionManagerPluginSetup.exe' 'SessionManagerPluginSetup.exe'

# ---- 4. Setup window (managed bootstrapper application, .NET Framework 4.8 which ships with Windows 10/11) ----
$baOut = Join-Path $obj 'ba'
if (Test-Path $baOut) { Remove-Item $baOut -Recurse -Force }
& dotnet build (Join-Path $here 'BA\SSMConnect.BA.csproj') -c Release -o $baOut "-p:Version=$Version" "-p:WixSdkDir=$env:WIX\SDK" --nologo -v:q
if ($LASTEXITCODE) { throw 'building the setup window (installer\BA) failed' }
$baDll = Join-Path $baOut 'SSMConnect.BA.dll'
$baConfig = Join-Path $here 'BA\BootstrapperCore.config'

# ---- 5. One-click bundle ----
$setup = Join-Path $out "SSMConnect-Setup-$Version-x64.exe"
& "$bin\candle.exe" -nologo -arch x64 -ext WixBalExtension -ext WixUtilExtension `
    "-dVersion=$Version" "-dIconFile=$IconFile" "-dBaDll=$baDll" "-dBaConfig=$baConfig" "-dLicenseRtf=$licenseRtf" "-dAwsCliMsi=$awsMsi" "-dPluginExe=$pluginExe" "-dSsmMsi=$msi" `
    -out "$obj\" (Join-Path $here 'Bundle.wxs')
if ($LASTEXITCODE) { throw 'candle failed (Bundle.wxs)' }
& "$bin\light.exe" -nologo -ext WixBalExtension -ext WixUtilExtension -pdbout "$obj\SSMConnect.setup.wixpdb" -out $setup "$obj\Bundle.wixobj"
if ($LASTEXITCODE) { throw 'light failed (bundle)' }

# Remove leftovers from older builds so installer\output holds exactly one file to redistribute
Get-ChildItem $out -File | Where-Object { $_.FullName -ne $setup } | Remove-Item -Force

foreach ($f in $setup) {
    Write-Host ''
    Write-Host $f
    Write-Host ('  SHA256: ' + (Get-FileHash $f -Algorithm SHA256).Hash)
}
