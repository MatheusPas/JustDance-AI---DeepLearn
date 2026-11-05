<#
  Instala o Python 3.11 PORTATIL dentro do projeto (pasta PythonRuntime) e as
  bibliotecas do jogo (MediaPipe, OpenCV, ffmpeg...). Nao mexe no Python do sistema.

  Uso:
    duplo clique em setup_python_runtime.bat
    ou, na Unity: menu JustDance > Instalar Python do projeto

  Opcoes:
    -Force            apaga PythonRuntime e instala do zero
    -Models lite,full modelos de pose a baixar (padrao: lite,full)
#>
param(
    [switch]$Force,
    [string[]]$Models = @('lite', 'full')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'      # sem isto o download no PowerShell 5 e MUITO mais lento
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'

$PythonVersion = '3.11.9'
$Root    = Split-Path -Parent $PSScriptRoot
$Runtime = Join-Path $Root 'PythonRuntime'
$Tools   = Join-Path $Root 'PythonTools'
$PyExe   = Join-Path $Runtime 'python.exe'
$Check   = Join-Path $Tools 'check_runtime.py'

function Step($text) { Write-Host "JD|setup|$text" }
function Fail($text) { Write-Host "JD|setup-error|$text"; exit 1 }

if (-not (Test-Path $Check)) { Fail "Nao encontrei PythonTools\check_runtime.py. Rode este script de dentro do projeto." }

# ---- ja esta instalado? ------------------------------------------------------
if ((-not $Force) -and (Test-Path $PyExe)) {
    Step "Python do projeto ja existe. Conferindo..."
    & $PyExe $Check --models @Models --smoke $Models[0]
    if ($LASTEXITCODE -eq 0) { Step "Tudo certo."; exit 0 }
    Step "A conferencia falhou. Reinstalando do zero."
}

try {
    # ---- 1) interpretador portatil ----------------------------------------------
    if (Test-Path $Runtime) { Remove-Item -Recurse -Force $Runtime }
    New-Item -ItemType Directory -Path $Runtime | Out-Null

    $zip = Join-Path $env:TEMP "python-$PythonVersion-embed-amd64.zip"
    $url = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
    Step "Baixando Python $PythonVersion portatil (~10 MB)..."
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $Runtime -Force
    Remove-Item $zip -Force

    # O arquivo ._pth manda no sys.path do Python portatil: liga o site-packages,
    # a pasta PythonTools (nossos scripts) e o modulo 'site' (necessario para o pip).
    $pth = Get-ChildItem -Path $Runtime -Filter 'python3*._pth' | Select-Object -First 1
    if (-not $pth) { Fail "Python portatil sem arquivo ._pth (versao inesperada)." }
    $stem = [IO.Path]::GetFileNameWithoutExtension($pth.Name)
    Set-Content -Path $pth.FullName -Encoding ASCII -Value @("$stem.zip", '.', 'Lib\site-packages', '..\PythonTools', 'import site')
    New-Item -ItemType Directory -Path (Join-Path $Runtime 'Lib\site-packages') -Force | Out-Null

    # ---- 2) pip --------------------------------------------------------------------
    Step "Instalando o pip..."
    $getPip = Join-Path $Runtime 'get-pip.py'
    Invoke-WebRequest -Uri 'https://bootstrap.pypa.io/get-pip.py' -OutFile $getPip -UseBasicParsing
    & $PyExe $getPip --no-warn-script-location
    if ($LASTEXITCODE -ne 0) { Fail "Nao consegui instalar o pip." }
    Remove-Item $getPip -Force

    # ---- 3) bibliotecas ------------------------------------------------------------
    Step "Instalando bibliotecas (MediaPipe, OpenCV, ffmpeg...). Pode levar alguns minutos."
    & $PyExe -m pip install --no-warn-script-location --disable-pip-version-check -r (Join-Path $Tools 'requirements-runtime.txt')
    if ($LASTEXITCODE -ne 0) { Fail "Falha ao instalar as bibliotecas (veja as mensagens acima)." }

    # ---- 4) modelos de pose + teste real do MediaPipe -------------------------------
    Step "Baixando modelos de pose e testando o MediaPipe..."
    & $PyExe $Check --models @Models --smoke $Models[0]
    if ($LASTEXITCODE -ne 0) { Fail "As bibliotecas foram instaladas, mas a verificacao falhou (veja as linhas JD|check acima)." }

    Step "Pronto! Python do projeto instalado em: $Runtime"
    exit 0
}
catch {
    Fail $_.Exception.Message
}
