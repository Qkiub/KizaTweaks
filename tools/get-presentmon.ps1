# Descarga el medidor de FPS (PresentMon, licencia MIT) para incluirlo dentro de KizaTweaks.exe.
# Lo usan compilar.bat y el flujo de GitHub Actions. No hace nada si ya está descargado.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$dest = Join-Path $PSScriptRoot '..\src\KizaTweaks.App\Tools\PresentMon.exe'
if (Test-Path $dest) { Write-Host "Medidor de FPS ya presente."; exit 0 }
New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
$headers = @{ 'User-Agent' = 'KizaTweaks-build' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
try {
    $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/GameTechDev/PresentMon/releases/latest' -Headers $headers
    $asset = $rel.assets | Where-Object { $_.name -match '^PresentMon-[\d.]+-x64\.exe$' } | Select-Object -First 1
    if (-not $asset) { throw "No se encontró el ejecutable x64 en la última versión." }
    Write-Host "Descargando $($asset.name)..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $dest -Headers $headers
    Write-Host "Medidor de FPS listo."
}
catch {
    # Sin internet o sin acceso a GitHub: se compila igual y la app ofrecerá descargarlo la primera vez.
    Write-Warning "No se pudo descargar el medidor de FPS: $($_.Exception.Message)"
    if (Test-Path $dest) { Remove-Item $dest -Force }
}
exit 0
