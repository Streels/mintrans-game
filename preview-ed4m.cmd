@echo off
rem Просмотр первого художественного ассета ЭД4М. Сборка C# не требуется.
chcp 65001 >nul
cd /d "%~dp0"
call tools\find-godot.cmd || (pause & exit /b 1)
"%GODOT%" --headless --editor --path game --import --quit || (pause & exit /b 1)
start "" "%GODOT%" --path game res://scenes/art/ed4m_preview.tscn
