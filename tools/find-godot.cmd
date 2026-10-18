@echo off
rem Находит Godot (.NET-версию) и записывает путь в переменную GODOT.
set "GODOT="
for /f "delims=" %%G in ('where Godot_v4*_mono_win64.exe 2^>nul') do if not defined GODOT set "GODOT=%%G"
if not defined GODOT for /r "%LOCALAPPDATA%\Microsoft\WinGet\Packages" %%G in (Godot_v4*_mono_win64.exe) do if not defined GODOT set "GODOT=%%G"
if not defined GODOT (
  echo Не найден Godot 4 .NET. Установите его командой:
  echo   winget install GodotEngine.GodotEngine.Mono
  exit /b 1
)
exit /b 0
