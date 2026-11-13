@echo off
cd /d "%~dp0"
if exist .venv\Scripts\python.exe (
  .venv\Scripts\python.exe server.py
) else (
  py -3 server.py
)
pause
