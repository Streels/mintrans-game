@echo off
rem Собрать и запустить игру. Параметры передаются игре, например:
rem   run-game.cmd --map=network_sandbox
rem   run-game.cmd --focus=city --zoom=2
chcp 65001 >nul
cd /d "%~dp0"
call tools\find-godot.cmd || (pause & exit /b 1)
echo Сборка...
dotnet build game\Mintrans.Game.csproj -v quiet -nologo || (echo Сборка не удалась. & pause & exit /b 1)
start "" "%GODOT%" --path game -- %*
