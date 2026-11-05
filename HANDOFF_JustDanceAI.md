# Just Dance AI — manual de continuação

> **Para o Claude Code que vai assumir:** este projeto já está em andamento. Leia este documento inteiro antes
> de mexer em qualquer coisa. Responda em português do Brasil. Primeiro rode a **checklist (seção 9)** e diga o
> que viu; só depois comece a seção 10. Não refaça o que já funciona (a seção 7 explica por quê).

Última atualização: 05/11/2026. O usuário (Matheus) é estudante de computação, já trabalha com C#/Unity e visão
computacional: pode ir direto aos detalhes técnicos.

**Preferência dele sobre git:** quando pedir um commit, **não** adicionar o Claude como coautor (nada de
`Co-Authored-By` nem link de sessão na mensagem). Nenhum commit foi feito ainda; o repositório tem muita coisa nova sem commitar.

---

## 1. O que o projeto é

Um jogo estilo **Just Dance** com câmera, feito com **Python + Unity**:

1. O usuário cria as próprias danças: envia um **vídeo que já tem a música e o(s) dançarino(s)**.
2. O sistema **separa a música da dança** e **captura os movimentos** (o "treinamento"), e salva com o nome escolhido
   numa **galeria**, com a **quantidade de pessoas** ao lado do nome.
3. Ao escolher a música: **contagem 3-2-1**; em cima o **vídeo de referência em negativo**; embaixo, num bloco, a **câmera do jogador**.
4. **Multijogador** de 1 até o nº de pessoas do vídeo; cada jogador é comparado ao dançarino correspondente e recebe pontuação.
5. **Desempenho foi um requisito explícito**: treino e jogo devem evitar gargalos. **O Python usado é o que fica dentro da pasta do projeto.**

## 2. Estado atual — o que existe e o que foi realmente verificado

| Parte | Estado | Verificação |
|---|---|---|
| Python: separar música/dança, capturar poses multipessoa, limpar, catalogar | Pronto | Testes automáticos (Linux, Python 3.11) com **detector falso** |
| Python: catalogação em paralelo (pedaços em processos) | Pronto | Teste: resultado **igual ao sequencial**. **Ganho de velocidade real não medido** (container de 2 núcleos) |
| Python: threads de tempo real (câmera, detector, vídeo) + `play.py` | Pronto | Partida completa com câmera falsa (notas ~97–98 imitando perfeito) |
| Python: `analysis_server.py` (sidecar p/ Unity) + protocolo binário | Pronto | Testes de ida e volta, orientações/espelho, modelo ausente |
| `bench.py`, `check_runtime.py` | Prontos | `--fake` funciona; **números reais só no PC do usuário** |
| Python portátil do projeto (`Tools/setup_python_runtime.ps1`) | Escrito | **Nunca executado** (o sandbox não baixa o interpretador) |
| Unity: menu, **Criar dança**, escolha de jogadores, **cena Game**, shader de negativo, placar, esqueleto | Escrito e commitado na pasta | **Só checagem de sintaxe. Nunca compilou na Unity, nunca rodou.** |
| **MediaPipe real**, download do modelo, webcam real, dança com gente real | — | **Nunca testados** |

Ao abrir a Unity pela primeira vez, **espere erros de compilação**: não havia compilador C# no ambiente onde isto foi escrito.
Corrija-os primeiro (veja `Logs/Editor.log`, procure `error CS`).

## 3. Onde estão as coisas

Pasta única do projeto: **`C:\Users\Matheus Games\JustDance AI - DeepLearn`** (repositório git, origin
`https://github.com/MatheusPas/JustDance-AI---DeepLearn.git`, branch `main`). A pasta antiga `...\JustDanceAI` está vazia e foi abandonada.

```
JustDance AI - DeepLearn/
  Assets/                 projeto Unity (6000.5.10f1, URP 17.5.0, uGUI 2.5.0, Input System 1.20.0 — SÓ o Input System novo)
    Editor/               MainMenuSceneBuilder, GameSceneBuilder, PythonSetupMenu
    Scripts/Core/         JustDancePaths, ProcessJob, PythonRunner, TrainingJob, SidecarClient
    Scripts/Game/         GameController, ChoreographyData (Choreography + Scoring), SkeletonGraphic, ScoreRow
    Scripts/Menu/         MainMenuController, CreateDancePanel, SongCard, SongLibrary, GameSession
    Shaders/UIInvert.shader
  PythonTools/            código Python (train.py, play.py, analysis_server.py, bench.py, ...) + tests/
  PythonRuntime/          Python 3.11 portátil + bibliotecas (NÃO vai pro git; o setup cria. Hoje há um resto parcial, o script refaz)
  GameData/               library.json, danças catalogadas, modelos de pose (NÃO vai pro git)
  Tools/                  setup_python_runtime.bat/.ps1, run_python.bat
```

`.gitignore` já tem `/PythonRuntime/`, `/GameData/`, `__pycache__/`, `*.pyc`. Na build final, copie `PythonRuntime/`, `PythonTools/` e `GameData/`
para ao lado do `.exe` (os caminhos são relativos a `Application.dataPath/..`). **Ainda não há passo de pós-build para isso.**

Documento do formato de dados também no Project "JustDance" do claude.ai: `claude/dance-data-format.md`.

## 4. Como rodar

**Instalar o Python do projeto (uma vez, precisa de internet):** duplo clique em `Tools\setup_python_runtime.bat`, ou na Unity
**JustDance ▸ Instalar Python do projeto**, ou pelo botão INSTALAR PYTHON da tela Criar dança. Baixa o Python 3.11.9 embutido
(python.org), instala o pip e `requirements-runtime.txt` (`mediapipe==1.0.1`, `opencv-contrib-python==4.13.0.92`, `imageio-ffmpeg==0.6.0`, `numpy==2.4.6`, `pygame==2.6.1`),
baixa os modelos de pose e faz um teste real do MediaPipe. `-Force` refaz do zero. Saída em linhas `JD|setup|...` / `JD|setup-error|...`.

**Terminal** (a partir da raiz do projeto): `Tools\run_python.bat train.py --video v.mp4 --name "Nome"`, `... play.py`, `... bench.py --video dance.mp4`,
`... check_runtime.py --smoke lite`, `... ..\PythonTools\tests\test_pipeline.py` (testes; 23, ~75 s).

**Unity:** abrir a pasta nova no Unity Hub. Menu **JustDance ▸ Build All (Menu + Game)** gera `Assets/Scenes/MainMenu.unity` e `Game.unity`
(e põe as duas nas Build Settings). Depois: Play na cena MainMenu. **Rodar o builder de novo sobrescreve as cenas.**

Opções do `train.py`: `--video --name --music --music-offset --start --end --max-people --workers (0=auto,1=sem paralelismo) --model lite|full|heavy --overwrite --machine --list --remove --rebuild-index`.
Com `--machine` a saída vira `JD|stage|`, `JD|progress|0.42`, `JD|warn|`, `JD|done|{json da entrada}`, `JD|error|` (é o que a Unity lê).

## 5. Arquitetura

```
CRIAR DANÇA (Unity) ── train.py (Python do projeto, outro processo) ──► GameData/dances/<slug>/{music.mp3, dance.mp4 (mudo), poses.json, thumbnail.jpg, meta.json} + library.json

JOGAR (Unity):
  Unity: WebCamTexture ──pixels──► [TCP 127.0.0.1] ──► analysis_server.py ──► MediaPipe (lite) ──► ângulos + pontos por jogador ──► Unity
  Unity: música = relógio mestre (AudioSettings.dspTime); VideoPlayer ajustado à música; frame da câmera carrega o instante da música (t_ms)
  Placar: ângulos do jogador vs poses.json[round(t_frame*fps)] → Scoring.TryScore
```

### Onde estavam os gargalos e o que foi feito
- **Catalogação:** (1) leitura do vídeo em thread própria, em paralelo à detecção; (2) frames reduzidos a ≤960 px antes de detectar (pontos são normalizados);
  (3) vídeo dividido em pedaços em **processos** (spawn), 1 s de sobreposição (aquecimento do rastreador), dançarinos ligados entre pedaços pela posição do quadril (<0,25);
  frames iniciais de cada pedaço pulados lendo em sequência (`grab`), **nunca por seek**; (4) modelo baixado antes da conversão (falha rápida); modelo `full` no treino.
- **Jogo:** nada espera por nada lento. Análise em **outro processo** (o Python não disputa CPU/GIL com a Unity); envio/recebimento em threads; **só o quadro mais recente**
  é analisado (se o analisador atrasa, quadros são pulados e o atraso não se acumula); modelo `lite`; a Unity só copia pixels (`GetPixels32`) quando o
  analisador está livre. No `play.py` (protótipo Python) a mesma ideia com threads: câmera, `PoseWorker` (caixa de 1 posição), `ReferenceStream` (vídeo já em negativo e redimensionado).
- A pontuação usa o **instante em que o quadro foi capturado**, não quando a detecção terminou: o atraso do detector não vira erro de nota. `ScoreTimeOffset` (0 por padrão, em `GameController`) compensa a latência da câmera.
- **F3** na partida (Unity) mostra fps da tela, análises/s, ms do detector, atraso e quadros enviados.

### Protocolo Unity ↔ servidor (binário, little-endian; documentado no topo de `sidecar_protocol.py`)
Cabeçalho 8 bytes: `"JD"`, tipo, 0, tamanho(uint32). Unity→servidor: CONFIG(1, JSON `{"players":n,"model":"lite"}`), FRAME(2: `seq u32, t_ms i32, w u16, h u16, canais u8, flags u8` + pixels RGB/RGBA;
flags: 1 = linhas de baixo para cima, 2 = espelhar), BYE(3), PING(4). Servidor→Unity: READY(0x81), RESULT(0x82: `seq, t_ms, detect_ms f32, n u8, 0` + n × `id u8, 8 ângulos f32, 13 pontos (x,y) f32`, NaN = ausente), LOG(0x83), PONG(0x84).
O servidor imprime `JD|ready|<porta>` ao subir (porta livre) e termina quando a Unity desconecta. Cliente de referência em Python: `sidecar_client.py` (`--spawn` testa com a webcam sem a Unity). **As duas pontas (`sidecar_protocol.py` e `SidecarClient.cs`) precisam andar juntas.**

## 6. Formato dos dados (contrato Python ↔ Unity)

Todos os caminhos de `library.json`/`meta.json` são **relativos a `GameData/`** (a Unity resolve com `JustDancePaths.ResolveGameData`).

Entrada: `format_version, name, slug, folder, audio_path, video_path, poses_path, thumbnail_path, fps, total_frames, duration_sec, num_people, movement_intensity, difficulty (Fácil/Médio/Difícil), music_source (video|attached), created_at`.

`poses.json` (colunar e achatado para o `JsonUtility`): `format_version, fps, frame_count, duration_sec, angle_names[8], point_indices[13]`, e `players[]` da esquerda para a direita
(`id, coverage, intensity, present[N], valid[N*8], angles[N*8], points[N*13*2]`). Tempo ↔ índice: `i = int(t*fps + 0.5)`. Ângulos: cotovelos, ombros, quadris, joelhos (E/D); pontos: nariz, ombros, cotovelos, pulsos, quadris, joelhos, tornozelos.

**Pontuação** (igual em `pose_utils.score_angles` e `Scoring.TryScore`): sobre os ângulos válidos dos dois lados, `max(0, 100·(1 − média_das_diferenças/70))`; com menos de 3 em comum não dá para comparar.
**Diferença entre as duas:** na Unity, jogador **não detectado** enquanto o dançarino está em cena conta **zero** (`ScoreRow.ReportMissing`); no `play.py` não conta nada. Alinhar se quiser.

**Unity ↔ jogador:** o jogador `id` do servidor (0,1,2… da esquerda para a direita no 1º quadro) é comparado ao dançarino de mesmo `id`. Com menos jogadores que dançarinos, seguem os dançarinos 0..n-1.

## 7. Decisões que NÃO devem ser desfeitas (e por quê)

1. **Converter para 30 fps com o filtro `fps=30`, nunca com `-r`** (o `-r` atrasava a imagem ~50 ms em relação à música; bug real achado pelos testes de alinhamento).
2. **`dance.mp4` é mudo e em fps constante; a música é um arquivo à parte e é o relógio mestre.** Poses são extraídas do `dance.mp4`, não do vídeo original.
3. **API Tasks do MediaPipe (`PoseLandmarker`), não `mp.solutions.pose`** (a antiga detecta só uma pessoa). Modo VIDEO exige timestamps estritamente crescentes.
4. **Ordem dos jogadores = esquerda → direita** (quadril). Ao dividir o treino em pedaços, a ligação entre pedaços é pela posição; **nunca** reordenar depois.
5. **A câmera é espelhada ANTES da detecção** (Unity: flag de espelho no FRAME; Python: `cv2.flip`). O MediaPipe rotula esquerda/direita pelo corpo que vê, então "mesmo lado da tela nos dois blocos" recebe o mesmo rótulo. Pressupõe dançarinos **de frente**.
6. **Cobertura < 25% descarta o dançarino**; `num_people` conta só os mantidos.
7. **Ângulo com ponto de baixa visibilidade (<0.5) é inválido, não zero**; a pontuação ignora inválidos.
8. **Catalogação atômica:** pasta temporária → só vira definitiva se tudo deu certo.
9. **Nunca usar seek para pular frames no treino** (pode errar frames e desalinhar poses e música); usar leitura sequencial com `grab()`.
10. **Só o quadro mais recente é analisado** (no Python e na Unity). Não colocar fila: o atraso cresceria sem limite.
11. **Análise de poses fora do processo da Unity** (Python do projeto). Migrar para ONNX/`com.unity.ai.inference` é possível mas é decisão em aberto (seção 10).
12. **Dificuldade** (limites 20 e 60 °/s) é chute, nunca calibrada com dança real. `format_version` ≠ 1 é ignorado.
13. Texto da UI: `UnityEngine.UI.Text` + `LegacyRuntime.ttf` (sem TextMeshPro, de propósito). Sem namespaces; tudo no Assembly-CSharp (e Assembly-CSharp-Editor em `Editor/`). `JsonUtility` para JSON.

## 8. Unity: o que existe

- **Menu** (`MainMenuController`): principal (com linha de status do Python) → Jogar → galeria → (se a dança tem >1 pessoa) escolha de jogadores → cena **`Game`**; **Criar dança** → `CreateDancePanel`; Opções = painel "em breve". `Esc` volta (não sai no meio do treino).
- **`CreateDancePanel`:** escolhe o vídeo (e música opcional) por uma janela do Windows aberta via PowerShell (`PythonRunner.PickFile`), campo de nome (se já existe, pede para apertar TREINAR de novo p/ substituir), barra de andamento, tempo decorrido, avisos, botão CANCELAR; se o Python do projeto não está instalado, mostra INSTALAR PYTHON. Ao terminar, a dança aparece na galeria.
- **`GameController`:** carrega a coreografia → sobe `analysis_server.py` → abre a webcam (640×480@30; `GameSession.CameraName` escolhe outra) → carrega a música (`UnityWebRequestMultimedia`) → prepara o `VideoPlayer` (modo APIOnly, sem áudio, `skipOnDrop`) → contagem 3-2-1 → partida. O vídeo é puxado para a música (desvio pequeno: ajusta `playbackSpeed`; >0,3 s: salta, no máx. 1×/2 s). Fim: tela com nota por jogador e JOGAR DE NOVO / MENU. Erros (sem câmera, sem Python, modelo ausente, analisador caiu) aparecem na mesma tela.
- **`SkeletonGraphic`** desenha os esqueletos por cima da câmera; **`ScoreRow`** é uma linha do placar (cor, nota média, "PERFEITO!/MUITO BEM/BOM/QUASE/TENTE DE NOVO").
- **`UIInvert.shader`** (`JustDance/UIInvert`): shader de UI com `lerp(cor, 1-cor, _Invert)`; o material `Assets/Materials/UIInvert.mat` é criado pelo `GameSceneBuilder` e fica no `RawImage` do vídeo de referência.
- **Editor:** `JustDance ▸ Build Main Menu Scene / Build Game Scene / Build All`, `Instalar Python do projeto (+ refazer do zero)`, `Verificar Python do projeto`, `Abrir pasta do projeto`.
- `SongLibrary.Load` procura: caminho informado → `GameData/library.json` → `StreamingAssets/library.json` → `persistentDataPath/library.json`; sem arquivo, 4 músicas de exemplo (clicar nelas só mostra um aviso).

## 9. Checklist de verificação (faça antes de continuar)

1. **Compilar a Unity** e corrigir `error CS` (nada foi compilado antes).
2. `Tools\setup_python_runtime.bat`: terminou com `JD|runtime|ok`? (`check_runtime.py --smoke lite` mede o MediaPipe real.) Falhas prováveis: rede/antivírus, `python311._pth`, versão do MediaPipe.
3. `Tools\run_python.bat bench.py --video <um dance.mp4>` e `bench.py --camera`: quantos fps de leitura, ms por quadro de cada modelo, ganho com 2/4 processos, a webcam entrega 30 fps? **Ajustar `LIVE_MODEL`, `--workers`, `--max-people` com esses números.**
4. `train.py` com um vídeo de dança real (corpo inteiro, de frente): quantas pessoas? avisos? dificuldade plausível?
5. JustDance ▸ Build All, Play: Criar dança (pelo menu), galeria, jogar. Conferir: negativo em cima, câmera embaixo, esqueleto alinhado com o corpo (se estiver de cabeça para baixo ou deslocado, ver `webcamBottomUp`/`uvRect` em `GameController.StartCamera`), notas sobem quando imita, **latência** (F3), sincronia vídeo×música.

## 10. Próximos passos

A) **Corrigir o que a verificação mostrar** (compilação, MediaPipe real, webcam, orientação da imagem, sincronia).
B) **Build final:** copiar `PythonRuntime/`, `PythonTools/`, `GameData/` ao lado do `.exe` (script de pós-build); decidir se o Python embutido vai junto na distribuição (o tamanho real só se sabe depois do setup rodar; veja o tamanho de `PythonRuntime/`).
C) **Opções:** escolher câmera (`GameSession.CameraName` já existe), volume, ajuste de latência (`ScoreTimeOffset`).
D) **Pontuação:** janelas de acerto/combos, tolerância a pequenos atrasos (janela deslizante em torno de `t`), escolher quem imita quem, calibrar a dificuldade.
E) **Menu:** capa com `thumbnail.jpg`, dificuldade e duração no card, remover dança pela galeria (`train.py --remove`), arte 2D de verdade no lugar dos placeholders.
F) **Longo prazo:** pose sem Python via ONNX + `com.unity.ai.inference` (o `.task` do MediaPipe não é ONNX; precisa outro modelo e pós-processamento).

## 11. Armadilhas já conhecidas

- `opencv-python` e `opencv-contrib-python` juntos quebram o `import cv2`. Usar só o `contrib` (fixado).
- O mount de pastas pelo ambiente de desenvolvimento é lento para muitos arquivos pequenos e mata processos em segundo plano; não instalar pacotes Python direto nele (por isso o setup roda no PC do usuário).
- Python embutido: o arquivo `python311._pth` manda no `sys.path`: precisa listar `Lib\site-packages`, `..\PythonTools` e `import site`; o script reescreve o arquivo assim. `pip` vem do `get-pip.py`.
- `mediapipe` importa `matplotlib` e outras dependências: não tente instalar "só o mínimo".
- Windows: a saída do Python redirecionada usa a página de código local; `train.py` e o servidor forçam UTF-8 (`PYTHONIOENCODING`/`PYTHONUTF8` também são definidos pelo `ProcessJob`).
- `cv2.imwrite` falha com caminhos não-ASCII no Windows; os `slug` são ASCII, mas a pasta do projeto tem espaços (`JustDance AI - DeepLearn`): sempre passar caminhos entre aspas (`ProcessJob.Quote`).
- A Unity só tem o Input System novo: `Input.GetKey...` dá erro (use `Keyboard.current`). O `InputField` legado funciona com ele.
- Hospedagens bloqueadas no ambiente de desenvolvimento (python.org, googleapis, nuget): downloads só acontecem no PC do usuário.
- O MP3 tem ~20–25 ms de atraso do codificador: irrelevante para a nota.

## 12. Histórico resumido

1. Arquitetura: Unity (câmera, UI, jogo) + Python/MediaPipe (pose); protótipo em Python, depois porte.
2. Protótipo de uma pessoa → **multipessoa** com `PlayerTracker`, contagem de pessoas na galeria e escolha de 1..N jogadores.
3. Dependências fixadas para Python 3.11; menu da Unity com placeholders gerados por código.
4. Catalogação refeita: música e dança separadas, captura limpa, catálogo por pasta, música anexável, recorte; `play.py` segue o relógio da música (achou e corrigiu o atraso do `-r`).
5. **Rodada de desempenho e integração (05/11/2026):** pasta nova do projeto; Python portátil dentro dele; catalogação com leitura em thread + processos em paralelo; tempo real com threads e quadro-mais-recente; servidor de análise por TCP para a Unity; `bench.py`; cena `Game`, tela Criar dança e escolha de jogadores na Unity.
