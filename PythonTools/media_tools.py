"""
Separação de mídia com ffmpeg (o binário vem junto do pacote imageio-ffmpeg,
não precisa instalar nada no sistema).

Um vídeo de dança traz duas coisas misturadas: a MÚSICA (trilha de áudio) e a
DANÇA (imagens). Aqui elas são separadas em dois arquivos independentes:

  music.mp3  -> só o áudio, tocado pelo jogo como relógio mestre
  dance.mp4  -> só a imagem (sem áudio), em fps constante e H.264

O vídeo mudo em fps constante garante que o frame N sempre corresponde ao
instante N/fps, que é o que alinha as poses com a música.
"""
import os
import re
import subprocess
from dataclasses import dataclass

import cv2
import imageio_ffmpeg

from config import MAX_VIDEO_FPS, MAX_VIDEO_HEIGHT


class MediaError(RuntimeError):
    """Erro ao ler ou converter um arquivo de mídia."""


@dataclass
class MediaInfo:
    duration: float
    has_video: bool
    has_audio: bool
    width: int
    height: int
    fps: float


def ffmpeg_exe():
    return imageio_ffmpeg.get_ffmpeg_exe()


def _run(args):
    cmd = [ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y", *args]
    proc = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
    if proc.returncode != 0:
        raise MediaError(proc.stderr.strip() or "o ffmpeg falhou sem mensagem")


def probe(path):
    """Lê duração e streams do arquivo (via `ffmpeg -i`, que imprime o resumo)."""
    if not os.path.isfile(path):
        raise MediaError(f"Arquivo não encontrado: {path}")

    proc = subprocess.run(
        [ffmpeg_exe(), "-hide_banner", "-i", path],
        capture_output=True, text=True, errors="replace",
    )
    text = proc.stderr

    m = re.search(r"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", text)
    if not m:
        raise MediaError(f"Não consegui ler o arquivo (formato inválido?): {path}")
    duration = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))

    video_line = re.search(r"Stream #\d+:\d+.*?: Video:.*", text)
    has_audio = re.search(r"Stream #\d+:\d+.*?: Audio:", text) is not None

    width = height = 0
    fps = 0.0
    if video_line:
        line = video_line.group(0)
        size = re.search(r"\b(\d{2,5})x(\d{2,5})\b", line)
        if size:
            width, height = int(size.group(1)), int(size.group(2))
        rate = re.search(r"(\d+(?:\.\d+)?) fps", line) or re.search(r"(\d+(?:\.\d+)?) tbr", line)
        if rate:
            fps = float(rate.group(1))

    return MediaInfo(duration, video_line is not None, has_audio, width, height, fps)


def target_fps(source_fps):
    """fps do vídeo normalizado: o do original, limitado a MAX_VIDEO_FPS."""
    if source_fps <= 0:
        return float(MAX_VIDEO_FPS)
    return float(min(MAX_VIDEO_FPS, round(source_fps)))


def make_dance_video(src, dst, *, fps, start=0.0, duration=None):
    """
    Gera o vídeo da dança: SEM áudio, fps constante, H.264 yuv420p, altura
    limitada. Toca em qualquer lugar (OpenCV, Unity VideoPlayer).
    """
    args = []
    if start > 0:
        args += ["-ss", f"{start:.3f}"]
    args += ["-i", src]
    if duration is not None:
        args += ["-t", f"{duration:.3f}"]
    args += [
        "-map", "0:v:0",
        "-an",
        # O filtro fps= (e não a opção -r) mantém cada frame no instante certo.
        # Com -r, 60 fps -> 30 fps atrasava a imagem ~50 ms em relação à música.
        "-vf", f"scale=-2:'min({MAX_VIDEO_HEIGHT},ih)',fps={fps:g}",
        "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
        "-pix_fmt", "yuv420p",
        "-movflags", "+faststart",
        dst,
    ]
    _run(args)


def make_music(src, dst, *, start=0.0, delay=0.0, duration=None):
    """
    Gera a música (mp3) a partir do áudio de `src` (o próprio vídeo ou um
    arquivo de música anexado).

    start: segundos a pular no começo de `src`.
    delay: segundos de silêncio a ADICIONAR no começo (para música anexada que
           começa depois do vídeo).
    duration: corta a música nesse comprimento (normalmente a duração da dança).
    """
    args = []
    if start > 0:
        args += ["-ss", f"{start:.3f}"]
    args += ["-i", src]
    if duration is not None:
        args += ["-t", f"{duration:.3f}"]
    args += ["-vn", "-map", "0:a:0"]
    if delay > 0:
        args += ["-af", f"adelay={int(delay * 1000)}:all=1"]
    args += ["-c:a", "libmp3lame", "-q:a", "2", "-ar", "44100", dst]
    _run(args)


def make_thumbnail(video_path, dst, *, at_fraction=0.15, width=480):
    """Salva uma miniatura (jpg) tirada de um ponto do vídeo."""
    cap = cv2.VideoCapture(video_path)
    try:
        total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        if total > 0:
            cap.set(cv2.CAP_PROP_POS_FRAMES, int(total * at_fraction))
        ok, frame = cap.read()
        if not ok:
            return False
        h, w = frame.shape[:2]
        frame = cv2.resize(frame, (width, max(1, int(h * width / w))))
        return bool(cv2.imwrite(dst, frame, [cv2.IMWRITE_JPEG_QUALITY, 85]))
    finally:
        cap.release()
