"""
Catalogar danças: transforma um vídeo de dança numa dança jogável.

O vídeo é separado em MÚSICA (mp3) e DANÇA (vídeo sem áudio), e os movimentos
dos dançarinos são capturados e salvos como coreografia.

Exemplos:
    python train.py --video meu_video.mp4 --name "Sofrência do Verão"
    python train.py --video clipe.mp4 --name "Pop Retrô" --start 12 --end 95
    python train.py --video ensaio.mp4 --name "Funk" --music estudio.mp3 --music-offset 1.5
    python train.py --video v.mp4 --name "Duo" --max-people 2 --workers 4
    python train.py --list
    python train.py --remove "Pop Retrô"
    python train.py --rebuild-index

Com --machine a saída vira linhas "JD|tipo|dados" fáceis de ler pela Unity.
"""
import argparse
import json
import sys
import time

import config  # noqa: F401  (antes do cv2)

import library
from catalog import CatalogError, ingest_dance
from config import MAX_TRAINING_DETECTIONS


def _fmt_time(seconds):
    return f"{int(seconds // 60)}:{int(seconds % 60):02d}"


def show_catalog():
    entries = library.list_entries()
    if not entries:
        print("Nenhuma dança catalogada ainda.")
    for i, e in enumerate(entries, start=1):
        n = e["num_people"]
        print(f"{i}. {e['name']}  ({n} {'pessoa' if n == 1 else 'pessoas'}"
              f" · {e['difficulty']} · {_fmt_time(e['duration_sec'])})")
    legacy = library.count_legacy_entries()
    if legacy:
        print(f"\n{legacy} dança(s) em formato antigo foram ignoradas. Refaça com --video.")


def main():
    # Com a saída redirecionada (Unity), o Windows usaria a página de código local e
    # quebraria nos acentos. Força UTF-8.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass

    p = argparse.ArgumentParser(description="Cataloga danças a partir de vídeos.")
    p.add_argument("--video", help="Vídeo com a dança (e, normalmente, a música).")
    p.add_argument("--name", help="Nome da música/dança na galeria.")
    p.add_argument("--music", help="Música para usar no lugar do áudio do vídeo.")
    p.add_argument("--music-offset", type=float, default=0.0,
                   help="Segundos da música anexada em que a dança começa "
                        "(negativo = a música começa depois do vídeo).")
    p.add_argument("--start", type=float, default=0.0, help="Início do trecho do vídeo (s).")
    p.add_argument("--end", type=float, default=None, help="Fim do trecho do vídeo (s).")
    p.add_argument("--max-people", type=int, default=MAX_TRAINING_DETECTIONS,
                   help="Máximo de dançarinos a rastrear (quanto menor, menos trabalho por frame).")
    p.add_argument("--workers", type=int, default=None,
                   help="Processos em paralelo na captura (0 = automático, 1 = sem paralelismo).")
    p.add_argument("--model", choices=("lite", "full", "heavy"), default=None,
                   help="Modelo de pose: lite (rápido), full (padrão), heavy (mais preciso).")
    p.add_argument("--overwrite", action="store_true", help="Substitui uma dança de mesmo nome.")
    p.add_argument("--machine", action="store_true", help="Saída em linhas JD|tipo|dados (para a Unity).")
    p.add_argument("--list", action="store_true", help="Lista as danças catalogadas.")
    p.add_argument("--remove", metavar="NOME", help="Remove uma dança do catálogo.")
    p.add_argument("--rebuild-index", action="store_true", help="Refaz o library.json.")
    args = p.parse_args()

    if args.list:
        return show_catalog()
    if args.rebuild_index:
        print(f"Índice refeito: {library.rebuild_index()} dança(s).")
        return
    if args.remove:
        ok = library.remove(args.remove)
        print("Removida." if ok else "Nenhuma dança com esse nome.")
        return

    if not args.video:
        p.error("informe --video (ou use --list / --remove / --rebuild-index)")

    name = args.name or input("Nome da música: ").strip()

    last = {"pct": -1, "t": 0.0}

    def progress(done, total):
        pct = min(100, int(100 * done / total))
        if args.machine:
            if pct != last["pct"]:
                print(f"JD|progress|{pct / 100:.2f}", flush=True)
        else:
            bar = "#" * (pct // 4) + "-" * (25 - pct // 4)
            sys.stdout.write(f"\r      [{bar}] {pct}%")
            sys.stdout.flush()
            if done >= total:
                sys.stdout.write("\n")
        last["pct"] = pct

    def log(message):
        print(f"JD|stage|{message}" if args.machine else message, flush=True)

    stats = {}
    t0 = time.perf_counter()
    try:
        entry, warnings = ingest_dance(
            args.video, name,
            music_path=args.music, music_offset=args.music_offset,
            start=args.start, end=args.end, max_people=args.max_people,
            overwrite=args.overwrite, progress=progress, log=log,
            workers=args.workers, model=args.model, stats=stats,
        )
    except CatalogError as e:
        print(f"JD|error|{e}" if args.machine else f"\nErro: {e}", flush=True)
        sys.exit(1)

    if args.machine:
        for w in warnings:
            print(f"JD|warn|{w}", flush=True)
        print(f"JD|done|{json.dumps(entry, ensure_ascii=False)}", flush=True)
        return

    n = entry["num_people"]
    origem = "do próprio vídeo" if entry["music_source"] == "video" else "anexada"
    print(f"\nDança catalogada: {entry['name']}")
    print(f"  Música:     {entry['audio_path']}  ({origem})")
    print(f"  Dança:      {entry['video_path']}  (sem áudio, {entry['fps']:g} fps)")
    print(f"  Movimentos: {entry['poses_path']}")
    print(f"  Dançarinos: {n}   Dificuldade: {entry['difficulty']} "
          f"(intensidade {entry['movement_intensity']}°/s)   Duração: {_fmt_time(entry['duration_sec'])}")
    if stats:
        fps_proc = stats["frames"] / max(stats["wall_s"], 1e-9)
        print(f"  Captura:    {stats['frames']} frames em {stats['wall_s']:.1f}s "
              f"({fps_proc:.1f} frames/s, {stats['workers']} processo(s)); "
              f"total {time.perf_counter() - t0:.1f}s")
    for w in warnings:
        print(f"  Aviso: {w}")


if __name__ == "__main__":
    main()
