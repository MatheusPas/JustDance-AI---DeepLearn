"""
Configuração central: caminhos e parâmetros do pipeline de catalogação e do jogo.
"""
import os

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
# Dentro do projeto da Unity este código fica em PythonTools/ e os dados (danças,
# modelos, library.json) em GameData/, ao lado. É a mesma pasta que a Unity lê.
# Fora desse layout, os dados ficam em ./assets.
_DEFAULT_ASSETS = (os.path.join(os.path.dirname(BASE_DIR), "GameData")
                   if os.path.basename(BASE_DIR) == "PythonTools"
                   else os.path.join(BASE_DIR, "assets"))
# JUSTDANCE_ASSETS permite apontar para outra pasta (a Unity define isto; os testes
# usam para não mexer no seu catálogo).
ASSETS_DIR = os.environ.get("JUSTDANCE_ASSETS") or _DEFAULT_ASSETS

# Cada dança catalogada vira uma pasta própria: assets/dances/<slug>/
DANCES_DIR = os.path.join(ASSETS_DIR, "dances")
# Modelos de pose (~10-30 MB cada). JUSTDANCE_MODELS permite compartilhar entre catálogos.
MODELS_DIR = os.environ.get("JUSTDANCE_MODELS") or os.path.join(ASSETS_DIR, "models")
# Índice com todas as danças (a Unity lê este arquivo). Os caminhos dentro
# dele são RELATIVOS à pasta onde ele está (assets/).
LIBRARY_FILE = os.path.join(ASSETS_DIR, "library.json")

# Versão do formato de dança. Danças de versões antigas são ignoradas.
FORMAT_VERSION = 1

# ---- Vídeo da dança (normalizado na catalogação) ---------------------------
MAX_VIDEO_FPS = 30        # o vídeo e as poses usam o mesmo fps (1 pose por frame)
MAX_VIDEO_HEIGHT = 720    # vídeos maiores são reduzidos

# ---- Detecção ---------------------------------------------------------------
MAX_TRAINING_DETECTIONS = 6      # máximo de pessoas rastreadas no vídeo
LANDMARK_MIN_VISIBILITY = 0.5    # abaixo disso o ponto é considerado não confiável
MIN_PLAYER_COVERAGE = 0.25       # dançarino precisa aparecer em >= 25% dos frames
MAX_GAP_SECONDS = 0.5            # buracos de detecção menores que isso são preenchidos
SMOOTH_SECONDS = 0.15            # janela da média móvel que tira o tremido

# ---- Desempenho -------------------------------------------------------------
# Modelos do MediaPipe: lite (rápido) | full (equilibrado) | heavy (mais preciso, lento).
TRAIN_MODEL = "full"             # catalogação roda uma vez: vale usar qualidade
LIVE_MODEL = "lite"              # enquanto o jogador dança: prioriza velocidade (fps)

TRAIN_DETECT_MAX_SIDE = 960      # frames da catalogação são reduzidos a este lado maior
TRAIN_WORKERS = 0                # processos em paralelo na catalogação (0 = automático)
TRAIN_MIN_CHUNK_SECONDS = 8.0    # não vale dividir vídeos curtos demais
TRAIN_CHUNK_OVERLAP_SECONDS = 1.0  # trecho refeito no começo de cada pedaço (aquecimento)
CHUNK_MATCH_MAX_DISTANCE = 0.25  # distância máx. (0..1 da tela) para ligar o mesmo dançarino entre pedaços

LIVE_DETECT_MAX_WIDTH = 640      # imagens maiores que isso são reduzidas antes de detectar
CAMERA_INDEX = 0
CAMERA_WIDTH = 640               # webcams entregam mais fps em resoluções baixas (MJPG)
CAMERA_HEIGHT = 480
CAMERA_FPS = 30
REFERENCE_BUFFER_FRAMES = 6      # quadros do vídeo da dança decodificados à frente
SCORE_TIME_OFFSET = 0.0          # compara o jogador com a dança em (t - offset); ajuste p/ latência

# ---- Dificuldade (heurística, ajuste conforme suas danças) -----------------
# Intensidade = velocidade angular média das articulações, em graus/segundo.
DIFFICULTY_EASY_MAX = 20.0
DIFFICULTY_MEDIUM_MAX = 60.0


def auto_workers():
    """Quantos processos usar na catalogação quando TRAIN_WORKERS = 0."""
    cores = os.cpu_count() or 2
    return max(1, min(4, cores // 2))


os.makedirs(DANCES_DIR, exist_ok=True)
os.makedirs(MODELS_DIR, exist_ok=True)
