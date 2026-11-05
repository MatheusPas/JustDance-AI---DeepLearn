"""
Mede onde está o gargalo no SEU computador, etapa por etapa.

    python bench.py --video minha_danca.mp4              # catalogação: leitura, detecção, paralelismo
    python bench.py --video minha_danca.mp4 --live       # jogo: o detector acompanha 30 fps?
    python bench.py --camera                              # a webcam entrega quantos fps?
    python bench.py --video v.mp4 --models lite full      # compara modelos

Precisa do modelo de pose (baixa sozinho na primeira vez). Para conferir só a
estrutura, sem o modelo, use --fake (detector sintético, só para desenvolvimento).
"""
import argparse
import functools
import os
import sys
import tempfile
import time

import config  # noqa: F401  (antes do cv2)
import cv2
import numpy as np

import media_tools
from choreography import _FrameFeeder, extract_raw_tracks
from pose_utils import MediaPipeDetector, ensure_model
from realtime import LatestFrameCamera, PoseWorker, open_camera


def make_factory(args, model):
    if args.fake:
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "tests"))
        import synthetic
        return synthetic.vision_detector_factory
    ensure_model(model)
    return functools.partial(MediaPipeDetector, model=model)


def clip(video, seconds):
    """Recorta o começo do vídeo (sem reprocessar o original)."""
    if not seconds:
        return video, None
    info = media_tools.probe(video)
    if info.duration <= seconds:
        return video, None
    tmp = tempfile.mkdtemp(prefix="jd_bench_")
    dst = os.path.join(tmp, "clip.mp4")
    media_tools.make_dance_video(video, dst, fps=media_tools.target_fps(info.fps), duration=seconds)
    return dst, tmp


def bench_decode(video):
    feeder = _FrameFeeder(video, max_side=config.TRAIN_DETECT_MAX_SIDE)
    t0, n = time.perf_counter(), 0
    for _ in feeder:
        n += 1
    wall = time.perf_counter() - t0
    feeder.close()
    print(f"  leitura do vídeo: {n} frames, {1000 * wall / max(n, 1):.1f} ms/frame ({n / wall:.0f} frames/s)")
    return n


def bench_detect(video, factory, label, frames=90):
    detector = factory(config.MAX_TRAINING_DETECTIONS)
    feeder = _FrameFeeder(video, max_side=config.TRAIN_DETECT_MAX_SIDE)
    times = []
    for i, rgb in feeder:
        t0 = time.perf_counter()
        detector(rgb, i * 1000.0 / feeder.fps)
        times.append((time.perf_counter() - t0) * 1000)
        if len(times) >= frames:
            break
    feeder.close()
    close = getattr(detector, "close", None)
    if close:
        close()
    times = np.array(times[5:] or times)      # ignora o aquecimento
    print(f"  detecção [{label}]: mediana {np.median(times):.1f} ms, p95 {np.percentile(times, 95):.1f} ms "
          f"=> até {1000 / np.median(times):.0f} frames/s por processo")
    return float(np.median(times))


def bench_extract(video, factory, workers_list):
    base = None
    for w in workers_list:
        stats = {}
        t0 = time.perf_counter()
        extract_raw_tracks(video, factory, config.MAX_TRAINING_DETECTIONS, workers=w,
                           detect_max_side=config.TRAIN_DETECT_MAX_SIDE, min_chunk_seconds=2.0, stats=stats)
        wall = time.perf_counter() - t0
        base = base or wall
        print(f"  catalogação com {stats['workers']} processo(s): {wall:.1f} s "
              f"({stats['frames'] / wall:.1f} frames/s, {base / wall:.2f}x)")


def bench_live(source, factory, label, seconds=8.0, camera=False):
    """Alimenta o detector com frames a 30 fps (vídeo ou câmera) e vê se ele acompanha."""
    detector = factory(2)
    worker = PoseWorker(detector, 2, input_format="bgr", max_width=config.LIVE_DETECT_MAX_WIDTH).start()
    if camera:
        cap = open_camera()
        if cap is None:
            print("  não consegui abrir a câmera")
            return
        cam = LatestFrameCamera(cap, mirror=True).start()
        nxt = None
    else:
        cap = cv2.VideoCapture(source)
        cam, nxt = None, time.perf_counter()
    t0, sent, seq, last = time.perf_counter(), 0, -1, -1
    while time.perf_counter() - t0 < seconds:
        if cam:
            item = cam.read_latest(last)
            if item:
                last = item[0]
                worker.submit(*item)
                sent += 1
            time.sleep(0.002)
        else:
            ok, frame = cap.read()
            if not ok:
                cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
                continue
            seq += 1
            worker.submit(seq, frame, time.perf_counter())
            sent += 1
            nxt += 1 / 30
            time.sleep(max(0.0, nxt - time.perf_counter()))
    wall = time.perf_counter() - t0
    worker.stop()
    if cam:
        print(f"  câmera: {cam.captured / wall:.1f} fps entregues")
        cam.stop()
    else:
        cap.release()
    rate = worker.processed / wall
    print(f"  [{label}] {sent / wall:.0f} frames/s chegando, {rate:.1f} analisados/s, "
          f"{worker.dropped} descartados, detector {worker.detect_ms_avg:.0f} ms/frame")
    if rate < 20:
        print("  -> o detector não acompanha. Use --model lite, feche outros programas ou reduza os jogadores.")
    else:
        print("  -> ok: o jogo recebe poses a pelo menos 20 vezes por segundo.")


def main():
    p = argparse.ArgumentParser(description="Mede gargalos da catalogação e do jogo.")
    p.add_argument("--video", help="vídeo de dança (dance.mp4 de uma dança catalogada serve)")
    p.add_argument("--seconds", type=float, default=10.0, help="usa só os primeiros N segundos do vídeo")
    p.add_argument("--models", nargs="+", default=["lite", "full"], choices=("lite", "full", "heavy"))
    p.add_argument("--workers", nargs="+", type=int, default=[1, 2, 4])
    p.add_argument("--live", action="store_true", help="simula o jogo (detector em tempo real)")
    p.add_argument("--camera", action="store_true", help="testa a webcam de verdade")
    p.add_argument("--fake", action="store_true", help="detector sintético (sem modelo; só desenvolvimento)")
    args = p.parse_args()

    if args.camera:
        print("Webcam + detector ao vivo:")
        bench_live(None, make_factory(args, config.LIVE_MODEL), config.LIVE_MODEL, camera=True)
        return
    if not args.video:
        p.error("informe --video (ou --camera)")

    video, tmp = clip(args.video, args.seconds)
    print(f"Máquina: {os.cpu_count()} núcleos lógicos")
    print("Leitura:")
    bench_decode(video)
    if args.live:
        print("Jogo ao vivo (detector em thread, frames a 30 fps):")
        for m in args.models:
            bench_live(video, make_factory(args, m), m)
    else:
        print("Detecção:")
        for m in args.models:
            bench_detect(video, make_factory(args, m), m)
        print(f"Catalogação completa (modelo {config.TRAIN_MODEL}):")
        bench_extract(video, make_factory(args, config.TRAIN_MODEL), args.workers)
    if tmp:
        import shutil
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main()
