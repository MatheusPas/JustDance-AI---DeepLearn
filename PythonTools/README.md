# PythonTools — captura, catalogação e análise de poses

Tudo que o jogo faz com **imagem e movimento** mora aqui. Roda com o **Python 3.11
portátil do próprio projeto** (`PythonRuntime/`), sem mexer no Python do seu sistema.

```
JustDance AI - DeepLearn/
  Tools/                  setup_python_runtime.bat/.ps1, run_python.bat
  PythonRuntime/          Python portátil + bibliotecas (não vai pro git; o setup cria)
  PythonTools/            este código
  GameData/               danças catalogadas, library.json, modelos de pose (não vai pro git)
  Assets/                 projeto Unity
```

## 1. Instalar o Python do projeto (uma vez)

Duplo clique em `Tools\setup_python_runtime.bat` (ou, na Unity, **JustDance ▸ Instalar Python do projeto**).
Ele baixa o Python 3.11 portátil, instala as bibliotecas de `requirements-runtime.txt`, baixa os
modelos de pose e faz um teste real do MediaPipe. Precisa de internet só nessa hora.

Se algo falhar, ele diz qual etapa. `Tools\setup_python_runtime.bat -Force` refaz do zero.

## 2. Usar

Pelo terminal (a partir da pasta do projeto):

```
Tools\run_python.bat train.py --video meu_video.mp4 --name "Sofrência do Verão"
Tools\run_python.bat train.py --list
Tools\run_python.bat play.py            # jogo de teste em Python (Unity não precisa disso)
Tools\run_python.bat bench.py --video video.mp4
```

Na Unity, a tela **Criar Dança** chama o `train.py` e o jogo chama o `analysis_server.py`.

## O que a catalogação faz

| O que vem no vídeo | Vira | Para quê |
|---|---|---|
| A trilha de áudio | `music.mp3` | Toca no jogo e serve de **relógio mestre** |
| As imagens | `dance.mp4` (**sem áudio**, fps constante) | Referência visual (fundo em negativo) |
| O movimento dos dançarinos | `poses.json` | O gabarito com que o jogador é comparado |

Opções do `train.py`:

```
--start 12 --end 95        tira introdução e final (a música é cortada junto)
--music estudio.mp3        usa a música de estúdio no lugar do áudio do vídeo
--music-offset 1.5         a dança começa 1,5 s depois do início da música (negativo = entra silêncio)
--max-people 2             rastreia no máximo 2 dançarinos (menos trabalho por frame)
--workers 4                processos em paralelo (0 = automático, 1 = sem paralelismo)
--model lite|full|heavy    modelo de pose (padrão: full)
--overwrite  --list  --remove NOME  --rebuild-index
--machine                  saída "JD|tipo|dados" para a Unity ler
```

## Desempenho (onde estavam os gargalos)

**Catalogação**
- A leitura do vídeo roda numa thread, em paralelo à detecção (antes uma esperava a outra).
- Os frames são reduzidos a no máximo 960 px antes de detectar (os pontos são normalizados, então
  o resultado não muda de escala).
- Com vários núcleos, o vídeo é dividido em pedaços processados por processos separados. Cada
  pedaço refaz 1 s do anterior (aquecimento do rastreador) e os dançarinos são ligados entre
  pedaços pela posição. Os frames iniciais de cada pedaço são pulados lendo em sequência, nunca por
  *seek*, que poderia errar frames e desalinhar poses e música.
- O modelo é baixado antes de converter o vídeo; se falhar, falha rápido.

**Jogo (tempo real)** — o laço que desenha a tela nunca espera por nada lento:
- *thread da câmera*: guarda só o frame mais recente. Sem fila, o atraso não se acumula.
  No Windows usa DirectShow + MJPG 640×480 (webcams só entregam 30 fps assim).
- *thread do detector*: analisa o frame mais novo; frames atrasados são descartados.
  Usa o modelo `lite` (config `LIVE_MODEL`).
- *thread do vídeo*: decodifica a dança à frente do relógio, já reduzida e em negativo; se o PC
  engasgar, descarta frames atrasados em vez de decodificá-los.
- O score compara o jogador com a dança no instante em que o **frame foi capturado**, não no
  instante em que a detecção terminou: o atraso do detector não vira erro de pontuação.
- Tecla **D** no `play.py` mostra fps da tela, ms do detector, latência e descartados.

`bench.py` mede tudo isso no **seu** computador e diz onde está o limite:

```
bench.py --video dance.mp4                 leitura, detecção por modelo, catalogação com 1/2/4 processos
bench.py --video dance.mp4 --live          o detector acompanha 30 fps?
bench.py --camera                          a webcam entrega quantos fps?
```

Os ganhos reais dependem da sua máquina: **rode o `bench.py`** antes de ajustar `--workers`,
`LIVE_MODEL` e `--max-people`.

## Servidor de análise (Unity ↔ Python)

`analysis_server.py` recebe os frames da câmera da Unity por TCP em `127.0.0.1` e devolve ângulos e
pontos de cada jogador. Protocolo binário (sem JSON por frame), documentado no topo de
`sidecar_protocol.py`. Só o frame mais recente é analisado. `sidecar_client.py --spawn` testa o
servidor com a webcam sem abrir a Unity.

## Formato de `poses.json`

Colunar e achatado (simples de ler com o `JsonUtility` da Unity). Tempo ↔ índice: `i = round(t * fps)`.

```
format_version, fps, frame_count, duration_sec
angle_names    ["left_elbow", "right_elbow", "left_shoulder", "right_shoulder",
                "left_hip", "right_hip", "left_knee", "right_knee"]
point_indices  índices MediaPipe dos 13 pontos guardados
players[]      um por dançarino, da esquerda para a direita:
  id, coverage, intensity
  present   [frame_count]            1 = dançarino localizado neste frame
  valid     [frame_count * 8]        1 = ângulo confiável
  angles    [frame_count * 8]        graus (0 onde valid = 0)
  points    [frame_count * 13 * 2]   x,y normalizados (0..1)
```

Todos os caminhos de `library.json` e `meta.json` são **relativos a `GameData/`**.

## Testes

```
Tools\run_python.bat ..\PythonTools\tests\test_pipeline.py     (ou: python -m unittest discover -s tests -v)
```

Usam um vídeo sintético (dois bonecos de palito) e um detector que lê a imagem, então rodam sem
webcam e **sem o modelo do MediaPipe**. Conferem: separação música/dança, recorte, música anexada,
rastreamento, descarte de falsos positivos, alinhamento vídeo↔música↔poses, catalogação em
paralelo (igual à sequencial), as threads de tempo real, o protocolo e o servidor, e uma partida
completa. **Não testam o MediaPipe em si**: para isso, `check_runtime.py --smoke lite`.
