"""
Vídeo de dança SINTÉTICO para testar o pipeline sem precisar de pessoas reais.

Dois bonecos de palito dançam (balançam os braços). Cada articulação é desenhada
como uma bolinha de cor única, e `vision_detector` encontra as bolinhas pela
cor, como se fosse um detector de pose lendo a imagem. Assim o teste confere de
verdade o alinhamento entre o vídeo (depois da reconversão do ffmpeg) e as poses.
"""
import math
import os
import random
import subprocess

import cv2
import numpy as np

import media_tools
from pose_utils import (
    L_ANKLE, L_ELBOW, L_HIP, L_KNEE, L_SHOULDER, L_WRIST, NOSE, POINT_INDICES,
    R_ANKLE, R_ELBOW, R_HIP, R_KNEE, R_SHOULDER, R_WRIST,
)

W, H = 1280, 720
SOURCE_FPS = 60
CENTERS = [320, 960]            # x dos dois dançarinos (px)
DROPOUT = (3.0, 3.3)            # dançarino 1 some da imagem neste intervalo (s)
SPURIOUS_AT_MS = 2000           # um falso positivo de 1 frame, injetado no detector


class Lm:
    __slots__ = ("x", "y", "visibility")

    def __init__(self, x=0.0, y=0.0, visibility=0.0):
        self.x, self.y, self.visibility = x, y, visibility


def joints_px(k, t):
    """Posição (px) de cada articulação do dançarino k no tempo t. Pernas do 0 ficam 'fora do quadro'."""
    cx, cy = CENTERS[k], 380
    ph = k * 1.1
    th_l = math.radians(50 + 40 * math.sin(2 * math.pi * t / 2.0 + ph))
    th_r = math.radians(50 + 40 * math.sin(2 * math.pi * t / 2.0 + ph + 1.7))
    be_l = math.radians(40 + 30 * math.sin(2 * math.pi * t / 1.5 + ph))
    be_r = math.radians(40 + 30 * math.sin(2 * math.pi * t / 1.5 + ph + 0.9))
    sway = 20 * math.sin(2 * math.pi * t / 1.2) if k == 1 else 0

    p = {
        NOSE: (cx, cy - 230),
        L_SHOULDER: (cx + 55, cy - 150), R_SHOULDER: (cx - 55, cy - 150),
        L_HIP: (cx + 35, cy), R_HIP: (cx - 35, cy),
        L_KNEE: (cx + 35 + sway, cy + 140), R_KNEE: (cx - 35 + sway, cy + 140),
        L_ANKLE: (cx + 35, cy + 280), R_ANKLE: (cx - 35, cy + 280),
    }
    ls, rs = p[L_SHOULDER], p[R_SHOULDER]
    p[L_ELBOW] = (ls[0] + 90 * math.sin(th_l), ls[1] + 90 * math.cos(th_l))
    p[L_WRIST] = (p[L_ELBOW][0] + 90 * math.sin(th_l + be_l), p[L_ELBOW][1] + 90 * math.cos(th_l + be_l))
    p[R_ELBOW] = (rs[0] - 90 * math.sin(th_r), rs[1] + 90 * math.cos(th_r))
    p[R_WRIST] = (p[R_ELBOW][0] - 90 * math.sin(th_r + be_r), p[R_ELBOW][1] + 90 * math.cos(th_r + be_r))
    return p


def visible_joints(k):
    legs = {L_KNEE, R_KNEE, L_ANKLE, R_ANKLE}
    return [i for i in POINT_INDICES if not (k == 0 and i in legs)]


def is_present(k, t):
    return not (k == 1 and DROPOUT[0] <= t < DROPOUT[1])


def ground_truth_landmarks(k, t):
    """Landmarks (0..1) do dançarino k no tempo t, como um detector perfeito devolveria."""
    p = joints_px(k, t)
    lms = [Lm() for _ in range(33)]
    for idx in visible_joints(k):
        lms[idx] = Lm(p[idx][0] / W, p[idx][1] / H, 1.0)
    return lms


def _hue(j):
    return 6 + 13 * j


BONES = [(L_SHOULDER, R_SHOULDER), (L_HIP, R_HIP), (L_SHOULDER, L_HIP), (R_SHOULDER, R_HIP),
         (L_SHOULDER, L_ELBOW), (L_ELBOW, L_WRIST), (R_SHOULDER, R_ELBOW), (R_ELBOW, R_WRIST),
         (L_HIP, L_KNEE), (L_KNEE, L_ANKLE), (R_HIP, R_KNEE), (R_KNEE, R_ANKLE), (NOSE, L_SHOULDER)]


def render_frame(t):
    img = np.full((H, W, 3), (30, 20, 20), np.uint8)
    for k in (0, 1):
        if not is_present(k, t):
            continue
        p = joints_px(k, t)
        vis = set(visible_joints(k))
        for a, b in BONES:
            if a in vis and b in vis:
                cv2.line(img, tuple(int(v) for v in p[a]), tuple(int(v) for v in p[b]), (130, 130, 130), 6)
        for j, idx in enumerate(POINT_INDICES):
            if idx in vis:
                bgr = cv2.cvtColor(np.uint8([[[_hue(j), 255, 255]]]), cv2.COLOR_HSV2BGR)[0, 0]
                cv2.circle(img, tuple(int(v) for v in p[idx]), 9, tuple(int(c) for c in bgr), -1)
    return img


def make_source_video(path, seconds=6.0, with_audio=True, tone_hz=440):
    """Cria um vídeo 1280x720 a 60 fps (dois dançarinos), com ou sem trilha de tom."""
    silent = path + ".silent.mp4"
    writer = cv2.VideoWriter(silent, cv2.VideoWriter_fourcc(*"mp4v"), SOURCE_FPS, (W, H))
    for n in range(int(seconds * SOURCE_FPS)):
        writer.write(render_frame(n / SOURCE_FPS))
    writer.release()

    if with_audio:
        cmd = [media_tools.ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y", "-i", silent,
               "-f", "lavfi", "-i", f"sine=frequency={tone_hz}:duration={seconds}",
               "-c:v", "copy", "-c:a", "aac", "-shortest", path]
        subprocess.run(cmd, check=True)
        os.remove(silent)
    else:
        os.replace(silent, path)


def make_tone(path, seconds, hz):
    cmd = [media_tools.ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y",
           "-f", "lavfi", "-i", f"sine=frequency={hz}:duration={seconds}", path]
    subprocess.run(cmd, check=True)


def make_vision_detector(seed=1):
    """Detector falso que LÊ A IMAGEM: acha as bolinhas coloridas e monta os landmarks."""
    rng = random.Random(seed)

    def detect(frame_rgb, ts_ms):
        hsv = cv2.cvtColor(frame_rgb, cv2.COLOR_RGB2HSV)
        h, w = frame_rgb.shape[:2]
        persons = {0: {}, 1: {}}
        for j, idx in enumerate(POINT_INDICES):
            mask = cv2.inRange(hsv, (_hue(j) - 5, 150, 150), (_hue(j) + 5, 255, 255))
            contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            for c in contours:
                m = cv2.moments(c)
                if m["m00"] < 15:
                    continue
                cx, cy = m["m10"] / m["m00"] / w, m["m01"] / m["m00"] / h
                persons[0 if cx < 0.5 else 1][idx] = (cx, cy)

        detections = []
        for found in persons.values():
            if len(found) >= 5:
                lms = [Lm() for _ in range(33)]
                for idx, (x, y) in found.items():
                    lms[idx] = Lm(x, y, 1.0)
                detections.append(lms)

        if abs(ts_ms - SPURIOUS_AT_MS) < 17:       # falso positivo de um frame
            detections.append([Lm(0.5, 0.5, 1.0) for _ in range(33)])
        rng.shuffle(detections)
        return detections

    return detect


def vision_detector_factory(max_people):
    """Versão sem lambda de make_vision_detector: pode ser enviada a outro processo (spawn)."""
    return make_vision_detector()
