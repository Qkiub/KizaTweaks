@echo off
chcp 65001 >nul
title KizaTweaks - compilar
cd /d "%~dp0"
echo.
echo  ===== KizaTweaks by Qkiub: generar KizaTweaks.exe =====
echo.

rem 1) .NET 8 SDK (o superior). Se instala con winget si falta.
set "PATH=%PATH%;%ProgramFiles%\dotnet"
dotnet --list-sdks 2>nul | findstr /r /c:"^[89]\." /c:"^1[0-9]\." >nul
if errorlevel 1 (
  echo Instalando .NET 8 SDK, puede tardar unos minutos...
  winget install -e --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements --silent
  set "PATH=%PATH%;%ProgramFiles%\dotnet"
  dotnet --list-sdks 2>nul | findstr /r /c:"^[89]\." /c:"^1[0-9]\." >nul
  if errorlevel 1 (
    echo.
    echo No se pudo instalar .NET. Descargalo de https://dotnet.microsoft.com/download/dotnet/8.0 ^(SDK x64^) y vuelve a ejecutar este archivo.
    pause
    exit /b 1
  )
)

rem 2) Medidor de FPS que va incluido dentro del .exe
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\get-presentmon.ps1"

rem 3) Pruebas y compilacion
echo.
echo Ejecutando pruebas...
dotnet run --project tests\KizaTweaks.Tests -c Release
if errorlevel 1 (
  echo.
  echo Las pruebas fallaron. Revisa los mensajes de arriba.
  pause
  exit /b 1
)
echo.
echo Compilando KizaTweaks.exe...
dotnet publish src\KizaTweaks.App\KizaTweaks.App.csproj -c Release -o publish
if errorlevel 1 (
  echo.
  echo Error al compilar. Copia los mensajes de arriba y envialos.
  pause
  exit /b 1
)
echo.
echo  ===== Listo: publish\KizaTweaks.exe =====
explorer publish
pause
