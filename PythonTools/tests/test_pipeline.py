"""
Teste de ponta a ponta do pipeline de catalogação e do jogo, SEM webcam e SEM o
modelo do MediaPipe (usa um vídeo sintético e um detector falso que lê a imagem).

Rodar (na pasta do projeto):
    python -m unittest discover -s tests -v
"""
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest

TMP = tempfile.mkdtemp(prefix="jd_test_")
os.environ["JUSTDANCE_ASSETS"] = os.path.join(TMP, "assets")     # isola do seu catálogo real
os.environ["SDL_AUDIODRIVER"] = "dummy"
os.environ["SDL_VIDEODRIVER"] = "dummy"
os.environ["PYGAME_HIDE_SUPPORT_PROMPT"] = "1"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import cv2
import numpy as np

import analysis_server
import catalog
import choreography
import library
import media_tools
import play
import realtime
import sidecar_client
import sidecar_protocol as proto
import synthetic
from choreography import load_choreography
from pose_utils import L_HIP, POINT_INDICES, R_HIP, ModelError, landmarks_to_features

FPS = 30


def decode_pcm(path, sr=8000):
    out = subprocess.run([media_tools.ffmpeg_exe(), "-v", "error", "-i", path, "-f", "f32le",
                          "-ac", "1", "-ar", str(sr), "-"], capture_output=True).stdout
    return np.frombuffer(out, dtype=np.float32), sr


def dominant_hz(path, start=1.0, end=4.0):
    x, sr = decode_pcm(path)
    seg = x[int(start * sr):int(end * sr)]
    spec = np.abs(np.fft.rfft(seg * np.hanning(len(seg))))
    return float(np.argmax(spec) * sr / len(seg))


def rms(path, start, end):
    x, sr = decode_pcm(path)
    return float(np.sqrt(np.mean(x[int(start * sr):int(end * sr)] ** 2)))


def ground_truth_angles(k, t):
    return landmarks_to_features(synthetic.ground_truth_landmarks(k, t))[0]


def angle_errors(player, k, t_offset):
    """Erro (graus) entre os ângulos capturados e o gabarito, fora do buraco de detecção."""
    errs = []
    for i in range(player.angles.shape[0]):
        t = t_offset + i / FPS
        if synthetic.DROPOUT[0] - 0.2 <= t <= synthetic.DROPOUT[1] + 0.2 and k == 1:
            continue
        gt, got = ground_truth_angles(k, t), player.angles[i]
        both = ~np.isnan(gt) & ~np.isnan(got)
        errs.extend(np.abs(gt[both] - got[both]).tolist())
    return np.array(errs)


class PipelineTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.src = os.path.join(TMP, "source.mp4")
        synthetic.make_source_video(cls.src, seconds=6.0, with_audio=True, tone_hz=440)
        cls.silent_src = os.path.join(TMP, "silent_source.mp4")
        synthetic.make_source_video(cls.silent_src, seconds=6.0, with_audio=False)
        cls.detector = lambda self_or_n: synthetic.make_vision_detector()
        cls.entry, cls.warnings = catalog.ingest_dance(
            cls.src, "Dança Teste", detector_factory=lambda n: synthetic.make_vision_detector(),
            log=lambda *a: None)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(TMP, ignore_errors=True)

    # ---------------------------------------------------------------- separação
    def test_music_and_dance_are_separated(self):
        dance = media_tools.probe(library.resolve(self.entry["video_path"]))
        music_path = library.resolve(self.entry["audio_path"])
        music = media_tools.probe(music_path)

        self.assertTrue(dance.has_video)
        self.assertFalse(dance.has_audio, "o vídeo da dança deve ficar SEM áudio")
        self.assertEqual(dance.fps, FPS)                     # 60 fps de origem -> 30 constante
        self.assertEqual(dance.height, 720)
        self.assertAlmostEqual(dance.duration, 6.0, delta=0.1)

        self.assertTrue(music.has_audio)
        self.assertFalse(music.has_video, "a música deve ficar SEM imagem")
        self.assertAlmostEqual(music.duration, 6.0, delta=0.2)
        self.assertAlmostEqual(dominant_hz(music_path), 440, delta=15)   # é o áudio do vídeo
        self.assertTrue(os.path.exists(library.resolve(self.entry["thumbnail_path"])))

    def test_catalog_entry(self):
        e = self.entry
        self.assertEqual(e["num_people"], 2)                 # falso positivo de 1 frame foi descartado
        self.assertIn(e["difficulty"], ("Fácil", "Médio", "Difícil"))
        self.assertEqual(e["music_source"], "video")
        for key in ("audio_path", "video_path", "poses_path", "thumbnail_path", "folder"):
            self.assertFalse(os.path.isabs(e[key]), key)     # caminhos relativos
        for key in ("audio_path", "video_path", "poses_path", "thumbnail_path"):
            self.assertTrue(os.path.exists(library.resolve(e[key])), key)
        self.assertIn("Dança Teste", [x["name"] for x in library.list_entries()])
        self.assertEqual(self.warnings, [])

    # ---------------------------------------------------------------- movimentos
    def test_dancers_are_tracked_left_to_right(self):
        ch = load_choreography(library.resolve(self.entry["poses_path"]))
        self.assertEqual(ch.fps, FPS)
        self.assertAlmostEqual(ch.frame_count, 180, delta=2)
        self.assertEqual(ch.num_people, 2)
        hips = [POINT_INDICES.index(L_HIP), POINT_INDICES.index(R_HIP)]
        xs = [float(np.nanmedian(p.points[p.present][:, hips, 0])) for p in ch.players]
        self.assertLess(xs[0], 0.5)
        self.assertGreater(xs[1], 0.5)
        self.assertGreater(ch.players[0].coverage, 0.99)
        self.assertAlmostEqual(ch.players[1].coverage, 1 - 0.3 / 6, delta=0.03)

    def test_short_dropout_is_interpolated(self):
        ch = load_choreography(library.resolve(self.entry["poses_path"]))
        self.assertTrue(ch.players[1].present[int(3.15 * FPS)])
        self.assertFalse(np.isnan(ch.players[1].angles[int(3.15 * FPS)]).all())

    def test_poses_are_aligned_with_video_and_music_time(self):
        ch = load_choreography(library.resolve(self.entry["poses_path"]))
        for k, player in enumerate(ch.players):
            errs = angle_errors(player, k, t_offset=0.0)
            self.assertGreater(len(errs), 100)
            # um deslocamento de 1 frame daria ~4 graus de erro médio
            self.assertLess(errs.mean(), 2.0, f"dançarino {k}")
            self.assertLess(np.percentile(errs, 95), 6.0, f"dançarino {k}")

    def test_unreliable_joints_are_masked(self):
        ch = load_choreography(library.resolve(self.entry["poses_path"]))
        names = ["left_elbow", "right_elbow", "left_shoulder", "right_shoulder",
                 "left_hip", "right_hip", "left_knee", "right_knee"]
        valid = (~np.isnan(ch.players[0].angles)).mean(axis=0)
        self.assertTrue(all(valid[i] > 0.95 for i in range(4)), "braços do dançarino 0 são válidos")
        self.assertTrue(all(valid[i] < 0.05 for i in range(4, 8)), "pernas cortadas ficam inválidas")
        self.assertTrue(all(v > 0.9 for v in (~np.isnan(ch.players[1].angles)).mean(axis=0)), names)

    # ---------------------------------------------------------------- recorte e música anexada
    def test_trim_keeps_music_dance_and_poses_aligned(self):
        entry, _ = catalog.ingest_dance(
            self.src, "Recortada", start=1.0, end=5.0, detector_factory=lambda n: synthetic.make_vision_detector(),
            log=lambda *a: None)
        self.assertAlmostEqual(entry["duration_sec"], 4.0, delta=0.1)
        self.assertAlmostEqual(media_tools.probe(library.resolve(entry["audio_path"])).duration, 4.0, delta=0.2)
        ch = load_choreography(library.resolve(entry["poses_path"]))
        for k, player in enumerate(ch.players):
            self.assertLess(angle_errors(player, k, t_offset=1.0).mean(), 2.0)

    def test_attached_music_with_offsets(self):
        tone = os.path.join(TMP, "estudio.wav")
        synthetic.make_tone(tone, 10, 880)

        e1, _ = catalog.ingest_dance(self.src, "Anexada", music_path=tone, music_offset=1.5,
                                     detector_factory=lambda n: synthetic.make_vision_detector(), log=lambda *a: None)
        m1 = library.resolve(e1["audio_path"])
        self.assertEqual(e1["music_source"], "attached")
        self.assertAlmostEqual(dominant_hz(m1), 880, delta=20)          # tocou a música anexada
        self.assertAlmostEqual(media_tools.probe(m1).duration, 6.0, delta=0.2)

        e2, _ = catalog.ingest_dance(self.src, "Atrasada", music_path=tone, music_offset=-2.0,
                                     detector_factory=lambda n: synthetic.make_vision_detector(), log=lambda *a: None)
        m2 = library.resolve(e2["audio_path"])
        self.assertLess(rms(m2, 0.1, 1.7), 0.01, "a música deve começar 2s depois")
        self.assertGreater(rms(m2, 2.3, 3.5), 0.05)

    # ---------------------------------------------------------------- erros
    def test_errors(self):
        factory = lambda n: synthetic.make_vision_detector()
        with self.assertRaises(catalog.CatalogError) as cm:
            catalog.ingest_dance(self.silent_src, "Sem Som", detector_factory=factory, log=lambda *a: None)
        self.assertIn("--music", str(cm.exception))
        self.assertIsNone(library.get_entry("Sem Som"))
        self.assertEqual([d for d in os.listdir(os.path.join(TMP, "assets", "dances")) if d.startswith(".tmp")], [])

        with self.assertRaises(catalog.CatalogError):
            catalog.ingest_dance(self.src, "Dança Teste", detector_factory=factory, log=lambda *a: None)
        catalog.ingest_dance(self.src, "Dança Teste", overwrite=True, detector_factory=factory, log=lambda *a: None)

        with self.assertRaises(catalog.CatalogError):
            catalog.ingest_dance(self.src, "Vazio", detector_factory=lambda n: (lambda f, t: []), log=lambda *a: None)
        self.assertIsNone(library.get_entry("Vazio"))

        with self.assertRaises(catalog.CatalogError):
            catalog.ingest_dance(os.path.join(TMP, "nao_existe.mp4"), "X", log=lambda *a: None)

    def test_remove_and_rebuild_index(self):
        catalog.ingest_dance(self.src, "Temporária", detector_factory=lambda n: synthetic.make_vision_detector(),
                             log=lambda *a: None)
        folder = library.resolve(library.get_entry("Temporária")["folder"])
        self.assertTrue(os.path.isdir(folder))
        self.assertTrue(library.remove("Temporária"))
        self.assertFalse(os.path.exists(folder))
        os.remove(library.LIBRARY_FILE)
        self.assertGreaterEqual(library.rebuild_index(), 1)
        self.assertIsNotNone(library.get_entry("Dança Teste"))

    # ---------------------------------------------------------------- jogo
    def test_reference_video_follows_the_clock(self):
        ref = play.ReferenceVideo(library.resolve(self.entry["video_path"]))
        self.assertIsNotNone(ref.frame_at(0))
        self.assertEqual(ref.frames_read, 1)
        ref.frame_at(1.0)
        self.assertEqual(ref.frames_read, 31)                # pulou direto para o frame do segundo 1
        held = ref.frame
        self.assertIs(ref.frame_at(0.5), held)               # tempo "voltou": mantém o frame
        self.assertIsNone(ref.frame_at(100))                 # acabou o vídeo
        ref.release()

    def test_music_clock_tracks_playback(self):
        clock = play.MusicClock(library.resolve(self.entry["audio_path"]))
        if clock._mixer is None:
            self.skipTest("pygame sem dispositivo de áudio neste ambiente")
        clock.start()
        time.sleep(1.0)
        self.assertAlmostEqual(clock.now(), 1.0, delta=0.3)
        clock.stop()

    def test_play_loop_scores_a_perfect_imitation(self):
        class WallClock:
            def __init__(self): self.t0 = time.perf_counter()
            def start(self): self.t0 = time.perf_counter()
            def now(self): return time.perf_counter() - self.t0
            def stop(self): pass

        clock = WallClock()

        class FakeCamera:
            """'Câmera' que mostra a própria dança (espelhada, pois o jogo espelha de volta)."""
            def __init__(self, path):
                self.ref = play.ReferenceVideo(path)
            def isOpened(self): return True
            def read(self):
                f = self.ref.frame_at(clock.now())
                return (f is not None), (None if f is None else cv2.flip(f, 1))
            def release(self): self.ref.release()

        saved = (play.MusicClock, play.show_countdown, cv2.imshow, cv2.waitKey, cv2.destroyAllWindows)
        play.MusicClock = lambda path: clock
        play.show_countdown = lambda *a, **k: None
        cv2.imshow = lambda *a, **k: None
        cv2.waitKey = lambda *a, **k: -1
        cv2.destroyAllWindows = lambda *a, **k: None
        try:
            result = play.play_dance(
                self.entry, 2,
                camera=FakeCamera(library.resolve(self.entry["video_path"])),
                detector=synthetic.make_vision_detector())
        finally:
            play.MusicClock, play.show_countdown, cv2.imshow, cv2.waitKey, cv2.destroyAllWindows = saved

        self.assertEqual(set(result), {0, 1})
        for pid, avg in result.items():
            self.assertGreater(avg, 90, f"jogador {pid + 1} imitando perfeitamente deveria pontuar alto")

    # ---------------------------------------------------------------- desempenho
    def test_parallel_extraction_matches_sequential(self):
        """Dividir o vídeo em pedaços (processos) dá o mesmo resultado que ler tudo em sequência."""
        video = library.resolve(self.entry["video_path"])
        stats_seq, stats_par = {}, {}
        raw1, fps1, n1 = choreography.extract_raw_tracks(
            video, synthetic.vision_detector_factory, 3, workers=1, stats=stats_seq)
        raw2, fps2, n2 = choreography.extract_raw_tracks(
            video, synthetic.vision_detector_factory, 3, workers=2,
            min_chunk_seconds=1.5, overlap_seconds=0.5, stats=stats_par)
        self.assertEqual((fps1, n1), (fps2, n2))
        self.assertEqual(stats_par["workers"], 2)
        c1 = choreography.build_choreography(raw1, fps1, n1)
        c2 = choreography.build_choreography(raw2, fps2, n2)
        self.assertEqual(c1.num_people, c2.num_people)
        for a, b in zip(c1.players, c2.players):
            both = ~np.isnan(a.angles) & ~np.isnan(b.angles)
            valid_a, valid_b = (~np.isnan(a.angles)).sum(), (~np.isnan(b.angles)).sum()
            self.assertGreater(both.sum(), 0.95 * min(valid_a, valid_b))
            self.assertLess(float(np.abs(a.angles[both] - b.angles[both]).mean()), 1.0)
            self.assertAlmostEqual(a.coverage, b.coverage, delta=0.03)

    def test_parallel_extraction_keeps_dancers_in_order_across_chunks(self):
        video = library.resolve(self.entry["video_path"])
        raw, fps, n = choreography.extract_raw_tracks(
            video, synthetic.vision_detector_factory, 3, workers=3,
            min_chunk_seconds=1.0, overlap_seconds=0.5)
        choreo = choreography.build_choreography(raw, fps, n)
        self.assertEqual(choreo.num_people, 2)
        xs = []
        for p in choreo.players:
            hip = POINT_INDICES.index(L_HIP)
            xs.append(float(np.nanmedian(p.points[p.present][:, hip, 0])))
        self.assertLess(xs[0], 0.5)
        self.assertGreater(xs[1], 0.5)
        for k, p in enumerate(choreo.players):
            err = angle_errors(p, k, 0.0)
            self.assertLess(float(np.median(err)), 3.0, f"dançarino {k} fora do gabarito depois de juntar os pedaços")

    def test_catalog_with_workers(self):
        stats = {}
        entry, _ = catalog.ingest_dance(
            self.src, "Em Paralelo", detector_factory=synthetic.vision_detector_factory,
            workers=2, stats=stats, log=lambda *a: None)
        self.assertEqual(entry["num_people"], 2)
        self.assertIn("wall_s", stats)

    def test_pose_worker_never_blocks_and_drops_stale_frames(self):
        def slow_detector(frame, ts):
            time.sleep(0.08)
            return []

        worker = realtime.PoseWorker(slow_detector, 2, input_format="rgb").start()
        frame = np.zeros((48, 64, 3), np.uint8)
        t0 = time.perf_counter()
        for seq in range(30):
            worker.submit(seq, frame, time.perf_counter())
            time.sleep(0.005)
        submit_time = time.perf_counter() - t0
        self.assertLess(submit_time, 0.5, "submit() não pode esperar o detector")
        deadline = time.time() + 3
        result = None
        while time.time() < deadline and (result is None or result.seq != 29):
            result = worker.poll() or result
            time.sleep(0.01)
        worker.stop()
        self.assertEqual(result.seq, 29, "o último frame enviado tem que ser analisado")
        self.assertLess(worker.processed, 15)
        self.assertGreater(worker.dropped, 10)

    def test_latest_frame_camera_keeps_only_the_newest_frame(self):
        class Counter:
            def __init__(self): self.n = 0
            def isOpened(self): return True
            def read(self):
                time.sleep(0.01)
                self.n += 1
                return True, np.full((4, 4, 3), self.n % 255, np.uint8)
            def release(self): pass

        cam = realtime.LatestFrameCamera(Counter(), mirror=False).start()
        time.sleep(0.5)
        seq, frame, _ = cam.read_latest()
        self.assertGreater(seq, 30)                       # leu bastante enquanto ninguém consumia...
        self.assertEqual(int(frame[0, 0, 0]), seq % 255)  # ...mas entrega só o mais novo
        self.assertIsNone(cam.read_latest(after_seq=seq + 1000))
        cam.stop()

    def test_reference_stream_is_ready_in_advance_and_catches_up(self):
        path = library.resolve(self.entry["video_path"])
        stream = realtime.ReferenceStream(path, (320, 180), invert=True).start()
        first = stream.frame_at(0)
        self.assertEqual(first.shape, (180, 320, 3))
        plain = play.ReferenceVideo(path)
        expect = cv2.bitwise_not(cv2.resize(plain.frame_at(0), (320, 180), interpolation=cv2.INTER_AREA))
        self.assertLess(float(np.abs(first.astype(int) - expect.astype(int)).mean()), 1.0)

        time.sleep(0.3)
        stream.frame_at(3.0)                              # salta 3 s de uma vez
        deadline = time.time() + 3
        while time.time() < deadline and stream._buf[0][0] < int(3.0 * stream.fps) - 1:
            stream.frame_at(3.0)
            time.sleep(0.01)
        self.assertGreaterEqual(stream._buf[0][0], int(3.0 * stream.fps) - 1)
        plain.release()

        stream.frame_at(10.0)
        deadline = time.time() + 3
        while time.time() < deadline and not stream.finished(10.0):
            time.sleep(0.01)
        self.assertTrue(stream.finished(10.0))
        stream.stop()

    # ---------------------------------------------------------------- servidor para a Unity
    def test_protocol_roundtrip(self):
        pixels = np.random.RandomState(0).randint(0, 255, (6, 8, 4), dtype=np.uint8)
        msg = proto.pack_frame(7, 1234, pixels, bottom_up=True, mirror=True)
        mtype, size = proto.HEADER.unpack(msg[:8])[1], proto.HEADER.unpack(msg[:8])[3]
        self.assertEqual((mtype, size), (proto.MSG_FRAME, len(msg) - 8))
        seq, t_ms, rgb = proto.parse_frame(bytearray(msg[8:]))
        self.assertEqual((seq, t_ms), (7, 1234))
        self.assertTrue(np.array_equal(rgb, pixels[..., :3][::-1][:, ::-1]))

        angles = np.arange(8, dtype=np.float32)
        angles[3] = np.nan
        points = np.random.RandomState(1).rand(13, 2).astype(np.float32)
        res = proto.pack_result(9, 55, 12.5, {1: (angles, points), 0: (angles + 1, points)})
        seq, t_ms, ms, players = proto.parse_result(bytearray(res[8:]))
        self.assertEqual((seq, t_ms, ms), (9, 55, 12.5))
        self.assertEqual(sorted(players), [0, 1])
        self.assertTrue(np.isnan(players[1][0][3]))
        self.assertTrue(np.allclose(players[1][1], points))

    def _start_server(self, factory):
        ready, box = threading.Event(), {}

        def on_ready(port):
            box["port"] = port
            ready.set()

        def run():
            box["code"] = analysis_server.serve(players=2, detector_factory=factory, on_ready=on_ready,
                                                accept_timeout=10, log=lambda *a: None)

        thread = threading.Thread(target=run, daemon=True)
        thread.start()
        self.assertTrue(ready.wait(5))
        return box, thread

    def test_sidecar_returns_the_right_angles_for_every_orientation(self):
        """A Unity manda frames de cabeça para baixo / espelhados: o servidor desvira e acerta os ângulos."""
        box, thread = self._start_server(lambda players, model: synthetic.make_vision_detector())
        client = sidecar_client.SidecarClient(box["port"])
        client.start(players=2, model="lite")
        self.assertEqual(client.wait_ready(10)["players"], 2)

        choreo = load_choreography(library.resolve(self.entry["poses_path"]))
        ref = play.ReferenceVideo(library.resolve(self.entry["video_path"]))
        t = 2.0
        frame_bgr = ref.frame_at(t)
        rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
        rgba = np.dstack([rgb, np.full(rgb.shape[:2], 255, np.uint8)])
        variants = {
            "normal": dict(pixels=rgb, bottom_up=False, mirror=False),
            "unity_rgba_bottom_up": dict(pixels=rgba[::-1], bottom_up=True, mirror=False),
            "mirrored": dict(pixels=rgb[:, ::-1], bottom_up=False, mirror=True),
        }
        expected = choreo.angles_at(t)
        for seq, (name, kwargs) in enumerate(variants.items(), start=1):
            client.send_frame(seq, 2000 + seq, **kwargs)
            got = client.wait_result(10)
            self.assertIsNotNone(got, name)
            rseq, t_ms, _ms, players = got
            self.assertEqual((rseq, t_ms), (seq, 2000 + seq), name)
            self.assertEqual(sorted(players), [0, 1], name)
            for pid, (angles, points) in players.items():
                both = ~np.isnan(angles) & ~np.isnan(expected[pid])
                self.assertGreaterEqual(both.sum(), 3, name)
                self.assertLess(float(np.abs(angles[both] - expected[pid][both]).mean()), 6.0,
                                f"{name}: jogador {pid + 1} com ângulos errados")
            xs = [players[0][1][:, 0].mean(), players[1][1][:, 0].mean()]
            self.assertLess(xs[0], xs[1], f"{name}: jogador 1 deveria estar à esquerda")
        ref.release()
        client.close()
        thread.join(5)
        self.assertEqual(box["code"], 0)

    def test_sidecar_reports_missing_model(self):
        def no_model(players, model):
            raise ModelError("modelo ausente de propósito")

        box, thread = self._start_server(no_model)
        client = sidecar_client.SidecarClient(box["port"])
        client.start(players=1)
        deadline = time.time() + 5
        while time.time() < deadline and not client.logs and not client.closed:
            time.sleep(0.02)
        self.assertTrue(client.logs and client.logs[0]["level"] == "error")
        self.assertIn("modelo ausente", client.logs[0]["message"])
        thread.join(5)
        self.assertEqual(box["code"], 2)
        client.close()

    def test_train_machine_output_is_parseable(self):
        """É assim que a Unity lê o progresso da catalogação."""
        env = dict(os.environ, JUSTDANCE_ASSETS=os.path.join(TMP, "assets_machine"), PYTHONIOENCODING="utf-8")
        # sem o modelo do MediaPipe a catalogação falha com uma mensagem JD|error legível
        proc = subprocess.run(
            [sys.executable, os.path.join(ROOT, "train.py"), "--video", self.src, "--name", "Máquina", "--machine"],
            capture_output=True, text=True, encoding="utf-8", env=dict(env, JUSTDANCE_MODELS=os.path.join(TMP, "sem_modelo")))
        lines = [l for l in proc.stdout.splitlines() if l.startswith("JD|")]
        self.assertTrue(lines, proc.stderr)
        self.assertTrue(lines[-1].startswith("JD|error|") or lines[-1].startswith("JD|done|"), lines[-1])


if __name__ == "__main__":
    unittest.main()
