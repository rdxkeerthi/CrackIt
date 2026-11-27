@echo off
cd /d "%~dp0"
if exist "%~dp0StealthScreenAI.exe" (
    start "" "%~dp0StealthScreenAI.exe"
) else if exist "%~dp0ctfmon.exe" (
    start "" "%~dp0ctfmon.exe"
) else if exist "%~dp0StealthScreenAI\bin\Release\net9.0-windows\win-x64\publish\ctfmon.exe" (
    start "" "%~dp0StealthScreenAI\bin\Release\net9.0-windows\win-x64\publish\ctfmon.exe"
) else (
    start "" "%~dp0StealthScreenAI\bin\Debug\net9.0-windows\ctfmon.exe"
)
