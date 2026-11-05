"""
Catalogação de uma dança nova a partir de um vídeo.

  vídeo ──► música (music.mp3)          áudio separado; ou outra música anexada
        └─► dança  (dance.mp4)          vídeo sem áudio, fps constante
                └─► movimentos (poses.json)   captura de pose de todos os dançarinos

A dança é montada numa pasta temporária e só vira definitiva (e entra no
catálogo) se tudo der certo — nunca fica uma dança pela metade.
"""
import datetime
import functools
import json
import os
import shutil

import library
import media_tools
from choreography import (
    ChoreographyError, build_choreography, difficulty_label, extract_raw_tracks, save_choreography,
)
from config import (
    DANCES_DIR, FORMAT_VERSION, MAX_TRAINING_DETECTIONS, TRAIN_DETECT_MAX_SIDE, TRAIN_MODEL,
    TRAIN_WORKERS, auto_workers,
)
from pose_utils import MediaPipeDetector, ModelError, ensure_model


class CatalogError(RuntimeError):
    pass


def ingest_dance(video_path, name, *, music_path=None, music_offset=0.0, start=0.0, end=None,
                 max_people=MAX_TRAINING_DETECTIONS, overwrite=False,
                 detector_factory=None, progress=None, log=print,
                 workers=None, model=None, detect_max_side=TRAIN_DETECT_MAX_SIDE, stats=None):
    """
    Cataloga uma dança. Retorna (entry, avisos).

    music_path:   música para usar no lugar do áudio do vídeo.
    music_offset: segundos de início da música anexada em que a dança começa
                  (positivo = pula o começo da música; negativo = adiciona silêncio).
    start / end:  recorta o vídeo (e a música do vídeo) para tirar intro/outro.
    detector_factory: função(max_people) -> detector. Por padrão usa o MediaPipe;
                  existe para testes. Para rodar em paralelo (workers > 1) precisa
                  ser algo que o Python consiga enviar a outro processo.
    workers:      processos em paralelo na captura (None = config; 0 = automático; 1 = sem paralelismo).
    model:        modelo de pose do MediaPipe: lite | full | heavy (padrão: config.TRAIN_MODEL).
    stats:        dict opcional que recebe os tempos de cada etapa.
    """
    name = name.strip()
    if not name:
        raise CatalogError("O nome da música não pode ser vazio.")

    if detector_factory is None:
        model = model or TRAIN_MODEL
        try:
            ensure_model(model)     # falha logo, antes de converter o vídeo
        except (ModelError, ValueError) as e:
            raise CatalogError(str(e)) from e
        detector_factory = functools.partial(MediaPipeDetector, model=model)

    if workers is None:
        workers = TRAIN_WORKERS
    if workers <= 0:
        workers = auto_workers()

    # ---- 1) valida entradas --------------------------------------------------
    try:
        info = media_tools.probe(video_path)
        if not info.has_video:
            raise CatalogError("O arquivo não tem imagem (vídeo).")

        if music_path:
            music_info = media_tools.probe(music_path)
            if not music_info.has_audio:
                raise CatalogError("O arquivo de música anexado não tem áudio.")
            music_source = "attached"
        elif info.has_audio:
            music_source = "video"
        else:
            raise CatalogError(
                "O vídeo não tem trilha de áudio. Anexe a música com --music arquivo.mp3."
            )
    except media_tools.MediaError as e:
        raise CatalogError(str(e)) from e

    end = info.duration if end is None else min(end, info.duration)
    duration = end - start
    if start < 0 or duration <= 0:
        raise CatalogError("Intervalo inválido: --start/--end não cabem na duração do vídeo.")

    slug = library.slugify(name)
    final_dir = library.dance_dir(slug)
    exists = library.get_entry(name) is not None or os.path.exists(final_dir)
    if exists and not overwrite:
        raise CatalogError(f"Já existe uma dança chamada '{name}'. Use --overwrite para substituir.")

    # ---- 2) monta tudo numa pasta temporária -----------------------------------
    tmp_dir = os.path.join(DANCES_DIR, f".tmp_{slug}")
    shutil.rmtree(tmp_dir, ignore_errors=True)
    os.makedirs(tmp_dir)
    warnings = []

    try:
        fps = media_tools.target_fps(info.fps)
        dance_file = os.path.join(tmp_dir, "dance.mp4")
        music_file = os.path.join(tmp_dir, "music.mp3")
        poses_file = os.path.join(tmp_dir, "poses.json")

        log("1/4 Separando a dança (vídeo sem áudio)...")
        media_tools.make_dance_video(video_path, dance_file, fps=fps, start=start, duration=duration)

        log("2/4 Separando a música...")
        if music_source == "video":
            media_tools.make_music(video_path, music_file, start=start, duration=duration)
        else:
            media_tools.make_music(
                music_path, music_file,
                start=max(music_offset, 0.0), delay=max(-music_offset, 0.0), duration=duration,
            )
        music_len = media_tools.probe(music_file).duration
        if music_len < duration - 1.0:
            warnings.append(
                f"A música ({music_len:.0f}s) é mais curta que a dança ({duration:.0f}s)."
            )

        media_tools.make_thumbnail(dance_file, os.path.join(tmp_dir, "thumbnail.jpg"))

        log("3/4 Capturando os movimentos (pode demorar)...")
        raw, real_fps, frame_count = extract_raw_tracks(
            dance_file, detector_factory, max_people, progress,
            workers=workers, detect_max_side=detect_max_side, stats=stats)

        log("4/4 Limpando e salvando a coreografia...")
        choreo = build_choreography(raw, real_fps, frame_count)
        save_choreography(choreo, poses_file)

        for p in choreo.players:
            if p.coverage < 0.7:
                warnings.append(
                    f"Dançarino {p.id + 1} só foi detectado em {p.coverage:.0%} do vídeo "
                    "(saiu do quadro ou está cortado)."
                )

        intensity = sum(p.intensity for p in choreo.players) / choreo.num_people
        folder = library.to_relative(final_dir)
        entry = {
            "format_version": FORMAT_VERSION,
            "name": name,
            "slug": slug,
            "folder": folder,
            "audio_path": f"{folder}/music.mp3",
            "video_path": f"{folder}/dance.mp4",
            "poses_path": f"{folder}/poses.json",
            "thumbnail_path": f"{folder}/thumbnail.jpg",
            "fps": choreo.fps,
            "total_frames": choreo.frame_count,
            "duration_sec": round(choreo.duration, 2),
            "num_people": choreo.num_people,
            "movement_intensity": round(intensity, 1),
            "difficulty": difficulty_label(intensity),
            "music_source": music_source,
            "created_at": datetime.datetime.now().isoformat(timespec="seconds"),
        }
        with open(os.path.join(tmp_dir, "meta.json"), "w", encoding="utf-8") as f:
            json.dump(entry, f, indent=2, ensure_ascii=False)

        # ---- 3) publica: troca a pasta temporária pela definitiva ---------------
        if exists:
            old = library.get_entry(name)
            shutil.rmtree(final_dir, ignore_errors=True)
            if old and old["folder"] != folder:
                shutil.rmtree(library.resolve(old["folder"]), ignore_errors=True)
        os.replace(tmp_dir, final_dir)
        library.register(entry)
        return entry, warnings

    except (media_tools.MediaError, ChoreographyError) as e:
        raise CatalogError(str(e)) from e
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)
