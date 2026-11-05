"""
Coreografia: a "captura de movimento" de um vídeo de dança.

extract_raw_tracks()   lê o vídeo da dança e detecta todas as pessoas. Para não
                       virar gargalo: a leitura do vídeo roda numa thread (em
                       paralelo à detecção) e, em máquinas com vários núcleos, o
                       vídeo é dividido em pedaços processados em paralelo.
build_choreography()   limpa o resultado (descarta falsos positivos, preenche
                       buracos curtos, suaviza, numera da esquerda p/ direita)
save / load            formato poses.json (colunar, achatado, simples de ler
                       tanto em Python quanto no JsonUtility da Unity)

Como o vídeo da dança tem fps constante, o frame i corresponde ao instante
i / fps. Para consultar a pose no tempo t da música: índice = round(t * fps).
"""
import concurrent.futures
import json
import multiprocessing
import pickle
import queue
import threading
import time
import warnings
from dataclasses import dataclass

import cv2
import numpy as np

from config import (
    CHUNK_MATCH_MAX_DISTANCE, DIFFICULTY_EASY_MAX, DIFFICULTY_MEDIUM_MAX, FORMAT_VERSION,
    MAX_GAP_SECONDS, MIN_PLAYER_COVERAGE, SMOOTH_SECONDS, TRAIN_CHUNK_OVERLAP_SECONDS,
    TRAIN_MIN_CHUNK_SECONDS,
)
from pose_utils import (
    ANGLE_NAMES, L_HIP, POINT_INDICES, R_HIP, PlayerTracker, landmarks_to_features,
)


class ChoreographyError(RuntimeError):
    pass


@dataclass
class PlayerTrack:
    id: int
    present: np.ndarray      # (N,) bool   - pessoa localizada neste frame
    angles: np.ndarray       # (N, A) graus; NaN = ângulo não confiável/ausente
    points: np.ndarray       # (N, P, 2)   NaN = ausente
    coverage: float          # fração de frames em que foi detectada de verdade
    intensity: float         # velocidade angular média (graus/segundo)


@dataclass
class Choreography:
    fps: float
    frame_count: int
    players: list

    @property
    def duration(self):
        return self.frame_count / self.fps

    @property
    def num_people(self):
        return len(self.players)

    def index_at(self, t):
        return int(min(max(int(t * self.fps + 0.5), 0), self.frame_count - 1))

    def angles_at(self, t):
        """{id_do_dançarino: vetor de ângulos} dos dançarinos presentes em t."""
        i = self.index_at(t)
        return {p.id: p.angles[i] for p in self.players if p.present[i]}


# ---------------------------------------------------------------------------
# 1) Captura: vídeo -> detecções por frame
# ---------------------------------------------------------------------------

_HIPS = [POINT_INDICES.index(L_HIP), POINT_INDICES.index(R_HIP)]


class _FrameFeeder:
    """
    Lê o vídeo numa thread (decodifica, reduz e converte para RGB) enquanto o
    detector trabalha no frame anterior. O OpenCV libera o GIL nessas operações,
    então leitura e detecção acontecem de verdade ao mesmo tempo.

    Os primeiros `start` frames são PULADOS lendo em sequência (grab), e não por
    seek: o seek do OpenCV pode errar alguns frames e desalinharia poses e música.
    """

    def __init__(self, video_path, start=0, stop=None, max_side=None, depth=8):
        self.cap = cv2.VideoCapture(video_path)
        if not self.cap.isOpened():
            raise ChoreographyError(f"Não consegui abrir o vídeo da dança: {video_path}")
        self.fps = self.cap.get(cv2.CAP_PROP_FPS) or 30.0
        self.total_hint = int(self.cap.get(cv2.CAP_PROP_FRAME_COUNT))
        self.start, self.stop, self.max_side = start, stop, max_side
        self.decode_s = 0.0
        self._queue = queue.Queue(maxsize=depth)
        self._halt = threading.Event()
        self._error = None
        self._thread = threading.Thread(target=self._run, name="frame-feeder", daemon=True)

    def _put(self, item):
        while not self._halt.is_set():
            try:
                self._queue.put(item, timeout=0.1)
                return True
            except queue.Full:
                continue
        return False

    def _run(self):
        try:
            for _ in range(self.start):
                if self._halt.is_set() or not self.cap.grab():
                    break
            index = self.start
            while (self.stop is None or index < self.stop) and not self._halt.is_set():
                t0 = time.perf_counter()
                ok, frame = self.cap.read()
                if not ok:
                    break
                if self.max_side:
                    h, w = frame.shape[:2]
                    scale = self.max_side / max(h, w)
                    if scale < 1.0:
                        frame = cv2.resize(frame, (max(2, round(w * scale)), max(2, round(h * scale))),
                                           interpolation=cv2.INTER_AREA)
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                self.decode_s += time.perf_counter() - t0
                if not self._put((index, rgb)):
                    break
                index += 1
        except Exception as e:      # erro de leitura vai para quem consome
            self._error = e
        finally:
            self._put(None)

    def __iter__(self):
        self._thread.start()
        while True:
            item = self._queue.get()
            if item is None:
                break
            yield item
        if self._error:
            raise ChoreographyError(f"Erro ao ler o vídeo da dança: {self._error}")

    def close(self):
        self._halt.set()
        try:
            while True:
                self._queue.get_nowait()
        except queue.Empty:
            pass
        if self._thread.is_alive():
            self._thread.join(timeout=2)
        self.cap.release()


def _compact(frames, n):
    """Lista (por frame) de {id: (angles, points)} -> arrays compactos por id."""
    ids = sorted({pid for f in frames for pid in f})
    raw = {}
    for pid in ids:
        angles = np.full((n, len(ANGLE_NAMES)), np.nan, dtype=np.float32)
        points = np.full((n, len(POINT_INDICES), 2), np.nan, dtype=np.float32)
        for i, f in enumerate(frames):
            if pid in f:
                angles[i], points[i] = f[pid]
        raw[pid] = {"angles": angles, "points": points}
    return raw


def _scan_range(video_path, start, stop, detector_factory, max_people, max_side, tick=None):
    """
    Detecta pessoas nos frames [start, stop) (stop=None: até o fim).
    Retorna (raw, fps, n_frames, stats). `tick(k)` é chamado a cada k frames prontos.
    """
    detector = detector_factory(max_people)
    feeder = _FrameFeeder(video_path, start, stop, max_side)
    tracker = PlayerTracker(max_players=max_people)
    frames, detect_s, pending = [], 0.0, 0
    try:
        fps = feeder.fps
        for index, rgb in feeder:
            t0 = time.perf_counter()
            detections = detector(rgb, index * 1000.0 / fps)
            detect_s += time.perf_counter() - t0
            assigned = tracker.assign(detections or [])
            frames.append({pid: landmarks_to_features(lms) for pid, lms in assigned.items()})
            pending += 1
            if tick and pending >= 10:
                tick(pending)
                pending = 0
        if tick and pending:
            tick(pending)
    finally:
        feeder.close()
        close = getattr(detector, "close", None)
        if close:
            close()
    n = len(frames)
    stats = {"frames": n, "decode_s": feeder.decode_s, "detect_s": detect_s}
    return _compact(frames, n), fps, n, stats


# --- paralelismo por pedaços do vídeo ----------------------------------------

_PROGRESS_QUEUE = None


def _init_worker(progress_queue):
    global _PROGRESS_QUEUE
    _PROGRESS_QUEUE = progress_queue


def _scan_chunk(video_path, start, stop, detector_factory, max_people, max_side):
    tick = (lambda k: _PROGRESS_QUEUE.put(k)) if _PROGRESS_QUEUE is not None else None
    return _scan_range(video_path, start, stop, detector_factory, max_people, max_side, tick)


def _hip_xy(points):
    """(n, P, 2) -> (n, 2): centro do quadril (NaN onde o dançarino não está)."""
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", category=RuntimeWarning)
        return np.nanmean(points[:, _HIPS, :], axis=1)


def _stitch(results, bounds, overlap, max_people):
    """
    Junta os pedaços numa só captura. Cada pedaço numerou os dançarinos por conta
    própria; o trecho de sobreposição (detectado por dois pedaços) serve para
    descobrir quem é quem: liga os dançarinos mais próximos entre si.
    """
    k = len(results)
    starts = [0] + [bounds[i] - overlap for i in range(1, k)]
    owned_from = [0] + [bounds[i] for i in range(1, k)]
    total = starts[-1] + results[-1][2]
    for i in range(k - 1):
        if starts[i] + results[i][2] != bounds[i + 1]:
            raise ChoreographyError("contagem de frames inconsistente entre os pedaços")

    glob = {}

    def new_gid():
        gid = len(glob)
        glob[gid] = {
            "angles": np.full((total, len(ANGLE_NAMES)), np.nan, dtype=np.float32),
            "points": np.full((total, len(POINT_INDICES), 2), np.nan, dtype=np.float32),
        }
        return gid

    for ci, (raw, _fps, n, _stats) in enumerate(results):
        s, own = starts[ci], owned_from[ci]
        off = own - s
        mapping = {}
        if ci > 0:
            pairs = []
            for lid, track in raw.items():
                pl = _hip_xy(track["points"][:off])
                for gid, g in glob.items():
                    d = np.hypot(*(pl - _hip_xy(g["points"][s:own])).T)
                    if (~np.isnan(d)).any():
                        pairs.append((float(np.nanmean(d)), lid, gid))
            used_l, used_g = set(), set()
            for dist, lid, gid in sorted(pairs):
                if dist < CHUNK_MATCH_MAX_DISTANCE and lid not in used_l and gid not in used_g:
                    mapping[lid] = gid
                    used_l.add(lid)
                    used_g.add(gid)
        for lid, track in raw.items():
            gid = mapping.get(lid)
            if gid is None:
                gid = new_gid()
            glob[gid]["angles"][own:s + n] = track["angles"][off:]
            glob[gid]["points"][own:s + n] = track["points"][off:]
    return glob, total


def _is_picklable(obj):
    try:
        pickle.dumps(obj)
        return True
    except Exception:
        return False


def extract_raw_tracks(video_path, detector_factory, max_people, progress=None, *,
                       workers=1, detect_max_side=None,
                       min_chunk_seconds=TRAIN_MIN_CHUNK_SECONDS,
                       overlap_seconds=TRAIN_CHUNK_OVERLAP_SECONDS, stats=None):
    """
    Percorre o vídeo da dança e detecta todas as pessoas. `detector_factory(max_people)`
    devolve um detector chamável `detector(frame_rgb, timestamp_ms)`.

    workers > 1 divide o vídeo em pedaços processados em paralelo (processos
    separados; o detector_factory precisa poder ser enviado ao processo, como
    uma classe ou functools.partial). Se não puder, roda sequencialmente.

    Retorna (raw, fps, frame_count): raw[id] = {"angles": (N,A), "points": (N,P,2)},
    com NaN onde não houve detecção. `stats` (dict) recebe tempos por etapa.
    """
    t_start = time.perf_counter()
    probe = cv2.VideoCapture(video_path)
    if not probe.isOpened():
        raise ChoreographyError(f"Não consegui abrir o vídeo da dança: {video_path}")
    fps = probe.get(cv2.CAP_PROP_FPS) or 30.0
    total_hint = int(probe.get(cv2.CAP_PROP_FRAME_COUNT))
    probe.release()

    overlap = max(1, round(overlap_seconds * fps))
    min_chunk = max(int(min_chunk_seconds * fps), 2 * overlap + 1)
    n_chunks = 1
    if workers > 1 and total_hint > 0 and _is_picklable(detector_factory):
        n_chunks = max(1, min(workers, total_hint // min_chunk))

    def finish(raw, real_fps, n, parts, used_workers):
        if n == 0:
            raise ChoreographyError("O vídeo da dança não tem frames legíveis.")
        if stats is not None:
            stats.update(
                frames=n, workers=used_workers, wall_s=time.perf_counter() - t_start,
                decode_s=sum(p["decode_s"] for p in parts), detect_s=sum(p["detect_s"] for p in parts),
            )
        return raw, real_fps, n

    def sequential():
        done = [0]

        def tick(k):
            done[0] += k
            if progress and total_hint:
                progress(min(done[0], total_hint), total_hint)

        raw, real_fps, n, st = _scan_range(video_path, 0, None, detector_factory, max_people,
                                           detect_max_side, tick)
        return finish(raw, real_fps, n, [st], 1)

    if n_chunks == 1:
        return sequential()

    bounds = [round(i * total_hint / n_chunks) for i in range(n_chunks + 1)]
    ctx = multiprocessing.get_context("spawn")      # igual no Windows e no Linux
    progress_queue = ctx.Queue()
    done = 0
    try:
        with concurrent.futures.ProcessPoolExecutor(
                max_workers=n_chunks, mp_context=ctx,
                initializer=_init_worker, initargs=(progress_queue,)) as pool:
            futures = []
            for i in range(n_chunks):
                scan_from = bounds[i] if i == 0 else bounds[i] - overlap
                scan_to = bounds[i + 1] if i < n_chunks - 1 else None
                futures.append(pool.submit(_scan_chunk, video_path, scan_from, scan_to,
                                           detector_factory, max_people, detect_max_side))
            pending = set(futures)
            while pending:
                _, pending = concurrent.futures.wait(pending, timeout=0.25)
                try:
                    while True:
                        done += progress_queue.get_nowait()
                except queue.Empty:
                    pass
                if progress:
                    progress(min(done, total_hint), total_hint)
            results = [f.result() for f in futures]
    finally:
        progress_queue.close()

    try:
        glob, total = _stitch(results, bounds, overlap, max_people)
    except ChoreographyError:
        return sequential()       # contagem de frames do arquivo não bateu: refaz sem dividir
    if progress and total_hint:
        progress(total_hint, total_hint)
    return finish(glob, fps, total, [r[3] for r in results], n_chunks)


# ---------------------------------------------------------------------------
# 2) Limpeza
# ---------------------------------------------------------------------------

def _fill_short_gaps(x, max_gap):
    """Interpola linearmente buracos (NaN) internos de até `max_gap` frames."""
    nan = np.isnan(x)
    if not nan.any() or nan.all():
        return x
    idx = np.arange(len(x))
    interp = np.interp(idx, idx[~nan], x[~nan])
    out = x.copy()
    edges = np.flatnonzero(np.diff(np.concatenate(([0], nan.view(np.int8), [0]))))
    for start, end in zip(edges[::2], edges[1::2]):      # runs de NaN: [start, end)
        if start > 0 and end < len(x) and (end - start) <= max_gap:
            out[start:end] = interp[start:end]
    return out


def _smooth(x, window):
    """Média móvel que ignora NaN e mantém os NaN onde estavam."""
    if window <= 1 or len(x) < window:
        return x
    valid = ~np.isnan(x)
    kernel = np.ones(window)
    num = np.convolve(np.where(valid, x, 0.0), kernel, mode="same")
    den = np.convolve(valid.astype(float), kernel, mode="same")
    out = np.full_like(x, np.nan)
    out[valid] = num[valid] / den[valid]
    return out


def _clean(arr2d, max_gap, window):
    out = np.empty_like(arr2d, dtype=np.float64)
    for c in range(arr2d.shape[1]):
        col = _fill_short_gaps(arr2d[:, c].astype(np.float64), max_gap)
        out[:, c] = _smooth(col, window)
    return out.astype(np.float32)


def _intensity(angles, fps):
    """Velocidade angular média (graus/segundo) das articulações confiáveis."""
    if len(angles) < 2:
        return 0.0
    speed = np.abs(np.diff(angles, axis=0)) * fps
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", category=RuntimeWarning)
        per_frame = np.nanmean(speed, axis=1)
        value = np.nanmean(per_frame)
    return 0.0 if np.isnan(value) else float(value)


def difficulty_label(intensity):
    if intensity < DIFFICULTY_EASY_MAX:
        return "Fácil"
    if intensity < DIFFICULTY_MEDIUM_MAX:
        return "Médio"
    return "Difícil"


def build_choreography(raw, fps, frame_count):
    """
    raw -> Choreography limpa:
      - descarta pessoas que aparecem em menos de MIN_PLAYER_COVERAGE do vídeo
        (falsos positivos, plateia que passou rapidamente)
      - preenche buracos curtos e suaviza o tremido
      - renumera os dançarinos da esquerda para a direita (posição média)
    """
    max_gap = max(1, int(round(MAX_GAP_SECONDS * fps)))
    window = max(3, int(round(SMOOTH_SECONDS * fps)) | 1)   # sempre ímpar

    kept = []
    for pid, track in raw.items():
        detected = ~np.isnan(track["points"]).all(axis=(1, 2))
        coverage = float(detected.mean())
        if coverage < MIN_PLAYER_COVERAGE:
            continue

        pts = track["points"].reshape(frame_count, -1)
        points = _clean(pts, max_gap, window).reshape(frame_count, len(POINT_INDICES), 2)
        angles = _clean(track["angles"], max_gap, window)
        present = ~np.isnan(points).all(axis=(1, 2))

        hips = [POINT_INDICES.index(L_HIP), POINT_INDICES.index(R_HIP)]
        mean_x = float(np.nanmedian(points[present][:, hips, 0]))
        kept.append((mean_x, PlayerTrack(0, present, angles, points, coverage, _intensity(angles, fps))))

    if not kept:
        raise ChoreographyError(
            "Nenhum dançarino foi detectado de forma estável no vídeo. "
            "Use um vídeo com o corpo inteiro visível e boa iluminação."
        )

    kept.sort(key=lambda item: item[0])
    players = []
    for new_id, (_, track) in enumerate(kept):
        track.id = new_id
        players.append(track)
    return Choreography(fps=fps, frame_count=frame_count, players=players)


# ---------------------------------------------------------------------------
# 3) Arquivo poses.json
# ---------------------------------------------------------------------------

def save_choreography(choreo, path):
    players = []
    for p in choreo.players:
        players.append({
            "id": p.id,
            "coverage": round(p.coverage, 3),
            "intensity": round(p.intensity, 1),
            "present": p.present.astype(int).tolist(),
            "valid": (~np.isnan(p.angles)).astype(int).ravel().tolist(),
            "angles": np.round(np.nan_to_num(p.angles, nan=0.0), 1).ravel().tolist(),
            "points": np.round(np.nan_to_num(p.points, nan=0.0), 4).ravel().tolist(),
        })
    data = {
        "format_version": FORMAT_VERSION,
        "fps": choreo.fps,
        "frame_count": choreo.frame_count,
        "duration_sec": round(choreo.duration, 3),
        "angle_names": ANGLE_NAMES,
        "point_indices": POINT_INDICES,
        "players": players,
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, separators=(",", ":"))


def load_choreography(path):
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    if data.get("format_version") != FORMAT_VERSION:
        raise ChoreographyError(
            f"{path} está em um formato antigo. Refaça a dança com train.py."
        )

    n = data["frame_count"]
    n_angles = len(data["angle_names"])
    n_points = len(data["point_indices"])
    players = []
    for p in data["players"]:
        present = np.array(p["present"], dtype=bool)
        angles = np.array(p["angles"], dtype=np.float32).reshape(n, n_angles)
        valid = np.array(p["valid"], dtype=bool).reshape(n, n_angles)
        angles[~valid] = np.nan
        points = np.array(p["points"], dtype=np.float32).reshape(n, n_points, 2)
        points[~present] = np.nan
        players.append(PlayerTrack(p["id"], present, angles, points, p["coverage"], p["intensity"]))
    return Choreography(fps=data["fps"], frame_count=n, players=players)
