<#
  Prepara IIS para IpsosPagoHonorarios: carpetas, grupo de aplicaciones, sitio y permisos.
  Se puede ejecutar más de una vez. Ejecutar como administrador, en el servidor:

      .\instalar-iis.ps1                       (D:\IpsosPagoHonorarios, puerto 8080)
      .\instalar-iis.ps1 -Raiz E:\Honorarios -Puerto 80
#>
param(
    [string]$Raiz = "D:\IpsosPagoHonorarios",
    [string]$Nombre = "IpsosPagoHonorarios",
    [int]$Puerto = 8080
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

$app = Join-Path $Raiz "app"
$archivos = Join-Path $Raiz "archivos"
$logs = Join-Path $app "logs"
New-Item -ItemType Directory $app, $archivos, $logs -Force | Out-Null

# Grupo de aplicaciones: sin código administrado, siempre activo y sin apagarse por inactividad
# (la aplicación ejecuta procesos en segundo plano: plazos de corrección y avisos).
if (-not (Test-Path "IIS:\AppPools\$Nombre")) { New-WebAppPool $Nombre | Out-Null }
Set-ItemProperty "IIS:\AppPools\$Nombre" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$Nombre" -Name startMode -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$Nombre" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

# Sitio
if (-not (Test-Path "IIS:\Sites\$Nombre")) {
    New-Website -Name $Nombre -PhysicalPath $app -ApplicationPool $Nombre -Port $Puerto | Out-Null
}
Set-ItemProperty "IIS:\Sites\$Nombre" -Name applicationDefaults.preloadEnabled -Value $true

# Permisos para la identidad del grupo de aplicaciones
$id = "IIS AppPool\$Nombre"
icacls $app /grant "${id}:(OI)(CI)RX" | Out-Null
icacls $logs /grant "${id}:(OI)(CI)M" | Out-Null
icacls $archivos /grant "${id}:(OI)(CI)M" | Out-Null
$secretos = Join-Path $app "appsettings.Production.json"
if (Test-Path $secretos) {
    icacls $secretos /inheritance:r /grant "Administrators:F" "${id}:R" | Out-Null
    Write-Host "Permisos de appsettings.Production.json ajustados (solo administradores y la aplicación)."
}
else {
    Write-Host "Falta crear $secretos. Después de crearlo, vuelve a ejecutar este script para protegerlo."
}

Write-Host ""
Write-Host "Listo. Sitio '$Nombre' en el puerto $Puerto, aplicación en $app, archivos en $archivos."
Write-Host "Siguiente: copiar la aplicación a $app, crear appsettings.Production.json y abrir http://localhost:$Puerto/Cuenta/Login"
