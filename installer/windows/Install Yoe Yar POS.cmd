@echo off
rem Double-click to install or update Yoe Yar POS.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo Installation did not finish. Read the message above.
  pause
)
