@echo off
rem Build script for TokenDock (wrapper for build.ps1)
rem Usage: build.cmd [-RunTests]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
pause
