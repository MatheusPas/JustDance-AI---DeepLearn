"""
Jogo (protótipo em Python): escolhe a música na galeria, faz a contagem 3-2-1 e dança.

  - bloco de cima: vídeo da dança (sem áudio) com efeito negativo
  - bloco de baixo: câmera dos jogadores (espelhada), com esqueleto e score

A MÚSICA é o relógio mestre. O vídeo da dança e as poses de referência são
consultados pelo tempo da música, então imagem, som e pontuação não se separam.

Desempenho: o laço que desenha a tela nunca espera por nada lento.
  thread da câmera     -> guarda só o frame mais recente (sem fila, sem atraso acumulado)
  thread do detector   -> detecta as poses no frame mais novo; frames atrasados são descartados
  thread do vídeo      -> decodifica a dança à frente, já reduzida e em negativo
O score compara o jogador com a dança no instante em que o FRAME foi capturado
(e não no instante em que a detecção terminou), então o atraso do detector não
vira erro de pontuação.

Uso:
    python play.py              (tecla D mostra fps/latência, Q ou Esc sai)
"""
import time

import config  # noqa: F401  (antes do cv2)
import cv2
import numpy as np

import library
from choreography import ChoreographyError, load_choreography
from config import LIVE_DETECT_MAX_WIDTH, LIVE_MODEL, SCORE_TIME_OFFSET
from pose_utils import (
    L_ANKLE, L_ELBOW, L_HIP, L_KNEE, L_SHOULDER, L_WRIST, NOSE, POINT_INDICES, R_ANKLE,
    R_ELBOW, R_HIP, R_KNEE, R_SHOULDER, R_WRIST, MediaPipeDetector, ModelError, ensure_model,
    score_angles,
)
from realtime import LatestFrameCamera, LoopStats, PoseWorker, ReferenceStream, open_camera

WINDOW_NAME = "Just Dance AI"
BLOCK_WIDTH = 640
BLOCK_HEIGHT = 360
TARGET_FPS = 60

_P = {lm: i for i, lm in enumerate(POINT_INDICES)}
_BONES = [(_P[a], _P[b]) for a, b in [
    (L_SHOULDER, R_SHOULDER), (L_HIP, R_HIP), (L_SHOULDER, L_HIP), (R_SHOULDER, R_HIP),
    (L_SHOULDER, L_ELBOW), (L_ELBOW, L_WRIST), (R_SHOULDER, R_ELBOW), (R_ELBOW, R_WRIST),
    (L_HIP, L_KNEE), (L_KNEE, L_ANKLE), (R_HIP, R_KNEE), (R_KNEE, R_ANKLE), (NOSE, L_SHOULDER),
]]
_HIPS = (_P[L_HIP], _P[R_HIP])
_COLORS = [(0, 255, 0), (0, 200, 255), (255, 120, 0), (255, 0, 200), (0, 255, 255), (200, 255, 0)]


# ---------------------------------------------------------------------------
# Menu
# ---------------------------------------------------------------------------

def _fmt_time(seconds):
    return f"{int(seconds // 60)}:{int(seconds % 60):02d}"


def choose_song():
    entries = library.list_entries()
    if not entries:
        print("Nenhuma dança catalogada. Rode 'python train.py --video ... --name ...' primeiro.")
        legacy = library.count_legacy_entries()
        if legacy:
            print(f"({legacy} dança(s) em formato antigo foram ignoradas; refaça com train.py.)")
        return None

    print("\n=== Galeria de músicas ===")
    for i, e in enumerate(entries, start=1):
        n = e["num_people"]
        print(f"{i}. {e['name']}  ({n} {'pessoa' if n == 1 else 'pessoas'}"
              f" · {e['difficulty']} · {_fmt_time(e['duration_sec'])})")

    while True:
        choice = input("Escolha o número da música: ").strip()
        if choice.isdigit() and 1 <= int(choice) <= len(entries):
            return entries[int(choice) - 1]
        print("Opção inválida, tente novamente.")


def choose_num_players(max_people):
    if max_people == 1:
        return 1
    while True:
        raw = input(f"Quantos jogadores vão dançar? (1 a {max_people}): ").strip()
        if raw.isdigit() and 1 <= int(raw) <= max_people:
            return int(raw)
        print("Valor inválido, tente novamente.")


# ---------------------------------------------------------------------------
# Vídeo de referência (versão simples e síncrona, usada em testes e como referência)
# ---------------------------------------------------------------------------

class ReferenceVideo:
    """Entrega o frame do vídeo da dança que corresponde ao tempo `t` (segundos)."""

    def __init__(self, path):
        self.cap = cv2.VideoCapture(path)
        if not self.cap.isOpened():
            raise RuntimeError(f"Não foi possível abrir o vídeo da dança: {path}")
        self.fps = self.cap.get(cv2.CAP_PROP_FPS) or 30.0
        self.frames_read = 0
        self.frame = None

    def frame_at(self, t):
        """Frame do tempo t, ou None quando o vídeo acabou. Pula frames se estiver atrasado."""
        target = max(int(t * self.fps), 0)
        while self.frames_read <= target:
            ok, frame = self.cap.read()
            if not ok:
                return None
            self.frame = frame
            self.frames_read += 1
        return self.frame

    def release(self):
        self.cap.release()


class MusicClock:
    """Relógio da música. Sem placa de áudio, cai para o relógio do sistema."""

    def __init__(self, audio_path):
        self._mixer = None
        try:
            import pygame
            pygame.mixer.init()
            pygame.mixer.music.load(audio_path)
            self._mixer = pygame.mixer
        except Exception as e:  # sem dispositivo de áudio, arquivo ilegível etc.
            print(f"Aviso: não consegui tocar a música ({e}). Seguindo sem som.")
        self._t0 = None

    def start(self):
        if self._mixer:
            self._mixer.music.play()
        self._t0 = time.perf_counter()

    def now(self):
        if self._mixer:
            pos = self._mixer.music.get_pos()      # ms tocados; -1 se não está tocando
            if pos >= 0:
                return pos / 1000.0
        return time.perf_counter() - self._t0

    def stop(self):
        if self._mixer:
            self._mixer.music.stop()


# ---------------------------------------------------------------------------
# Desenho (tudo numa única imagem pré-alocada: sem alocar memória a cada frame)
# ---------------------------------------------------------------------------

class Screen:
    def __init__(self):
        self.canvas = np.zeros((2 * BLOCK_HEIGHT, BLOCK_WIDTH, 3), np.uint8)
        self.top = self.canvas[:BLOCK_HEIGHT]
        self.bottom = self.canvas[BLOCK_HEIGHT:]
        self.preview = np.zeros((BLOCK_HEIGHT, BLOCK_WIDTH, 3), np.uint8)   # câmera sem desenhos
        self.view = (0, 0, BLOCK_WIDTH, BLOCK_HEIGHT)                        # onde a câmera aparece

    def set_camera(self, frame_bgr):
        """Mostra o frame da câmera dentro do bloco de baixo, sem deformar a imagem."""
        h, w = frame_bgr.shape[:2]
        scale = min(BLOCK_WIDTH / w, BLOCK_HEIGHT / h)
        vw, vh = max(2, round(w * scale)), max(2, round(h * scale))
        x0, y0 = (BLOCK_WIDTH - vw) // 2, (BLOCK_HEIGHT - vh) // 2
        self.view = (x0, y0, vw, vh)
        self.preview[y0:y0 + vh, x0:x0 + vw] = cv2.resize(frame_bgr, (vw, vh), interpolation=cv2.INTER_AREA)

    def compose(self, reference_frame):
        if reference_frame is not None:
            self.top[:] = reference_frame
        else:
            self.top[:] = 0
        self.bottom[:] = self.preview

    def to_pixels(self, points):
        """Pontos normalizados (0..1 do frame da câmera) -> pixels do canvas inteiro."""
        x0, y0, vw, vh = self.view
        return int(x0 + points[0] * vw), int(BLOCK_HEIGHT + y0 + points[1] * vh)

    def draw_player(self, pid, pts):
        color = _COLORS[pid % len(_COLORS)]
        px = [self.to_pixels(p) for p in pts]
        valid = [not np.isnan(p).any() for p in pts]
        for a, b in _BONES:
            if valid[a] and valid[b]:
                cv2.line(self.canvas, px[a], px[b], color, 2, cv2.LINE_AA)
        if all(valid[i] for i in _HIPS):
            cx = (px[_HIPS[0]][0] + px[_HIPS[1]][0]) // 2
            cy = (px[_HIPS[0]][1] + px[_HIPS[1]][1]) // 2
            cv2.circle(self.canvas, (cx, cy), 16, color, 2, cv2.LINE_AA)
            cv2.putText(self.canvas, str(pid + 1), (cx - 6, cy + 6),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2, cv2.LINE_AA)

    def draw_scoreboard(self, last, totals):
        rows = sorted(last)
        cv2.rectangle(self.canvas, (0, BLOCK_HEIGHT), (260, BLOCK_HEIGHT + 24 * len(rows) + 14), (0, 0, 0), -1)
        for row, pid in enumerate(rows):
            score = last[pid]
            vals = totals[pid]
            now = "--" if score is None else f"{score:.0f}"
            avg = f"{sum(vals) / len(vals):.0f}" if vals else "--"
            cv2.putText(self.canvas, f"Jogador {pid + 1}: {now}  (media {avg})",
                        (8, BLOCK_HEIGHT + 24 + row * 24), cv2.FONT_HERSHEY_SIMPLEX, 0.5,
                        _COLORS[pid % len(_COLORS)], 1, cv2.LINE_AA)

    def draw_debug(self, lines):
        for i, line in enumerate(lines):
            cv2.putText(self.canvas, line, (BLOCK_WIDTH - 250, 18 + i * 18),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1, cv2.LINE_AA)

    def show(self):
        cv2.imshow(WINDOW_NAME, self.canvas)


def show_countdown(screen, reference, camera=None, steps=("3", "2", "1", "Vai!"), step_seconds=0.8):
    """Contagem 3-2-1. A câmera já aparece no bloco de baixo para o jogador se posicionar."""
    cam_seq = -1
    for text in steps:
        end = time.perf_counter() + step_seconds
        while time.perf_counter() < end:
            if camera is not None:
                item = camera.read_latest(cam_seq)
                if item:
                    cam_seq = item[0]
                    screen.set_camera(item[1])
            screen.compose(reference.frame_at(0))
            size = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, 4, 6)[0]
            pos = ((BLOCK_WIDTH - size[0]) // 2, (BLOCK_HEIGHT + size[1]) // 2)
            cv2.putText(screen.canvas, text, pos, cv2.FONT_HERSHEY_SIMPLEX, 4, (255, 255, 255), 6, cv2.LINE_AA)
            screen.show()
            cv2.waitKey(15)


# ---------------------------------------------------------------------------
# Jogo
# ---------------------------------------------------------------------------

def play_dance(entry, num_players, *, camera=None, detector=None, time_offset=None):
    """`camera` e `detector` podem ser injetados (testes); por padrão usa webcam e MediaPipe."""
    try:
        choreo = load_choreography(library.resolve(entry["poses_path"]))
    except (ChoreographyError, OSError) as e:
        print(f"Não consegui carregar os movimentos: {e}")
        return None
    offset = SCORE_TIME_OFFSET if time_offset is None else time_offset

    capture = camera or open_camera()
    if capture is None or not capture.isOpened():
        print("Não foi possível abrir a câmera.")
        return None

    if detector is None:
        try:
            ensure_model(LIVE_MODEL)
            detector = MediaPipeDetector(num_players, model=LIVE_MODEL)
        except ModelError as e:
            print(f"Erro: {e}")
            capture.release()
            return None

    reference = ReferenceStream(library.resolve(entry["video_path"]), (BLOCK_WIDTH, BLOCK_HEIGHT)).start()
    cam = LatestFrameCamera(capture, mirror=True, max_fps=TARGET_FPS if camera is not None else None).start()
    worker = PoseWorker(detector, num_players, input_format="bgr", max_width=LIVE_DETECT_MAX_WIDTH).start()
    clock = MusicClock(library.resolve(entry["audio_path"]))
    screen = Screen()
    stats = LoopStats()

    show_countdown(screen, reference, cam)

    totals = {pid: [] for pid in range(num_players)}
    last_score = {pid: None for pid in range(num_players)}
    last_players = {}
    latency_ms = 0.0
    debug = False
    cam_seq = -1
    frame_period = 1.0 / TARGET_FPS
    next_tick = time.perf_counter()

    clock.start()
    try:
        while True:
            t = clock.now()
            if t >= choreo.duration or reference.finished(t):
                break
            if worker.error:
                print(f"Erro na detecção: {worker.error}")
                break
            if cam.ended and cam.read_latest(cam_seq) is None:
                break

            item = cam.read_latest(cam_seq)
            if item:
                cam_seq, frame, t_cap = item
                worker.submit(cam_seq, frame, t_cap)        # não espera: frame novo substitui o antigo
                screen.set_camera(frame)

            result = worker.poll()
            if result:
                # instante da MÚSICA em que o frame foi capturado (e não "agora")
                t_frame = t - (time.perf_counter() - result.t_capture)
                latency_ms = (time.perf_counter() - result.t_capture) * 1000.0
                ref_angles = choreo.angles_at(t_frame - offset)
                last_players = result.players
                for pid, (angles, _points) in result.players.items():
                    ref = ref_angles.get(pid)
                    score = None if ref is None else score_angles(angles, ref)
                    last_score[pid] = score
                    if score is not None:
                        totals[pid].append(score)
                for pid in range(num_players):
                    if pid not in result.players:
                        last_score[pid] = None

            screen.compose(reference.frame_at(t))
            for pid, (_angles, points) in last_players.items():
                screen.draw_player(pid, points)
            screen.draw_scoreboard(last_score, totals)
            if debug:
                screen.draw_debug([
                    f"tela: {stats.fps:.0f} fps",
                    f"camera: {cam.captured} frames",
                    f"detector: {worker.detect_ms_avg:.0f} ms",
                    f"latencia: {latency_ms:.0f} ms",
                    f"descartados: {worker.dropped}",
                ])
            screen.show()
            stats.tick()

            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break
            if key == ord("d"):
                debug = not debug

            next_tick += frame_period
            sleep = next_tick - time.perf_counter()
            if sleep > 0:
                time.sleep(sleep)
            else:
                next_tick = time.perf_counter()     # atrasado: não tenta "recuperar" frames de tela
    finally:
        clock.stop()
        cam.stop()
        worker.stop()
        reference.stop()
        cv2.destroyAllWindows()

    print("\n=== Pontuação final ===")
    for pid, values in totals.items():
        avg = sum(values) / len(values) if values else 0.0
        print(f"Jogador {pid + 1}: {avg:.1f}")
    return {pid: (sum(v) / len(v) if v else 0.0) for pid, v in totals.items()}


def main():
    entry = choose_song()
    if entry:
        play_dance(entry, choose_num_players(entry["num_people"]))


if __name__ == "__main__":
    main()
