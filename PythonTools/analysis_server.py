"""
Servidor de análise de poses para a Unity (processo separado, TCP em 127.0.0.1).

A Unity abre a câmera, manda os frames para cá e recebe de volta os ângulos e
pontos de cada jogador. O protocolo está em sidecar_protocol.py.

Pontos de desempenho:
  - a thread que LÊ a rede só lê e entrega: nunca espera pelo detector
  - só o frame mais recente é analisado (frames atrasados são descartados);
    assim o atraso não cresce mesmo que o PC não acompanhe o fps da câmera
  - a detecção roda em outra thread e o resultado volta assim que fica pronto

Uso:
    python analysis_server.py --players 2 --model lite
    (imprime "JD|ready|<porta>" quando está pronto para a Unity conectar)
"""
import argparse
import socket
import sys
import threading
import time

import config  # noqa: F401  (antes do cv2)
from config import LIVE_DETECT_MAX_WIDTH, LIVE_MODEL
import sidecar_protocol as proto
from pose_utils import MediaPipeDetector, ModelError, ensure_model
from realtime import PoseWorker


def _default_detector_factory(players, model):
    ensure_model(model)
    return MediaPipeDetector(players, model=model)


def serve(*, host="127.0.0.1", port=0, players=2, model=LIVE_MODEL, detector_factory=None,
          accept_timeout=30.0, on_ready=None, log=None):
    """
    Atende UM cliente (a Unity) e termina quando ele desconecta.
    Retorna 0 em caso normal, 2 se o modelo de pose não pôde ser obtido, 1 em outros erros.
    """
    detector_factory = detector_factory or _default_detector_factory
    log = log or (lambda level, msg: print(f"JD|log|{level}|{msg}", flush=True))

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((host, port))
    server.listen(1)
    server.settimeout(accept_timeout)
    actual_port = server.getsockname()[1]
    if on_ready:
        on_ready(actual_port)
    else:
        print(f"JD|ready|{actual_port}", flush=True)

    try:
        sock, _ = server.accept()
    except socket.timeout:
        log("error", "A Unity não conectou a tempo.")
        server.close()
        return 1
    server.close()
    sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    sock.settimeout(None)

    send_lock = threading.Lock()

    def send(data):
        with send_lock:
            sock.sendall(data)

    worker = None
    try:
        # ---- configuração (opcional) ------------------------------------------
        first = proto.read_message(sock)
        if first[0] == proto.MSG_CONFIG:
            import json
            cfg = json.loads(bytes(first[1]).decode("utf-8") or "{}")
            players = int(cfg.get("players", players))
            model = cfg.get("model", model)
            pending = None
        else:
            pending = first

        try:
            detector = detector_factory(players, model)
        except (ModelError, ValueError) as e:
            send(proto.json_message(proto.MSG_LOG, {"level": "error", "message": str(e)}))
            return 2

        def on_result(res):
            seq, t_ms = res.meta
            try:
                send(proto.pack_result(seq, t_ms, res.detect_ms, res.players))
            except OSError:
                pass                      # cliente saiu; o laço de leitura percebe

        worker = PoseWorker(detector, players, input_format="rgb",
                            max_width=LIVE_DETECT_MAX_WIDTH, on_result=on_result).start()
        send(proto.json_message(proto.MSG_READY,
                                {"version": proto.VERSION, "model": model, "players": players}))

        # ---- laço de leitura: só lê e entrega ------------------------------------
        message = pending
        while True:
            if message is None:
                message = proto.read_message(sock)
            mtype, payload = message
            message = None
            if mtype == proto.MSG_FRAME:
                seq, t_ms, rgb = proto.parse_frame(payload)
                worker.submit(seq, rgb, time.perf_counter(), meta=(seq, t_ms))
            elif mtype == proto.MSG_PING:
                send(proto.pack_message(proto.MSG_PONG))
            elif mtype == proto.MSG_BYE:
                break
            if worker.error:
                send(proto.json_message(proto.MSG_LOG, {"level": "error",
                                                         "message": f"Erro na detecção: {worker.error}"}))
                return 1
        return 0
    except (ConnectionError, OSError):
        return 0                          # a Unity fechou a conexão
    except proto.ProtocolError as e:
        log("error", str(e))
        return 1
    finally:
        if worker:
            worker.stop()
        try:
            sock.close()
        except OSError:
            pass


def main():
    p = argparse.ArgumentParser(description="Servidor de análise de poses para a Unity.")
    p.add_argument("--port", type=int, default=0, help="0 = escolhe uma porta livre")
    p.add_argument("--players", type=int, default=2, help="quantos jogadores detectar (a Unity pode mudar no CONFIG)")
    p.add_argument("--model", default=LIVE_MODEL, choices=("lite", "full", "heavy"))
    args = p.parse_args()
    sys.exit(serve(port=args.port, players=args.players, model=args.model))


if __name__ == "__main__":
    main()
