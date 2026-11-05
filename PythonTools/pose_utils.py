"""
Detecção de pose multipessoa (MediaPipe Tasks: PoseLandmarker), rastreamento
de jogadores entre frames, ângulos das articulações e comparação.
"""
import math
import os
import urllib.request

import numpy as np

from config import LANDMARK_MIN_VISIBILITY, MODELS_DIR

MODEL_NAMES = ("lite", "full", "heavy")
_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/pose_landmarker/"
    "pose_landmarker_{name}/float16/1/pose_landmarker_{name}.task"
)


def _check_model_name(name):
    if name not in MODEL_NAMES:
        raise ValueError(f"Modelo desconhecido '{name}'. Use um de: {', '.join(MODEL_NAMES)}.")
    return name


def model_path(name="full"):
    return os.path.join(MODELS_DIR, f"pose_landmarker_{_check_model_name(name)}.task")


def model_url(name="full"):
    return _MODEL_URL.format(name=_check_model_name(name))


MODEL_PATH = model_path("full")      # compatibilidade
MODEL_URL = model_url("full")

# Índices dos pontos do corpo no MediaPipe Pose (33 pontos)
NOSE = 0
L_SHOULDER, R_SHOULDER = 11, 12
L_ELBOW, R_ELBOW = 13, 14
L_WRIST, R_WRIST = 15, 16
L_HIP, R_HIP = 23, 24
L_KNEE, R_KNEE = 25, 26
L_ANKLE, R_ANKLE = 27, 28

# Ângulo medido no ponto do meio de cada trinca (a, b, c)
ANGLE_DEFS = [
    ("left_elbow", (L_SHOULDER, L_ELBOW, L_WRIST)),
    ("right_elbow", (R_SHOULDER, R_ELBOW, R_WRIST)),
    ("left_shoulder", (L_HIP, L_SHOULDER, L_ELBOW)),
    ("right_shoulder", (R_HIP, R_SHOULDER, R_ELBOW)),
    ("left_hip", (L_SHOULDER, L_HIP, L_KNEE)),
    ("right_hip", (R_SHOULDER, R_HIP, R_KNEE)),
    ("left_knee", (L_HIP, L_KNEE, L_ANKLE)),
    ("right_knee", (R_HIP, R_KNEE, R_ANKLE)),
]
ANGLE_NAMES = [name for name, _ in ANGLE_DEFS]

# Pontos guardados na coreografia (suficientes para desenhar o esqueleto)
POINT_INDICES = [NOSE, L_SHOULDER, R_SHOULDER, L_ELBOW, R_ELBOW, L_WRIST, R_WRIST,
                 L_HIP, R_HIP, L_KNEE, R_KNEE, L_ANKLE, R_ANKLE]


# ---------------------------------------------------------------------------
# Modelo e detector
# ---------------------------------------------------------------------------

class ModelError(RuntimeError):
    """Falha ao obter o modelo de pose."""


def ensure_model(name="full"):
    """Baixa o modelo de pose na primeira vez (fica em MODELS_DIR)."""
    path, url = model_path(name), model_url(name)
    if os.path.exists(path):
        return path

    print(f"Baixando o modelo de pose '{name}' (só acontece uma vez)...", flush=True)
    tmp = path + ".part"
    try:
        urllib.request.urlretrieve(url, tmp)
        os.replace(tmp, path)
    except Exception as e:
        if os.path.exists(tmp):
            os.remove(tmp)
        raise ModelError(
            f"Não consegui baixar o modelo de pose ({e}).\n"
            f"Baixe manualmente em:\n  {url}\n"
            f"e salve como:\n  {path}"
        ) from e
    return path


def create_landmarker(num_poses, model="full"):
    """PoseLandmarker em modo VIDEO, capaz de detectar até `num_poses` pessoas."""
    import mediapipe as mp

    vision = mp.tasks.vision
    options = vision.PoseLandmarkerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=ensure_model(model)),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=num_poses,
        min_pose_detection_confidence=0.5,
        min_pose_presence_confidence=0.5,
        min_tracking_confidence=0.5,
    )
    return vision.PoseLandmarker.create_from_options(options)


def detect(landmarker, frame_rgb, timestamp_ms):
    """Detecta todas as pessoas do frame. Retorna lista de listas de 33 landmarks."""
    import mediapipe as mp

    image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(frame_rgb))
    return landmarker.detect_for_video(image, int(timestamp_ms)).pose_landmarks


class MediaPipeDetector:
    """
    Detector de pessoas chamável: detector(frame_rgb, timestamp_ms) -> detecções.
    Os timestamps passados ao MediaPipe são sempre estritamente crescentes.
    """

    def __init__(self, max_people, model="full"):
        self._landmarker = create_landmarker(max_people, model)
        self._last_ts = -1

    def __call__(self, frame_rgb, timestamp_ms):
        ts = max(int(timestamp_ms), self._last_ts + 1)
        self._last_ts = ts
        return detect(self._landmarker, frame_rgb, ts)

    def close(self):
        self._landmarker.close()


# ---------------------------------------------------------------------------
# Rastreamento de jogadores
# ---------------------------------------------------------------------------

def _hip_center(landmarks):
    return ((landmarks[L_HIP].x + landmarks[R_HIP].x) / 2,
            (landmarks[L_HIP].y + landmarks[R_HIP].y) / 2)


class PlayerTracker:
    """
    Mantém um número de jogador estável (0, 1, 2...) para cada pessoa, mesmo
    que a ordem das detecções mude entre frames. No primeiro frame os jogadores
    são numerados da ESQUERDA para a DIREITA; depois, cada detecção é ligada ao
    jogador conhecido mais próximo (pelo quadril).
    """

    def __init__(self, max_players, match_threshold=0.35):
        self.max_players = max_players
        self.match_threshold = match_threshold
        self.centroids = {}

    def assign(self, detections):
        """detections: lista de landmarks por pessoa. Retorna {id: landmarks}."""
        if not detections:
            return {}

        centers = [_hip_center(d) for d in detections]

        if not self.centroids:
            order = sorted(range(len(centers)), key=lambda i: centers[i][0])
            assigned = {}
            for pid, idx in enumerate(order[: self.max_players]):
                assigned[pid] = detections[idx]
                self.centroids[pid] = centers[idx]
            return assigned

        assigned, used = {}, set()
        for pid, prev in list(self.centroids.items()):
            best_idx, best_dist = None, float("inf")
            for idx, center in enumerate(centers):
                if idx in used:
                    continue
                dist = math.hypot(center[0] - prev[0], center[1] - prev[1])
                if dist < best_dist:
                    best_idx, best_dist = idx, dist
            if best_idx is not None and best_dist < self.match_threshold:
                assigned[pid] = detections[best_idx]
                self.centroids[pid] = centers[best_idx]
                used.add(best_idx)

        for idx, center in enumerate(centers):
            if idx in used or len(self.centroids) >= self.max_players:
                continue
            new_id = len(self.centroids)
            assigned[new_id] = detections[idx]
            self.centroids[new_id] = center

        return assigned


# ---------------------------------------------------------------------------
# Features (ângulos + pontos) e comparação
# ---------------------------------------------------------------------------

def _visibility(lm):
    v = getattr(lm, "visibility", None)
    return 1.0 if v is None else v


def _angle(a, b, c):
    ax, ay = a[0] - b[0], a[1] - b[1]
    cx, cy = c[0] - b[0], c[1] - b[1]
    mag = math.hypot(ax, ay) * math.hypot(cx, cy)
    if mag == 0:
        return float("nan")
    cos = max(-1.0, min(1.0, (ax * cx + ay * cy) / mag))
    return math.degrees(math.acos(cos))


def landmarks_to_features(landmarks):
    """
    Converte os landmarks de UMA pessoa em:
      angles: array (len(ANGLE_DEFS),) em graus; NaN quando algum dos 3 pontos
              tem visibilidade baixa (ex.: perna fora do quadro)
      points: array (len(POINT_INDICES), 2) com x,y normalizados (0..1)
    """
    angles = np.full(len(ANGLE_DEFS), np.nan, dtype=np.float32)
    for k, (_, (ia, ib, ic)) in enumerate(ANGLE_DEFS):
        trio = (landmarks[ia], landmarks[ib], landmarks[ic])
        if min(_visibility(p) for p in trio) < LANDMARK_MIN_VISIBILITY:
            continue
        angles[k] = _angle((trio[0].x, trio[0].y), (trio[1].x, trio[1].y), (trio[2].x, trio[2].y))

    points = np.array([[landmarks[i].x, landmarks[i].y] for i in POINT_INDICES], dtype=np.float32)
    return angles, points


def score_angles(angles_a, angles_b, tolerance_deg=35.0, min_joints=3):
    """
    Score de 0 a 100 entre dois vetores de ângulos (NaN = ignorado).
    Retorna None se houver poucas articulações em comum para comparar.
    """
    a = np.asarray(angles_a, dtype=np.float64)
    b = np.asarray(angles_b, dtype=np.float64)
    mask = ~np.isnan(a) & ~np.isnan(b)
    if mask.sum() < min_joints:
        return None
    avg_diff = float(np.abs(a[mask] - b[mask]).mean())
    return round(max(0.0, 100.0 * (1 - avg_diff / (tolerance_deg * 2))), 1)
