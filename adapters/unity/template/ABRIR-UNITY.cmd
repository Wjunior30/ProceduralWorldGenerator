@echo off
setlocal
if not defined UNITY_EDITOR set "UNITY_EDITOR=C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe"
if not exist "%UNITY_EDITOR%" (
  echo Unity nao encontrada. Defina UNITY_EDITOR ou abra a pasta no Unity Hub.
  exit /b 1
)
start "" "%UNITY_EDITOR%" -force-d3d11 -projectPath "%~dp0" -executeMethod FrameworkPreviewLauncher.OpenPreview
