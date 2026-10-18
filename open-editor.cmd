@echo off
rem Открыть проект в редакторе Godot. Запуск игры из редактора — клавиша F5.
chcp 65001 >nul
cd /d "%~dp0"
call tools\find-godot.cmd || (pause & exit /b 1)
start "" "%GODOT%" --editor --path game
