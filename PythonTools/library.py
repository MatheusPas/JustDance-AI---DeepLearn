"""
Catálogo de danças.

Cada dança vive na sua própria pasta (assets/dances/<slug>/) com:
  music.mp3      a música, separada do vídeo
  dance.mp4      a dança (vídeo SEM áudio, fps constante)
  poses.json     a coreografia (movimentos capturados)
  thumbnail.jpg  miniatura para a galeria
  meta.json      os dados da dança (os mesmos de uma entrada do índice)

O índice assets/library.json é só uma lista com as meta.json de todas as
danças e pode ser reconstruído a qualquer momento (rebuild_index). Todos os
caminhos guardados são RELATIVOS à pasta assets/, para o projeto poder ser
movido de lugar ou lido pela Unity.
"""
import json
import os
import re
import shutil
import unicodedata

from config import ASSETS_DIR, DANCES_DIR, FORMAT_VERSION, LIBRARY_FILE


def slugify(name):
    text = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode("ascii")
    text = re.sub(r"[^a-zA-Z0-9]+", "_", text).strip("_").lower()
    return text or "danca"


def dance_dir(slug):
    return os.path.join(DANCES_DIR, slug)


def to_relative(path):
    return os.path.relpath(path, ASSETS_DIR).replace(os.sep, "/")


def resolve(relative_path):
    """Caminho relativo (como guardado no catálogo) -> caminho absoluto."""
    return os.path.join(ASSETS_DIR, *relative_path.split("/"))


def _read_index():
    if not os.path.exists(LIBRARY_FILE):
        return []
    with open(LIBRARY_FILE, "r", encoding="utf-8") as f:
        return json.load(f)


def _write_index(entries):
    os.makedirs(os.path.dirname(LIBRARY_FILE), exist_ok=True)
    tmp = LIBRARY_FILE + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(entries, f, indent=2, ensure_ascii=False)
    os.replace(tmp, LIBRARY_FILE)


def list_entries():
    """Danças no formato atual, ordenadas por nome."""
    entries = [e for e in _read_index() if e.get("format_version") == FORMAT_VERSION]
    return sorted(entries, key=lambda e: e["name"].lower())


def count_legacy_entries():
    return sum(1 for e in _read_index() if e.get("format_version") != FORMAT_VERSION)


def get_entry(name):
    for entry in list_entries():
        if entry["name"].lower() == name.lower():
            return entry
    return None


def register(entry):
    """Adiciona (ou substitui, se o nome já existir) uma dança no índice."""
    entries = [e for e in _read_index() if e.get("name", "").lower() != entry["name"].lower()]
    entries.append(entry)
    _write_index(entries)


def remove(name):
    """Apaga a dança (pasta inteira) e tira do índice. Retorna True se existia."""
    entry = get_entry(name)
    if not entry:
        return False
    shutil.rmtree(resolve(entry["folder"]), ignore_errors=True)
    _write_index([e for e in _read_index() if e.get("name", "").lower() != name.lower()])
    return True


def rebuild_index():
    """Refaz library.json olhando as meta.json de cada pasta de dança."""
    entries = []
    if os.path.isdir(DANCES_DIR):
        for folder in sorted(os.listdir(DANCES_DIR)):
            meta = os.path.join(DANCES_DIR, folder, "meta.json")
            if folder.startswith(".") or not os.path.isfile(meta):
                continue
            with open(meta, "r", encoding="utf-8") as f:
                entries.append(json.load(f))
    _write_index(entries)
    return len(entries)
