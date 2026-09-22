@echo off
cd /d "%~dp0"
dotnet build -c Release -nologo -v q > build.log 2>&1 || exit /b 1
bin\Release\net9.0\CpuArena.exe %* > "%ARENA_OUT%" 2>&1
