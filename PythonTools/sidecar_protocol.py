"""
Protocolo entre a Unity (cliente) e o analysis_server.py (servidor), por TCP em
127.0.0.1. Tudo em little-endian, sem texto no caminho quente (nada de JSON por frame).

Toda mensagem começa com um cabeçalho de 8 bytes:
    magic   2 bytes  "JD"
    tipo    1 byte
    reservado 1 byte (0)
    tamanho 4 bytes  uint32: bytes do conteúdo que vem a seguir

Unity -> servidor
    CONFIG (1)  JSON UTF-8: {"players": 2, "model": "lite"}   (opcional, 1ª mensagem)
    FRAME  (2)  struct FRAME_HEAD + pixels
    BYE    (3)  sem conteúdo
    PING   (4)  sem conteúdo

servidor -> Unity
    READY  (0x81) JSON: {"version": 1, "model": "lite", "players": 2}
    RESULT (0x82) struct RESULT_HEAD + n_players * struct PLAYER
    LOG    (0x83) JSON: {"level": "info|warn|error", "message": "..."}
    PONG   (0x84) sem conteúdo

FRAME_HEAD   seq uint32 | t_ms int32 | width uint16 | height uint16 | channels uint8 | flags uint8
    pixels: width*height*channels bytes, linha a linha (channels 3 = RGB, 4 = RGBA)
    flags bit 0: as linhas estão de BAIXO para CIMA (como o GetPixels32 da Unity)
    flags bit 1: espelhar horizontalmente antes de detectar

RESULT_HEAD  seq uint32 | t_ms int32 | detect_ms float32 | n_players uint8 | reservado uint8
PLAYER       id uint8 | 8 ângulos float32 (NaN = não confiável) | 13 pontos x,y float32 (0..1, NaN = ausente)
    Os pontos estão no sistema de coordenadas do frame DEPOIS de desvirar/espelhar
    (origem no canto superior esquerdo).

seq e t_ms são devolvidos exatamente como chegaram: a Unity usa t_ms para saber
a que instante da música o frame pertence e comparar com a coreografia nesse instante.
"""
import json
import struct

import numpy as np

MAGIC = b"JD"
VERSION = 1

MSG_CONFIG, MSG_FRAME, MSG_BYE, MSG_PING = 1, 2, 3, 4
MSG_READY, MSG_RESULT, MSG_LOG, MSG_PONG = 0x81, 0x82, 0x83, 0x84

HEADER = struct.Struct("<2sBBI")
FRAME_HEAD = struct.Struct("<IiHHBB")
RESULT_HEAD = struct.Struct("<IifBB")

N_ANGLES = 8
N_POINTS = 13
PLAYER = struct.Struct(f"<B{N_ANGLES}f{N_POINTS * 2}f")

FLAG_BOTTOM_UP = 1
FLAG_MIRROR = 2

MAX_PAYLOAD = 64 * 1024 * 1024        # recusa mensagens absurdas


class ProtocolError(RuntimeError):
    pass


def recv_exact(sock, n):
    """Lê exatamente n bytes (ou levanta ConnectionError se a conexão fechar)."""
    buf = bytearray(n)
    view = memoryview(buf)
    got = 0
    while got < n:
        k = sock.recv_into(view[got:], n - got)
        if k == 0:
            raise ConnectionError("conexão encerrada")
        got += k
    return buf


def read_message(sock):
    head = recv_exact(sock, HEADER.size)
    magic, mtype, _reserved, size = HEADER.unpack(head)
    if magic != MAGIC:
        raise ProtocolError("cabeçalho inválido")
    if size > MAX_PAYLOAD:
        raise ProtocolError(f"mensagem grande demais ({size} bytes)")
    payload = recv_exact(sock, size) if size else bytearray()
    return mtype, payload


def pack_message(mtype, payload=b""):
    return HEADER.pack(MAGIC, mtype, 0, len(payload)) + bytes(payload)


def json_message(mtype, obj):
    return pack_message(mtype, json.dumps(obj, ensure_ascii=False).encode("utf-8"))


# --- FRAME -----------------------------------------------------------------

def pack_frame(seq, t_ms, pixels, *, bottom_up=False, mirror=False):
    """pixels: array uint8 (altura, largura, 3 ou 4)."""
    h, w, ch = pixels.shape
    flags = (FLAG_BOTTOM_UP if bottom_up else 0) | (FLAG_MIRROR if mirror else 0)
    head = FRAME_HEAD.pack(seq, t_ms, w, h, ch, flags)
    return pack_message(MSG_FRAME, head + np.ascontiguousarray(pixels).tobytes())


def parse_frame(payload):
    """payload -> (seq, t_ms, rgb) onde rgb é uma VISTA (h, w, 3) já desvirada/espelhada (sem copiar)."""
    if len(payload) < FRAME_HEAD.size:
        raise ProtocolError("frame curto demais")
    seq, t_ms, w, h, ch, flags = FRAME_HEAD.unpack_from(payload, 0)
    if ch not in (3, 4) or w == 0 or h == 0:
        raise ProtocolError("formato de frame inválido")
    expected = FRAME_HEAD.size + w * h * ch
    if len(payload) != expected:
        raise ProtocolError(f"frame com tamanho errado: {len(payload)} != {expected}")
    pixels = np.frombuffer(payload, dtype=np.uint8, count=w * h * ch, offset=FRAME_HEAD.size).reshape(h, w, ch)
    rgb = pixels[..., :3]
    if flags & FLAG_BOTTOM_UP:
        rgb = rgb[::-1]
    if flags & FLAG_MIRROR:
        rgb = rgb[:, ::-1]
    return seq, t_ms, rgb


# --- RESULT ----------------------------------------------------------------

def pack_result(seq, t_ms, detect_ms, players):
    """players: {id: (angles (8,), points (13, 2))}"""
    parts = [RESULT_HEAD.pack(seq, t_ms, detect_ms, len(players), 0)]
    for pid in sorted(players):
        angles, points = players[pid]
        flat = np.concatenate([np.asarray(angles, np.float32).ravel(), np.asarray(points, np.float32).ravel()])
        parts.append(PLAYER.pack(pid, *flat.tolist()))
    return pack_message(MSG_RESULT, b"".join(parts))


def parse_result(payload):
    """payload -> (seq, t_ms, detect_ms, {id: (angles, points)})"""
    seq, t_ms, detect_ms, n, _ = RESULT_HEAD.unpack_from(payload, 0)
    players, off = {}, RESULT_HEAD.size
    for _ in range(n):
        vals = PLAYER.unpack_from(payload, off)
        off += PLAYER.size
        arr = np.array(vals[1:], dtype=np.float32)
        players[vals[0]] = (arr[:N_ANGLES], arr[N_ANGLES:].reshape(N_POINTS, 2))
    return seq, t_ms, detect_ms, players
