@echo off
:: VSCode ターミナル内で install.ps1 をそのまま実行するためのラッパー
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
