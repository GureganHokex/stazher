#!/bin/zsh
set -e
cd -- "$(dirname -- "$0")"
if [[ -x .venv/bin/python ]]; then
  exec .venv/bin/python server.py
fi
if python3 -c 'import fastapi, uvicorn' >/dev/null 2>&1; then
  exec python3 server.py
fi
print 'Первый запуск: python3 -m venv .venv && .venv/bin/pip install -r requirements.txt'
read '?Нажми Enter, чтобы закрыть окно.'
