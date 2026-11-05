@echo off
rem Roda um script de PythonTools com o Python do projeto. Exemplos:
rem   run_python.bat train.py --video meu_video.mp4 --name "Minha Danca"
rem   run_python.bat play.py
rem   run_python.bat bench.py --video video.mp4
set "ROOT=%~dp0.."
if not exist "%ROOT%\PythonRuntime\python.exe" (
  echo Python do projeto nao instalado. Rode Tools\setup_python_runtime.bat primeiro.
  exit /b 1
)
set PYTHONIOENCODING=utf-8
set PYTHONUTF8=1
set "SCRIPT=%~1"
shift
"%ROOT%\PythonRuntime\python.exe" "%ROOT%\PythonTools\%SCRIPT%" %1 %2 %3 %4 %5 %6 %7 %8 %9
