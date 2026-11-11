@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Microphone-Patch.ps1" -Mode Restore
pause
