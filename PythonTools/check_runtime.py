"""
Confere se o Python deste projeto tem tudo que o jogo precisa e, opcionalmente,
baixa os modelos de pose e faz um teste real do MediaPipe.

    python check_runtime.py                         # confere as bibliotecas
    python check_runtime.py --models lite full      # + baixa os modelos que faltam
    python check_runtime.py --smoke lite            # + roda o detector de verdade numa imagem em branco

Imprime linhas "JD|check|..." que a Unity mostra no console e termina com
"JD|runtime|ok" (código 0) ou "JD|runtime|falhou" (código 1).
"""
import argparse
import importlib
import os
import sys
import time

import config  # noqa: F401


def say(msg):
    print(f"JD|check|{msg}", flush=True)


def check_libs():
    ok = True
    say(f"Python {sys.version.split()[0]} em {sys.executable}")
    for name in ("numpy", "cv2", "mediapipe", "imageio_ffmpeg"):
        try:
            module = importlib.import_module(name)
            say(f"{name} {getattr(module, '__version__', '')} ok")
        except Exception as e:                          # noqa: BLE001
            say(f"{name} FALTANDO ou quebrado: {e}")
            ok = False
    try:
        import mediapipe as mp
        mp.tasks.vision.PoseLandmarker           # noqa: B018
    except Exception as e:                              # noqa: BLE001
        say(f"mediapipe sem a API PoseLandmarker: {e}")
        ok = False
    try:
        import imageio_ffmpeg
        exe = imageio_ffmpeg.get_ffmpeg_exe()
        say(f"ffmpeg: {exe} ({'ok' if os.path.exists(exe) else 'NÃO ENCONTRADO'})")
        ok = ok and os.path.exists(exe)
    except Exception:                                   # noqa: BLE001
        ok = False
    try:
        import pygame                                    # noqa: F401
        say("pygame ok (só para o play.py de teste)")
    except Exception:                                   # noqa: BLE001
        say("pygame ausente: o play.py de teste rodaria sem som (a Unity não usa)")
    return ok


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--models", nargs="*", choices=("lite", "full", "heavy"), default=[],
                   help="baixa estes modelos de pose se ainda não existirem")
    p.add_argument("--smoke", choices=("lite", "full", "heavy"),
                   help="cria o detector e roda numa imagem em branco (confirma que o MediaPipe funciona)")
    args = p.parse_args()

    ok = check_libs()

    if ok:
        from pose_utils import ModelError, create_landmarker, ensure_model, model_path
        for name in args.models:
            try:
                ensure_model(name)
                say(f"modelo {name}: {model_path(name)} ok")
            except ModelError as e:
                say(f"modelo {name}: {e}")
                ok = False
        if args.smoke and ok:
            import numpy as np
            from pose_utils import detect
            try:
                landmarker = create_landmarker(2, args.smoke)
                blank = np.zeros((360, 640, 3), np.uint8)
                t0 = time.perf_counter()
                for i in range(5):
                    found = detect(landmarker, blank, i * 33)
                ms = (time.perf_counter() - t0) / 5 * 1000
                landmarker.close()
                say(f"MediaPipe [{args.smoke}] funcionando: {ms:.0f} ms/frame numa imagem vazia, "
                    f"{len(found)} pessoa(s) (esperado: 0)")
            except Exception as e:                      # noqa: BLE001
                say(f"MediaPipe [{args.smoke}] falhou: {e}")
                ok = False

    print("JD|runtime|ok" if ok else "JD|runtime|falhou", flush=True)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
