# Yükleyiciyi derler ve dist/ klasörüne GitHub sürümüne yüklenecek dosyaları hazırlar:
#   RobRunTurkceYama.exe, tr.json, SHA256SUMS.txt ve SHA256SUMS.txt.sig (dijital imza)
# Kullanım:  powershell -ExecutionPolicy Bypass -File build.ps1
#
# İmza için GİZLİ anahtar gerekir. Varsayılan yer: %USERPROFILE%\.robrun-turkce-yama\imza-anahtari-GIZLI.xml
# (başka yer için ROBRUN_IMZA_ANAHTARI ortam değişkeni). Gizli anahtar ASLA depoya konmaz ve paylaşılmaz.
# Anahtar yoksa dosyalar imzasız üretilir; programlar imzasız sürümü güncelleme olarak KABUL ETMEZ.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet build src/RobRunTurkce/RobRunTurkce.csproj -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız." }

New-Item -ItemType Directory -Force dist | Out-Null
Copy-Item src/RobRunTurkce/bin/Release/net48/RobRunTurkceYama.exe dist/ -Force
Copy-Item translations/tr.json dist/ -Force

$appVer = ([xml](Get-Content src/RobRunTurkce/RobRunTurkce.csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$trText = [IO.File]::ReadAllText((Resolve-Path translations/tr.json))
if ($trText -notmatch '"_bilgi"\s*:\s*\{[^}]*"surum"\s*:\s*"([^"]+)"') { throw "tr.json içinde _bilgi.surum bulunamadı." }
$trVer = $Matches[1]

$lines = @("# uygulama $appVer", "# ceviri $trVer")
foreach ($f in 'RobRunTurkceYama.exe', 'tr.json') {
    $h = (Get-FileHash "dist/$f" -Algorithm SHA256).Hash.ToLower()
    $lines += "$h  $f"
}
$sumsPath = Join-Path $PSScriptRoot 'dist/SHA256SUMS.txt'
[IO.File]::WriteAllText($sumsPath, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))

$keyPath = if ($env:ROBRUN_IMZA_ANAHTARI) { $env:ROBRUN_IMZA_ANAHTARI } else { Join-Path $env:USERPROFILE '.robrun-turkce-yama\imza-anahtari-GIZLI.xml' }
$sigPath = "$sumsPath.sig"
if (Test-Path $keyPath) {
    $rsa = New-Object System.Security.Cryptography.RSACng
    try {
        $rsa.FromXmlString([IO.File]::ReadAllText($keyPath))
        $sig = $rsa.SignData([IO.File]::ReadAllBytes($sumsPath), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        [IO.File]::WriteAllBytes($sigPath, $sig)
    } finally { $rsa.Dispose() }
    Write-Host "İmzalandı: dist/SHA256SUMS.txt.sig"
} else {
    if (Test-Path $sigPath) { Remove-Item $sigPath }
    Write-Warning "Gizli imza anahtarı bulunamadı ($keyPath). Sürüm imzasız; programlar bunu güncelleme olarak kabul etmez."
}

Write-Host ""
Get-Content $sumsPath | ForEach-Object { Write-Host $_ }
Write-Host ""
Write-Host "GitHub sürümüne yükleyin: dist/RobRunTurkceYama.exe, dist/tr.json, dist/SHA256SUMS.txt, dist/SHA256SUMS.txt.sig"
