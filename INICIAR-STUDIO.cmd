@echo off
setlocal
set "ROOT=%~dp0"
set "STUDIO_URL=http://127.0.0.1:5179"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo O .NET nao foi encontrado. Instale o .NET 10 SDK para executar o Studio.
  pause
  exit /b 1
)

start "Procedural World Studio - servidor" /D "%ROOT%" cmd /k "dotnet run --project samples\ProceduralWorld.Studio\ProceduralWorld.Studio.csproj --urls %STUDIO_URL%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$u='%STUDIO_URL%'; for($i=0;$i -lt 60;$i++){try{Invoke-WebRequest -UseBasicParsing ($u+'/api/presets') -TimeoutSec 1 | Out-Null; Start-Process $u; exit 0}catch{Start-Sleep -Milliseconds 500}}; Write-Host 'O Studio nao iniciou dentro de 30 segundos.' -ForegroundColor Red; exit 1"

if errorlevel 1 pause
