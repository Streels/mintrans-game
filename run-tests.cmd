@echo off
rem Запустить все автотесты ядра.
chcp 65001 >nul
cd /d "%~dp0"
dotnet test Mintrans.slnx -nologo
pause
