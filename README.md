# Just Dance AI — DeepLearn

Jogo de dança controlado pela câmera: o jogador imita o dançarino do vídeo e recebe pontos de
acordo com o quanto a postura dele se parece com a do dançarino. Qualquer vídeo de dança vira
uma fase nova pela tela **Criar dança**, com 1 ou mais dançarinos (multijogador).

Trabalho C2 de **Visão Computacional** — FAESA, 2026/2.
Equipe: Matheus Alves Passos e Lucas Paton Pedroso Lima.

**Vídeo de demonstração:** _(adicionar o link do YouTube não listado ou do Google Drive)_

## Como a visão computacional é usada

| Parte | Como funciona |
|---|---|
| **Modelo** | MediaPipe **Pose Landmarker** (API Tasks): 33 pontos do corpo por pessoa, várias pessoas por quadro. Modelo `full` para catalogar danças e `lite` durante o jogo. |
| **Catalogar uma dança** | O vídeo é separado em música e imagem; as poses de cada dançarino são extraídas quadro a quadro, ordenadas da esquerda para a direita e suavizadas (`PythonTools/train.py`). |
| **Regra de interação** | A cada quadro, 8 ângulos do jogador (cotovelos, ombros, quadris e joelhos) são comparados com os do dançarino no mesmo instante da música: `nota = 100 · (1 − diferença média / 70°)`. |
| **Estado ao longo do tempo** | Nota média da música por jogador, avaliação do momento (**PERFEITO! / MUITO BEM / BOM / QUASE / TENTE DE NOVO**), barra de progresso e tela final com o resultado. |
| **Resposta visual** | Esqueleto do jogador desenhado sobre a câmera, cartão de cada jogador com cor, nota e avaliação animada. |
| **Sem detecção** | Se o jogador some da câmera enquanto o dançarino está em cena, aquele trecho conta **zero** para a média e a avaliação do cartão cai até **TENTE DE NOVO**. |
| **Desempenho** | A análise roda num processo Python separado (`analysis_server.py`), que recebe só o quadro mais recente da Unity; o relógio do jogo é a música. Tecla **F3** mostra FPS, ms do detector e atraso. |

## Requisitos

- Windows 10 ou 11 com webcam
- [Unity Hub](https://unity.com/download) + **Unity 6000.5.10f1** (só para abrir o projeto; o `.exe` não precisa)
- Internet na primeira instalação (baixa o Python portátil e os modelos de pose)

## Instalação (uma vez)

1. Clone o repositório:
   ```
   git clone https://github.com/MatheusPas/JustDance-AI---DeepLearn.git
   ```
2. Dê **duplo clique em `Tools\setup_python_runtime.bat`**. Ele baixa o Python 3.11 portátil para
   `PythonRuntime/`, instala as bibliotecas (`PythonTools/requirements-runtime.txt`), baixa os modelos
   de pose para `GameData/models/` e testa o MediaPipe. Deve terminar com `JD|runtime|ok`.
   O Python instalado no seu computador não é usado nem alterado.
3. No **Unity Hub**: **Add → Add project from disk** e escolha a pasta do repositório.

## Jogar (no editor da Unity)

1. Abra o projeto e aperte **▶ Play** (o jogo sempre começa pelo menu).
2. **Criar dança:** escolha um vídeo de dança (corpo inteiro, de frente, câmera parada), dê um
   nome e clique em **TREINAR**. A música vem do próprio vídeo. Ao terminar, a dança aparece na galeria.
3. **Jogar:** escolha a dança e, se ela tiver mais de um dançarino, quantos vão jogar.
4. Fique a uns **2 m da câmera**, com luz no rosto, e imite o dançarino como num espelho.
5. **F3** mostra o desempenho; **Esc** volta ao menu.

## Gerar o executável

1. **File → Build Profiles → Windows → Build**, escolhendo uma pasta vazia (ex.: `Build/`).
2. Ao terminar, o script `Assets/Editor/CopyPythonToBuild.cs` copia para o lado do `.exe` as pastas
   `PythonRuntime/`, `PythonTools/`, `GameData/` (danças e modelos) e `Tools/`.
3. Rode `JustDanceAI.exe`. Para levar o jogo para outro computador, copie a pasta da build inteira.

## Pelo terminal (alternativa e testes)

A partir da pasta do projeto:

```
Tools\run_python.bat train.py --video video.mp4 --name "Nome da dança"
Tools\run_python.bat train.py --list
Tools\run_python.bat check_runtime.py --smoke lite
Tools\run_python.bat bench.py --camera
Tools\run_python.bat ..\PythonTools\tests\test_pipeline.py
```

Detalhes de cada script, formato dos dados e decisões de desempenho:
[`PythonTools/README.md`](PythonTools/README.md) e [`HANDOFF_JustDanceAI.md`](HANDOFF_JustDanceAI.md).

## Estrutura

```
Assets/            projeto Unity (menu, tela de jogo, cliente do servidor de análise)
  Editor/          geradores das cenas (JustDance ▸ Build All) e script pós-build
PythonTools/       visão computacional: catalogação (train.py) e análise ao vivo (analysis_server.py)
Tools/             instalação do Python portátil e atalho run_python.bat
PythonRuntime/     Python portátil (criado pelo setup; fora do git)
GameData/          danças catalogadas, library.json e modelos de pose (fora do git)
```

## Créditos e conteúdo de terceiros

**Bibliotecas e modelos:** [MediaPipe](https://github.com/google-ai-edge/mediapipe) e os modelos
Pose Landmarker (Google, Apache 2.0), [OpenCV](https://opencv.org), NumPy,
[imageio-ffmpeg](https://github.com/imageio/imageio-ffmpeg) (FFmpeg), pygame e Unity.

**Vídeos de dança usados nos testes**, para fins exclusivamente acadêmicos e não versionados
(ficam só em `GameData/`, fora do git). Músicas e coreografias pertencem aos seus autores, à
Ubisoft (Just Dance) e à Ubisoft/Sony (Michael Jackson The Experience):

| Dança | Origem do vídeo |
|---|---|
| Blinding Lights — The Weeknd (Just Dance 2021) | <https://youtu.be/iNu4fMdnrDw> (canal Just Dance) |
| Flowers — Miley Cyrus (Just Dance 2024) | <https://youtu.be/WAw3JLkinqg> |
| Havana — Camila Cabello (Just Dance 2019) | <https://youtu.be/V0MU1IACmh4> |
| Waka Waka — Shakira (Just Dance 2018) | <https://www.youtube.com/watch?v=OobR681KUTo> |
| What Makes You Beautiful — One Direction (Just Dance 4) | <https://www.youtube.com/watch?v=834iwIuP124> |
| Macarena — Los Del Río (Just Dance Unlimited) | <https://www.youtube.com/watch?v=Nt-0yK9gPv4> |
| Thriller — Michael Jackson (Michael Jackson The Experience) | <https://www.youtube.com/watch?v=ibCGBqkkUb0> |
