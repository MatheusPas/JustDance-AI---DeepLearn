@echo off
rem Instala o Python portatil do projeto (pasta PythonRuntime). Duplo clique para rodar.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_python_runtime.ps1" %*
echo.
pause
