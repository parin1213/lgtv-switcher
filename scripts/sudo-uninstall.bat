@echo off
:: VSCode ターミナル内で uninstall.ps1 をそのまま実行するためのラッパー
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
