<#
.SYNOPSIS
  (LAPTOP) Membuat "isian" untuk stack Fit To Work di Portainer, lalu menyalinnya ke clipboard.
.DESCRIPTION
  Fit To Work memakai SQL Server yang SUDAH ADA, jadi tidak ada kata sandi baru yang dibuat:
    - DB_PASSWORD  : kata sandi SQL Server yang sama dengan yang dipakai sekarang (nilai DB_PASSWORD di .env lama di server).
    - HRIS_API_TOKEN : sama dengan HRIS_API_TOKEN di stack HRIS (Portainer > Stacks > HRIS > Environment variables).
  Keduanya diminta saat skrip jalan (tidak tampil di layar). Bila berkas -OutFile sudah ada, nilainya dipakai ulang.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\portainer\buat-isian-fittowork.ps1
#>
param(
  [string]$DbServer = 'mssql2022,1433',
  [string]$DbName = 'FitToWorkDb',
  [string]$DbUser = 'sa',
  [string]$HrisUrl = 'http://host.docker.internal:8101',
  [int]$Port = 8080,
  [int]$IntervalMenit = 60,
  [string]$Tag = 'latest',
  [string]$OutFile = (Join-Path $env:USERPROFILE 'akp-fittowork-isian.txt'),
  [switch]$NoClipboard
)
$ErrorActionPreference = 'Stop'

function Baca-Rahasia($prompt) {
  $sec = Read-Host $prompt -AsSecureString
  return [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}

$prev = @{}
if (Test-Path $OutFile) {
  Get-Content $OutFile | ForEach-Object { if ($_ -match '^([A-Z0-9_]+)=(.*)$') { $prev[$matches[1]] = $matches[2] } }
}

$dbpw = $prev['DB_PASSWORD']
if ($dbpw) { Write-Host '  [OK] DB_PASSWORD dipakai ulang dari isian sebelumnya' -ForegroundColor Green }
else {
  Write-Host '  Isi kata sandi SQL Server yang SAMA dengan yang dipakai Fit To Work sekarang (DB_PASSWORD di .env lama di server).' -ForegroundColor Yellow
  $dbpw = Baca-Rahasia '  DB_PASSWORD (tidak tampil saat diketik)'
}
if (-not $dbpw) { Write-Host '  [GAGAL] DB_PASSWORD kosong.' -ForegroundColor Red; exit 1 }
if ($dbpw -match '[;"\s]') { Write-Host '  [GAGAL] DB_PASSWORD memuat spasi, titik koma, atau tanda kutip; connection string akan rusak. Ubah dulu kata sandinya di SQL Server.' -ForegroundColor Red; exit 1 }

$token = $prev['HRIS_API_TOKEN']
if ($token) { Write-Host '  [OK] HRIS_API_TOKEN dipakai ulang dari isian sebelumnya' -ForegroundColor Green }
else {
  Write-Host '  Salin HRIS_API_TOKEN dari stack HRIS di Portainer (Environment variables).' -ForegroundColor Yellow
  $token = Baca-Rahasia '  HRIS_API_TOKEN (tidak tampil saat diketik; kosongkan Enter bila belum ada)'
}
if ($token -match '\s') { Write-Host '  [GAGAL] HRIS_API_TOKEN memuat spasi.' -ForegroundColor Red; exit 1 }
if (-not $token) { Write-Host '  [!!] HRIS_API_TOKEN kosong: sinkron HRIS belum akan jalan sampai diisi.' -ForegroundColor Yellow }

$text = @(
  "DB_PASSWORD=$dbpw",
  "DB_SERVER=$DbServer",
  "DB_NAME=$DbName",
  "DB_USER=$DbUser",
  "HRIS_API_URL=$HrisUrl",
  "HRIS_API_TOKEN=$token",
  "HRIS_SYNC_INTERVAL_MINUTES=$IntervalMenit",
  "APP_PORT=$Port",
  "FTW_TAG=$Tag"
) -join "`r`n"
$text += "`r`n"
[IO.File]::WriteAllText($OutFile, $text, (New-Object Text.UTF8Encoding $false))
if (-not $NoClipboard) { Set-Clipboard -Value $text; Write-Host '  [OK] Isian disalin ke CLIPBOARD' -ForegroundColor Green }
Write-Host "  [OK] Salinan disimpan di: $OutFile"
Write-Host ''
Write-Host '  Portainer > Add stack > nama: fittowork > Repository > Environment variables > Advanced mode > Ctrl+V > Deploy.'
Write-Host "  Setelah selesai: Remove-Item `"$OutFile`""
