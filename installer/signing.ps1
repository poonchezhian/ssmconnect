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
  Optional Authenticode signing, shared by build.ps1 and build-installer.ps1 (dot-sourced).
  Nothing is signed unless a certificate is configured, so builds without one keep working (the result is simply unsigned).

  Configure ONE of these on the build PC (environment variables, never committed):
      SSMC_SIGN_THUMBPRINT     thumbprint of a code-signing certificate in the CurrentUser or LocalMachine "My" store
                               (this also covers hardware tokens and cloud-HSM certificates that appear in the store)
      SSMC_SIGN_PFX            path to a .pfx file          (+ SSMC_SIGN_PFX_PASSWORD)
  Optional:
      SSMC_SIGN_TIMESTAMP      RFC 3161 timestamp server (default http://timestamp.digicert.com). The timestamp keeps
                               signatures valid after the certificate expires, so leave it on.
#>
$script:SignCert = $null
$script:SignChecked = $false

function Get-SigningCert {
    if ($script:SignChecked) { return $script:SignCert }
    $script:SignChecked = $true
    $thumb = $env:SSMC_SIGN_THUMBPRINT
    $pfx = $env:SSMC_SIGN_PFX
    if ($thumb) {
        $thumb = ($thumb -replace '\s', '').ToUpper()
        $c = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $thumb } | Select-Object -First 1
        if (-not $c) { throw "SSMC_SIGN_THUMBPRINT is set but no certificate with that thumbprint is in the Personal store." }
        $script:SignCert = $c
    }
    elseif ($pfx) {
        if (-not (Test-Path -LiteralPath $pfx)) { throw "SSMC_SIGN_PFX points to a file that does not exist: $pfx" }
        $pw = $env:SSMC_SIGN_PFX_PASSWORD
        if (-not $pw) { throw 'SSMC_SIGN_PFX is set but SSMC_SIGN_PFX_PASSWORD is empty.' }
        $script:SignCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $pw)
    }
    if ($script:SignCert) {
        $eku = $script:SignCert.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }
        if (-not $eku) { throw 'The configured certificate is not a code-signing certificate.' }
        if ($script:SignCert.NotAfter -lt (Get-Date)) { throw 'The configured code-signing certificate has expired.' }
        Write-Host ("Signing with: " + $script:SignCert.Subject)
    }
    else { Write-Host 'Signing: no certificate configured (SSMC_SIGN_THUMBPRINT / SSMC_SIGN_PFX), the build will be UNSIGNED.' }
    return $script:SignCert
}

function Test-SigningEnabled { return [bool](Get-SigningCert) }

# Signs one file (.exe, .dll, .msi) and fails loudly if the result is not a signature. No-op when signing is not configured.
function Invoke-Sign([Parameter(Mandatory)] [string] $Path) {
    $cert = Get-SigningCert
    if (-not $cert) { return }
    $ts = if ($env:SSMC_SIGN_TIMESTAMP) { $env:SSMC_SIGN_TIMESTAMP } else { 'http://timestamp.digicert.com' }
    $r = Set-AuthenticodeSignature -FilePath $Path -Certificate $cert -TimestampServer $ts -HashAlgorithm SHA256
    if (-not $r.SignerCertificate -or $r.Status -in 'NotSigned', 'HashMismatch') {
        throw "Signing failed for $Path ($($r.Status)): $($r.StatusMessage)"
    }
    Write-Host ("  signed: " + (Split-Path $Path -Leaf))
}

# Burn bundles are signed in three steps: sign the engine, re-attach the container, sign the whole file (WiX insignia).
function Invoke-SignBundle([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $WixBin, [Parameter(Mandatory)] [string] $Work) {
    if (-not (Get-SigningCert)) { return }
    $engine = Join-Path $Work 'setup-engine.exe'
    & "$WixBin\insignia.exe" -nologo -ib $Path -o $engine
    if ($LASTEXITCODE) { throw 'insignia failed (detach engine)' }
    Invoke-Sign $engine
    & "$WixBin\insignia.exe" -nologo -ab $engine $Path -o $Path
    if ($LASTEXITCODE) { throw 'insignia failed (re-attach engine)' }
    Remove-Item $engine -Force -ErrorAction SilentlyContinue
    Invoke-Sign $Path
}

# Reports the signature state of the final file in the build log.
function Show-Signature([Parameter(Mandatory)] [string] $Path) {
    $s = Get-AuthenticodeSignature $Path
    if ($s.SignerCertificate) { Write-Host ("  signature: {0} ({1})" -f $s.Status, $s.SignerCertificate.Subject) }
    else { Write-Host '  signature: none (unsigned build)' }
}
