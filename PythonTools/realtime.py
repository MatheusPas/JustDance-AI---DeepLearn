"""
Peças do modo tempo real. A ideia central: NADA que seja lento pode travar o
laço que desenha a tela.

  LatestFrameCamera   lê a webcam numa thread e guarda só o frame MAIS RECENTE
                      (sem fila: não acumula atraso se o resto estiver lento)
  PoseWorker          detecta as poses em outra thread; o laço principal só
                      "entrega o frame mais novo" e "pega o último resultado"
  ReferenceStream     decodifica o vídeo da dança à frente do relógio, já
                      reduzido e em negativo, numa terceira thread

O OpenCV e o MediaPipe liberam o GIL nas partes pesadas, então as threads
trabalham de verdade em paralelo.
"""
import collections
import os
import sys
import threading
import time
from dataclasses import dataclass, field

import config  # noqa: F401  (precisa vir antes do cv2)
import cv2
import numpy as np

from pose_utils import PlayerTracker, landmarks_to_features


# ---------------------------------------------------------------------------
# Câmera
# ---------------------------------------------------------------------------

def open_camera(index=None, width=None, height=None, fps=None):
    """
    Abre a webcam com as opções que mais reduzem atraso:
      - backend DirectShow no Windows (abre em ~1 s em vez de ~10 s)
      - MJPG em resolução baixa (webcams só entregam 30 fps assim)
      - buffer interno de 1 frame
    Retorna None se não conseguir abrir.
    """
    index = config.CAMERA_INDEX if index is None else index
    width = width or config.CAMERA_WIDTH
    height = height or config.CAMERA_HEIGHT
    fps = fps or config.CAMERA_FPS
    os.environ.setdefault("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0")

    backends = [cv2.CAP_DSHOW, cv2.CAP_ANY] if sys.platform == "win32" else [cv2.CAP_ANY]
    for backend in backends:
        cap = cv2.VideoCapture(index, backend)
        if cap.isOpened():
            cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
            cap.set(cv2.CAP_PROP_FPS, fps)
            cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            return cap
        cap.release()
    return None


class LatestFrameCamera:
    """
    Lê `capture` (algo como cv2.VideoCapture) numa thread e guarda só o frame mais
    recente. `read_latest()` nunca bloqueia.

    mirror=True espelha a imagem (efeito de espelho): feito aqui, ANTES da
    detecção, para que "esquerda" e "direita" dos landmarks coincidam com os
    lados da tela.
    """

    MAX_CONSECUTIVE_FAILURES = 30

    def __init__(self, capture, mirror=True, max_fps=None):
        self._cap = capture
        self._mirror = mirror
        self._min_interval = 1.0 / max_fps if max_fps else 0.0   # só para fontes que não bloqueiam (testes)
        self._lock = threading.Lock()
        self._frame = None
        self._seq = 0
        self._t = 0.0
        self._halt = threading.Event()
        self._thread = threading.Thread(target=self._run, name="camera", daemon=True)
        self.captured = 0
        self.ended = False

    def start(self):
        self._thread.start()
        return self

    def _run(self):
        failures = 0
        while not self._halt.is_set():
            t0 = time.perf_counter()
            ok, frame = self._cap.read()
            t = time.perf_counter()
            if not ok or frame is None:
                failures += 1
                if failures >= self.MAX_CONSECUTIVE_FAILURES or not self._cap.isOpened():
                    break
                time.sleep(0.005)
                continue
            failures = 0
            if self._mirror:
                frame = cv2.flip(frame, 1)
            with self._lock:
                self._frame, self._t = frame, t
                self._seq += 1
                self.captured += 1
            if self._min_interval:
                wait = self._min_interval - (time.perf_counter() - t0)
                if wait > 0:
                    time.sleep(wait)
        self.ended = True

    def read_latest(self, after_seq=-1):
        """(seq, frame_bgr, t_capture) do frame mais recente, ou None se não há um mais novo que after_seq."""
        with self._lock:
            if self._frame is None or self._seq <= after_seq:
                return None
            return self._seq, self._frame, self._t

    def stop(self):
        self._halt.set()
        self._thread.join(timeout=2)
        self._cap.release()


# ---------------------------------------------------------------------------
# Detecção de poses
# ---------------------------------------------------------------------------

@dataclass
class PoseResult:
    seq: int
    t_capture: float                 # perf_counter() do instante da captura do frame
    players: dict                    # id -> (angles (A,), points (P, 2))
    detect_ms: float                 # quanto a detecção deste frame demorou
    meta: object = None              # o que foi passado em submit() (o servidor usa p/ ecoar t_ms)


class PoseWorker:
    """
    Detecta poses numa thread separada com uma "caixa de correio" de UM frame:
    submit() nunca espera e, se o detector ainda estiver ocupado, o frame mais
    antigo é descartado (melhor pular um frame do que acumular atraso).

    detector: chamável `detector(frame_rgb, timestamp_ms) -> detecções`
    input_format: "bgr" (câmera do OpenCV) ou "rgb" (já convertido)
    max_width: frames mais largos que isto são reduzidos antes de detectar
    on_result: opcional, chamada NA THREAD DO DETECTOR a cada resultado novo
    """

    def __init__(self, detector, max_players, *, input_format="bgr", max_width=None, on_result=None):
        self._detector = detector
        self._tracker = PlayerTracker(max_players=max_players)
        self._format = input_format
        self._max_width = max_width
        self._on_result = on_result
        self._cond = threading.Condition()
        self._pending = None
        self._result = None
        self._polled = -1
        self._halt = threading.Event()
        self._thread = threading.Thread(target=self._run, name="pose-worker", daemon=True)
        self.dropped = 0
        self.processed = 0
        self.detect_ms_avg = 0.0
        self.error = None

    def start(self):
        self._thread.start()
        return self

    def submit(self, seq, frame, t_capture, meta=None):
        with self._cond:
            if self._pending is not None:
                self.dropped += 1
            self._pending = (seq, frame, t_capture, meta)
            self._cond.notify()

    def poll(self):
        """Resultado mais recente, se for novo desde a última chamada; senão None."""
        with self._cond:
            res = self._result
            if res is None or res.seq == self._polled:
                return None
            self._polled = res.seq
            return res

    def _prepare(self, frame):
        if self._max_width and frame.shape[1] > self._max_width:
            h, w = frame.shape[:2]
            frame = cv2.resize(frame, (self._max_width, max(2, round(h * self._max_width / w))),
                               interpolation=cv2.INTER_AREA)
        if self._format == "bgr":
            return cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        return frame if frame.flags.c_contiguous else np.ascontiguousarray(frame)

    def _run(self):
        try:
            while not self._halt.is_set():
                with self._cond:
                    while self._pending is None and not self._halt.is_set():
                        self._cond.wait(0.1)
                    if self._halt.is_set():
                        break
                    seq, frame, t_capture, meta = self._pending
                    self._pending = None

                t0 = time.perf_counter()
                rgb = self._prepare(frame)
                detections = self._detector(rgb, int(t_capture * 1000))
                assigned = self._tracker.assign(detections or [])
                players = {pid: landmarks_to_features(lms) for pid, lms in assigned.items()}
                ms = (time.perf_counter() - t0) * 1000.0

                self.processed += 1
                self.detect_ms_avg = ms if self.processed == 1 else 0.9 * self.detect_ms_avg + 0.1 * ms
                result = PoseResult(seq, t_capture, players, ms, meta)
                with self._cond:
                    self._result = result
                if self._on_result:
                    self._on_result(result)
        except Exception as e:      # o laço principal consulta .error
            self.error = e

    def stop(self):
        self._halt.set()
        with self._cond:
            self._cond.notify_all()
        self._thread.join(timeout=3)
        close = getattr(self._detector, "close", None)
        if close:
            close()


# ---------------------------------------------------------------------------
# Vídeo de referência
# ---------------------------------------------------------------------------

class ReferenceStream:
    """
    Decodifica o vídeo da dança numa thread, um pouco à frente do relógio, já
    reduzido para `size` e (opcional) em negativo. `frame_at(t)` só pega o frame
    pronto: o laço principal não gasta tempo decodificando.

    Se o computador travar por um instante, a thread descarta (grab) os frames
    atrasados em vez de decodificá-los por completo, e alcança o relógio rápido.
    """

    def __init__(self, path, size, invert=True, buffer=None):
        self.cap = cv2.VideoCapture(path)
        if not self.cap.isOpened():
            raise RuntimeError(f"Não foi possível abrir o vídeo da dança: {path}")
        self.fps = self.cap.get(cv2.CAP_PROP_FPS) or 30.0
        self.size = size
        self.invert = invert
        self._max_buffer = buffer or config.REFERENCE_BUFFER_FRAMES
        self._buf = collections.deque()          # (índice, frame)
        self._cond = threading.Condition()
        self._target = 0
        self._eof = False
        self._last_index = -1
        self._halt = threading.Event()
        self._thread = threading.Thread(target=self._run, name="reference", daemon=True)

    def start(self, wait_first=True):
        self._thread.start()
        if wait_first:
            with self._cond:
                self._cond.wait_for(lambda: self._buf or self._eof, timeout=5)
        return self

    def _run(self):
        index = 0
        while not self._halt.is_set():
            with self._cond:
                while len(self._buf) >= self._max_buffer and not self._halt.is_set():
                    self._cond.wait(0.05)
                late = index < self._target - 1
            if self._halt.is_set():
                break
            if late:                                # atrasado: só avança, sem decodificar tudo
                ok = self.cap.grab()
                if not ok:
                    break
                index += 1
                continue
            ok, frame = self.cap.read()
            if not ok:
                break
            out = cv2.resize(frame, self.size, interpolation=cv2.INTER_AREA)
            if self.invert:
                cv2.bitwise_not(out, out)
            with self._cond:
                self._buf.append((index, out))
                self._last_index = index
                self._cond.notify_all()
            index += 1
        with self._cond:
            self._eof = True
            self._cond.notify_all()

    def frame_at(self, t):
        """
        Frame (já reduzido/invertido) do instante t. Nunca bloqueia: se o
        decodificador ainda não chegou em t, devolve o último frame pronto.
        Só devolve None se nenhum frame foi decodificado ainda.
        """
        target = max(int(t * self.fps), 0)
        with self._cond:
            self._target = target
            while len(self._buf) >= 2 and self._buf[1][0] <= target:
                self._buf.popleft()
            self._cond.notify_all()
            return self._buf[0][1] if self._buf else None

    def finished(self, t):
        """True quando o vídeo acabou e o relógio já passou do último frame."""
        with self._cond:
            return self._eof and int(t * self.fps) > self._last_index

    def stop(self):
        self._halt.set()
        with self._cond:
            self._cond.notify_all()
        self._thread.join(timeout=2)
        self.cap.release()


# ---------------------------------------------------------------------------
# Medidas
# ---------------------------------------------------------------------------

@dataclass
class LoopStats:
    """FPS do laço de desenho e do detector, para achar gargalos (mostrado com a tecla D)."""
    window: float = 1.0
    _t0: float = field(default_factory=time.perf_counter)
    _frames: int = 0
    fps: float = 0.0

    def tick(self):
        self._frames += 1
        now = time.perf_counter()
        if now - self._t0 >= self.window:
            self.fps = self._frames / (now - self._t0)
            self._frames, self._t0 = 0, now
