"""
Cliente de referência do analysis_server.py (a Unity tem o seu em C#).

Serve para testar o servidor sem abrir a Unity:

    python analysis_server.py --players 2          (num terminal)
    python sidecar_client.py --port <porta>         (noutro; mostra a câmera com o esqueleto)

ou, de uma vez só:

    python sidecar_client.py --spawn --players 2
"""
import argparse
import json
import socket
import subprocess
import sys
import threading
import time

import config  # noqa: F401  (antes do cv2)
import cv2
import numpy as np

import sidecar_protocol as proto


class SidecarClient:
    def __init__(self, port, host="127.0.0.1", timeout=10.0):
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.sock.settimeout(None)
        self.ready = None
        self.logs = []
        self._results = []
        self._cond = threading.Condition()
        self._reader = None
        self.closed = False

    def start(self, players, model="lite"):
        self.sock.sendall(proto.json_message(proto.MSG_CONFIG, {"players": players, "model": model}))
        self._reader = threading.Thread(target=self._read_loop, daemon=True)
        self._reader.start()

    def _read_loop(self):
        try:
            while True:
                mtype, payload = proto.read_message(self.sock)
                with self._cond:
                    if mtype == proto.MSG_READY:
                        self.ready = json.loads(bytes(payload).decode("utf-8"))
                    elif mtype == proto.MSG_RESULT:
                        self._results.append(proto.parse_result(payload))
                    elif mtype == proto.MSG_LOG:
                        self.logs.append(json.loads(bytes(payload).decode("utf-8")))
                    self._cond.notify_all()
        except (ConnectionError, OSError):
            pass
        finally:
            with self._cond:
                self.closed = True
                self._cond.notify_all()

    def wait_ready(self, timeout=30.0):
        with self._cond:
            self._cond.wait_for(lambda: self.ready is not None or self.closed or self.logs, timeout=timeout)
        return self.ready

    def send_frame(self, seq, t_ms, pixels, *, bottom_up=False, mirror=False):
        self.sock.sendall(proto.pack_frame(seq, t_ms, pixels, bottom_up=bottom_up, mirror=mirror))

    def take_results(self):
        with self._cond:
            out, self._results = self._results, []
        return out

    def wait_result(self, timeout=10.0):
        with self._cond:
            self._cond.wait_for(lambda: self._results or self.closed, timeout=timeout)
            return self._results.pop(0) if self._results else None

    def close(self):
        try:
            self.sock.sendall(proto.pack_message(proto.MSG_BYE))
        except OSError:
            pass
        try:
            self.sock.close()
        except OSError:
            pass


def _demo(client, players):
    """Mostra a câmera com os pontos devolvidos pelo servidor, para conferir que tudo funciona."""
    from realtime import open_camera
    cap = open_camera()
    if cap is None:
        print("Não consegui abrir a câmera.")
        return
    seq, last, lat = 0, {}, 0.0
    sent_at = {}
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        frame = cv2.flip(frame, 1)
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        seq += 1
        sent_at[seq] = time.perf_counter()
        client.send_frame(seq, int(time.perf_counter() * 1000) & 0x7FFFFFFF, rgb)
        for rseq, _t, detect_ms, res in client.take_results():
            last = res
            lat = (time.perf_counter() - sent_at.pop(rseq, time.perf_counter())) * 1000
        h, w = frame.shape[:2]
        for pid, (_a, pts) in last.items():
            for x, y in pts:
                if not np.isnan(x):
                    cv2.circle(frame, (int(x * w), int(y * h)), 4, (0, 255, 0), -1)
            cv2.putText(frame, f"J{pid + 1}", (10, 30 + 25 * pid), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
        cv2.putText(frame, f"ida e volta: {lat:.0f} ms", (10, h - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
        cv2.imshow("sidecar_client (Q sai)", frame)
        if cv2.waitKey(1) & 0xFF in (ord("q"), 27) or client.closed:
            break
    cap.release()
    cv2.destroyAllWindows()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--port", type=int)
    p.add_argument("--spawn", action="store_true", help="inicia o analysis_server.py automaticamente")
    p.add_argument("--players", type=int, default=2)
    p.add_argument("--model", default="lite")
    args = p.parse_args()

    proc, port = None, args.port
    if args.spawn:
        proc = subprocess.Popen([sys.executable, "-u", "analysis_server.py"],
                                stdout=subprocess.PIPE, text=True)
        for line in proc.stdout:
            if line.startswith("JD|ready|"):
                port = int(line.strip().split("|")[2])
                break
    if not port:
        p.error("informe --port ou --spawn")

    client = SidecarClient(port)
    client.start(args.players, args.model)
    print("Pronto:", client.wait_ready() or client.logs)
    try:
        _demo(client, args.players)
    finally:
        client.close()
        if proc:
            proc.terminate()


if __name__ == "__main__":
    main()
