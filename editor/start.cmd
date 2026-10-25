@echo off
setlocal
set "editorExe=%~dp0bin\Release\net8.0\win-x64\KwzStudio.exe"
if not exist "%editorExe%" (
  echo Build the editor first: powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1"
  pause
  exit /b 1
)
start "" "%editorExe%" %*
