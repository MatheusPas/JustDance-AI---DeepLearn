using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

[Serializable]
class SidecarLogMessage
{
    public string level;
    public string message;
}

/// <summary>
/// A partida. A MÚSICA é o relógio mestre (AudioSettings.dspTime): o vídeo de referência
/// é ajustado a ela, e cada quadro da câmera é comparado com a coreografia no instante
/// da música em que foi capturado.
///
/// Nada aqui espera por algo lento: a análise de poses roda em OUTRO processo
/// (PythonTools/analysis_server.py, com o Python do projeto), a rede em threads próprias,
/// e só o resultado mais recente é usado. Se o analisador atrasa, quadros são pulados,
/// o jogo não trava.
/// </summary>
public class GameController : MonoBehaviour
{
    const string LiveModel = "lite";
    const double CountdownSeconds = 3.0;
    const float ScoreTimeOffset = 0f;         // ajuste (s) se a pontuação parecer adiantada/atrasada

    [Header("Vídeo de referência (em cima, em negativo)")]
    [SerializeField] RawImage referenceImage;
    [SerializeField] AspectRatioFitter referenceFitter;

    [Header("Câmera do jogador (embaixo)")]
    [SerializeField] RawImage cameraImage;
    [SerializeField] AspectRatioFitter cameraFitter;
    [SerializeField] SkeletonGraphic skeleton;

    [Header("HUD")]
    [SerializeField] Text titleLabel;
    [SerializeField] Text timeLabel;
    [SerializeField] RectTransform progressFill;
    [SerializeField] Text statusLabel;
    [SerializeField] Text countdownLabel;
    [SerializeField] RectTransform scoreContainer;
    [SerializeField] ScoreRow scoreRowTemplate;

    [Header("Tela final / erro")]
    [SerializeField] GameObject endPanel;
    [SerializeField] Text endTitle;
    [SerializeField] Text endBody;
    [SerializeField] Button againButton;
    [SerializeField] Text againLabel;
    [SerializeField] Button menuButton;

    [SerializeField] string menuSceneName = "MainMenu";

    enum State { Loading, Running, Done, Failed }
    State state = State.Loading;

    SongEntry song;
    int playerCount;
    Choreography choreo;

    ProcessJob serverJob;
    SidecarClient sidecar;
    WebCamTexture webcam;
    Color32[] pixels;
    uint frameSeq;
    bool unsentFrame;
    int shownCount = int.MinValue;
    int shownSecond = -1;
    bool webcamBottomUp;

    AudioSource audioSource;
    AudioClip clip;
    VideoPlayer video;
    string videoError;
    bool videoStarted;
    float nextSeekAllowed;
    float lastSpeed = 1f;

    double dspStart;
    double songLength;

    ScoreRow[] rows;
    bool[] seen;
    float[] refAngles = new float[Choreography.AngleCount];

    string failure;
    bool debug;
    float debugNext;
    int resultsInWindow;
    float windowStart;
    float resultRate;
    float latencyMs;
    float detectMs;
    int framesSent;
    float progressNext;

    // ------------------------------------------------------------------ ciclo

    void Awake()
    {
        Application.runInBackground = true;          // câmera e música não podem pausar se a janela perder o foco
        Application.targetFrameRate = 60;
        againButton.onClick.AddListener(() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex));
        menuButton.onClick.AddListener(BackToMenu);
        endPanel.SetActive(false);
        countdownLabel.text = "";
        // Antes da primeira imagem tudo fica preto (em negativo, o branco é que vira preto).
        referenceImage.texture = referenceImage.material != null && referenceImage.material.HasProperty("_Invert")
            ? Texture2D.whiteTexture : Texture2D.blackTexture;
        cameraImage.texture = Texture2D.blackTexture;
        scoreRowTemplate.gameObject.SetActive(false);
    }

    void Start()
    {
        StartCoroutine(Boot());
    }

    void OnDestroy()
    {
        Cleanup();
    }

    void Cleanup()
    {
        if (sidecar != null) { sidecar.Dispose(); sidecar = null; }
        if (serverJob != null) { serverJob.Kill(); serverJob = null; }
        if (webcam != null) { webcam.Stop(); Destroy(webcam); webcam = null; }
        if (video != null) { video.Stop(); }
        if (audioSource != null) audioSource.Stop();
    }

    void BackToMenu()
    {
        Cleanup();
        if (Application.CanStreamedLevelBeLoaded(menuSceneName)) SceneManager.LoadScene(menuSceneName);
        else SceneManager.LoadScene(0);
    }

    // ------------------------------------------------------------- preparação

    IEnumerator Boot()
    {
        song = GameSession.SelectedSong;
        if (song == null)
        {
            // Cena aberta direto no Editor: usa a primeira dança da galeria.
            bool demo;
            var list = SongLibrary.Load(null, out demo);
            if (!demo && list.Count > 0) song = list[0];
        }
        if (song == null || string.IsNullOrEmpty(song.poses_path))
        {
            Fail("Nenhuma dança escolhida.\nVolte ao menu e escolha uma música da galeria.");
            yield break;
        }

        playerCount = Mathf.Clamp(GameSession.PlayerCount, 1, Mathf.Min(Mathf.Max(1, song.num_people), PoseProtocol.MaxPlayers));
        titleLabel.text = song.name;
        BuildScoreRows();

        string posesPath = JustDancePaths.ResolveGameData(song.poses_path);
        string videoPath = JustDancePaths.ResolveGameData(song.video_path);
        string audioPath = JustDancePaths.ResolveGameData(song.audio_path);

        SetStatus("Carregando a coreografia...");
        yield return null;
        try { choreo = Choreography.Load(posesPath); }
        catch (Exception e) { failure = e.Message; }
        if (failure != null) { Fail(failure); yield break; }

        if (!File.Exists(videoPath) || !File.Exists(audioPath))
        {
            Fail("Faltam arquivos da dança (vídeo ou música):\n" + videoPath + "\n" + audioPath);
            yield break;
        }
        if (!JustDancePaths.PythonInstalled || !JustDancePaths.PythonToolsPresent)
        {
            Fail("O Python do projeto não está instalado.\n\n" +
                 "Rode Tools\\setup_python_runtime.bat\n(ou, na Unity, JustDance > Instalar Python do projeto).");
            yield break;
        }

        yield return StartAnalyzer();
        if (failure != null) { Fail(failure); yield break; }

        yield return StartCamera();
        if (failure != null) { Fail(failure); yield break; }

        yield return LoadAudio(audioPath);
        if (failure != null) { Fail(failure); yield break; }

        yield return PrepareVideo(videoPath);
        if (failure != null) { Fail(failure); yield break; }

        BeginRun();
    }

    IEnumerator StartAnalyzer()
    {
        SetStatus("Iniciando o analisador de poses...");
        serverJob = PythonRunner.RunScript("analysis_server.py",
            "--players", playerCount.ToString(), "--model", LiveModel);

        var tail = new List<string>();
        int port = 0;
        float deadline = Time.realtimeSinceStartup + 40f;
        while (port == 0)
        {
            string line;
            while (serverJob.TryReadLine(out line))
            {
                if (line.StartsWith("JD|ready|", StringComparison.Ordinal))
                    int.TryParse(line.Substring(9), out port);
                else
                {
                    tail.Add(line);
                    if (tail.Count > 6) tail.RemoveAt(0);
                }
            }
            if (port != 0) break;
            if (serverJob.IsDone)
            {
                failure = "O analisador de poses fechou ao iniciar.\n" + string.Join("\n", tail);
                yield break;
            }
            if (Time.realtimeSinceStartup > deadline)
            {
                failure = "O analisador de poses demorou demais para iniciar.";
                yield break;
            }
            yield return null;
        }

        sidecar = new SidecarClient();
        string connectError = null;
        try { sidecar.Connect(port, playerCount, LiveModel); }
        catch (Exception e) { connectError = e.Message; }
        if (connectError != null)
        {
            failure = "Não consegui falar com o analisador de poses: " + connectError;
            yield break;
        }

        SetStatus("Carregando o modelo de poses...");
        deadline = Time.realtimeSinceStartup + 90f;
        while (!sidecar.IsReady)
        {
            string log;
            while (sidecar.TryReadLog(out log))
            {
                SidecarLogMessage m = null;
                try { m = JsonUtility.FromJson<SidecarLogMessage>(log); } catch (Exception) { }
                if (m != null && m.level == "error")
                {
                    failure = m.message + "\n\nSe o modelo não foi baixado, rode Tools\\setup_python_runtime.bat.";
                    yield break;
                }
            }
            if (sidecar.Closed)
            {
                failure = "O analisador de poses encerrou antes de ficar pronto.\n" + string.Join("\n", tail);
                yield break;
            }
            if (Time.realtimeSinceStartup > deadline)
            {
                failure = "O modelo de poses demorou demais para carregar.";
                yield break;
            }
            yield return null;
        }
    }

    IEnumerator StartCamera()
    {
        SetStatus("Abrindo a câmera...");
        var devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            failure = "Nenhuma câmera encontrada. Conecte uma webcam e tente de novo.";
            yield break;
        }

        string deviceName = devices[0].name;
        if (!string.IsNullOrEmpty(GameSession.CameraName))
            foreach (var d in devices)
                if (d.name == GameSession.CameraName) { deviceName = d.name; break; }

        webcam = new WebCamTexture(deviceName, 640, 480, 30);
        webcam.Play();
        float deadline = Time.realtimeSinceStartup + 12f;
        while (webcam.width <= 16 && Time.realtimeSinceStartup < deadline) yield return null;
        if (webcam.width <= 16)
        {
            failure = "A câmera não enviou imagem. Ela pode estar em uso por outro programa.";
            yield break;
        }

        // Espelho: o jogador se vê como num espelho. O analisador recebe o quadro já espelhado
        // (flag de espelho), então os pontos que ele devolve batem com o que aparece na tela.
        webcamBottomUp = !webcam.videoVerticallyMirrored;
        cameraImage.texture = webcam;
        cameraImage.uvRect = webcam.videoVerticallyMirrored ? new Rect(1, 1, -1, -1) : new Rect(1, 0, -1, 1);
        cameraFitter.aspectRatio = (float)webcam.width / webcam.height;
        pixels = new Color32[webcam.width * webcam.height];
    }

    IEnumerator LoadAudio(string path)
    {
        SetStatus("Carregando a música...");
        AudioType type = AudioType.MPEG;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".wav") type = AudioType.WAV;
        else if (ext == ".ogg") type = AudioType.OGGVORBIS;

        using (var req = UnityWebRequestMultimedia.GetAudioClip(JustDancePaths.ToFileUrl(path), type))
        {
            ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                failure = "Não consegui carregar a música: " + req.error;
                yield break;
            }
            clip = DownloadHandlerAudioClip.GetContent(req);
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.clip = clip;
        songLength = clip.length;
    }

    IEnumerator PrepareVideo(string path)
    {
        SetStatus("Preparando o vídeo...");
        video = gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.source = VideoSource.Url;
        video.url = JustDancePaths.ToFileUrl(path);
        video.audioOutputMode = VideoAudioOutputMode.None;      // o som vem do AudioSource (relógio mestre)
        video.renderMode = VideoRenderMode.APIOnly;
        video.isLooping = false;
        video.skipOnDrop = true;
        video.waitForFirstFrame = true;
        video.errorReceived += (vp, msg) => videoError = msg;
        video.Prepare();

        float deadline = Time.realtimeSinceStartup + 25f;
        while (!video.isPrepared && videoError == null && Time.realtimeSinceStartup < deadline) yield return null;
        if (videoError != null) { failure = "Não consegui abrir o vídeo da dança: " + videoError; yield break; }
        if (!video.isPrepared) { failure = "O vídeo da dança demorou demais para abrir."; yield break; }

        if (video.width > 0 && video.height > 0)
            referenceFitter.aspectRatio = (float)video.width / video.height;
        if (video.texture != null) referenceImage.texture = video.texture;
    }

    void BuildScoreRows()
    {
        rows = new ScoreRow[playerCount];
        seen = new bool[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            var row = Instantiate(scoreRowTemplate, scoreContainer);
            row.gameObject.SetActive(true);
            row.Setup(i);
            rows[i] = row;
        }
    }

    // -------------------------------------------------------------- a partida

    void BeginRun()
    {
        SetStatus("");
        // O relógio da partida: t = 0 é o instante em que a música começa. Antes disso t < 0 (contagem).
        dspStart = AudioSettings.dspTime + CountdownSeconds;
        audioSource.PlayScheduled(dspStart);
        windowStart = Time.realtimeSinceStartup;
        state = State.Running;
    }

    double SongTime { get { return AudioSettings.dspTime - dspStart; } }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) { BackToMenu(); return; }
            if (keyboard.f3Key.wasPressedThisFrame) { debug = !debug; if (!debug && state == State.Running) SetStatus(""); }
        }

        if (state != State.Running) return;

        double t = SongTime;

        UpdateCountdown(t);
        PumpCamera(t);
        PumpResults(t);
        KeepVideoInSync(t);
        UpdateHud(t);

        if (sidecar.Closed)
        {
            FailRun("O analisador de poses parou de responder.\nA partida foi interrompida.");
            return;
        }
        if (t >= choreo.Duration || (t > 1.0 && !audioSource.isPlaying && t >= songLength - 0.1))
            Finish();
    }

    void UpdateCountdown(double t)
    {
        int shown = t < 0 ? Mathf.CeilToInt((float)-t) : (t < 0.9 ? 0 : -1);
        if (shown == shownCount) return;
        shownCount = shown;
        countdownLabel.text = shown > 0 ? shown.ToString() : (shown == 0 ? "VAI!" : "");
    }

    /// <summary>
    /// Entrega o quadro mais novo da câmera ao analisador, só quando ele está livre.
    /// Se um quadro chegou enquanto o analisador estava ocupado, ele fica "pendente" e sai
    /// assim que o analisador liberar (em vez de esperar a próxima atualização da câmera).
    /// </summary>
    void PumpCamera(double t)
    {
        if (webcam.didUpdateThisFrame) unsentFrame = true;
        if (!unsentFrame || !sidecar.CanSend || !sidecar.IsReady) return;

        int w = webcam.width, h = webcam.height;
        if (pixels.Length != w * h) pixels = new Color32[w * h];
        webcam.GetPixels32(pixels);
        if (sidecar.TrySendFrame(++frameSeq, (int)(t * 1000.0), pixels, w, h, webcamBottomUp, true))
        {
            framesSent++;
            unsentFrame = false;
        }
    }

    void PumpResults(double now)
    {
        PoseFrame f = sidecar.TakeLatest();
        if (f == null) return;

        resultsInWindow++;
        detectMs = Mathf.Lerp(detectMs, f.DetectMs, 0.2f);
        double tFrame = f.TMs / 1000.0;
        latencyMs = Mathf.Lerp(latencyMs, (float)((now - tFrame) * 1000.0), 0.2f);
        skeleton.SetFrame(f);

        if (tFrame < 0 || tFrame >= choreo.Duration) return;      // contagem ou depois do fim

        Array.Clear(seen, 0, seen.Length);
        for (int i = 0; i < f.Count; i++)
        {
            int pid = f.Ids[i];
            if (pid < 0 || pid >= rows.Length) continue;
            seen[pid] = true;

            float score;
            if (!choreo.TryGetAngles(pid, tFrame - ScoreTimeOffset, refAngles)) continue;   // dançarino fora de cena
            if (Scoring.TryScore(f.Angles[i], refAngles, out score)) rows[pid].Report(score);
        }

        // Jogador que a câmera não achou, enquanto o dançarino está em cena: conta zero.
        for (int pid = 0; pid < rows.Length; pid++)
            if (!seen[pid] && choreo.TryGetAngles(pid, tFrame - ScoreTimeOffset, refAngles))
                rows[pid].ReportMissing();
    }

    /// <summary>
    /// O vídeo toca sozinho (decodificado em outra thread pela Unity) e é puxado para a música:
    /// desvio pequeno = ajusta a velocidade levemente; desvio grande = salta (no máximo 1 vez a cada 2 s).
    /// </summary>
    void KeepVideoInSync(double t)
    {
        if (!videoStarted)
        {
            if (t >= -0.05)
            {
                video.Play();
                videoStarted = true;
            }
            return;
        }
        if (referenceImage.texture == null && video.texture != null) referenceImage.texture = video.texture;
        if (!video.isPlaying) return;

        double drift = video.time - t;      // > 0: o vídeo está adiantado
        float speed = 1f;
        if (Math.Abs(drift) > 0.3)
        {
            if (Time.realtimeSinceStartup >= nextSeekAllowed)
            {
                video.time = Math.Max(0.0, t);
                nextSeekAllowed = Time.realtimeSinceStartup + 2f;
            }
        }
        else if (Math.Abs(drift) > 0.03)
        {
            speed = Mathf.Clamp(1f - (float)drift * 0.8f, 0.85f, 1.15f);
        }
        if (Mathf.Abs(speed - lastSpeed) > 0.005f)
        {
            video.playbackSpeed = speed;
            lastSpeed = speed;
        }
    }

    void UpdateHud(double t)
    {
        float now = Time.realtimeSinceStartup;
        if (now >= progressNext)
        {
            progressNext = now + 0.1f;
            float p = Mathf.Clamp01((float)(t / choreo.Duration));
            progressFill.anchorMax = new Vector2(p, 1f);
            int sec = Mathf.Max(0, (int)t), total = (int)choreo.Duration;
            if (sec != shownSecond)
            {
                shownSecond = sec;
                timeLabel.text = string.Format("{0}:{1:00} / {2}:{3:00}", sec / 60, sec % 60, total / 60, total % 60);
            }
        }

        if (now - windowStart >= 1f)
        {
            resultRate = resultsInWindow / (now - windowStart);
            resultsInWindow = 0;
            windowStart = now;
        }

        if (debug && now >= debugNext)
        {
            debugNext = now + 0.5f;
            statusLabel.text = string.Format(
                "tela {0:0} fps | análise {1:0} /s ({2:0} ms) | atraso {3:0} ms | enviados {4}",
                1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f), resultRate, detectMs, latencyMs, framesSent);
        }
    }

    // ------------------------------------------------------------------- fim

    void Finish()
    {
        state = State.Done;
        audioSource.Stop();
        video.Pause();
        skeleton.Clear();
        countdownLabel.text = "";
        SetStatus("");

        var sb = new StringBuilder();
        for (int i = 0; i < rows.Length; i++)
        {
            float avg = rows[i].Average;
            sb.Append("Jogador ").Append(i + 1).Append(":  ").Append(Mathf.RoundToInt(avg))
              .Append("   ").Append(Scoring.Rating(avg)).Append('\n');
        }
        ShowEnd("FIM DA MÚSICA", sb.ToString().TrimEnd(), "JOGAR DE NOVO");
        Cleanup();
    }

    void FailRun(string message)
    {
        state = State.Failed;
        Cleanup();
        ShowEnd("ALGO DEU ERRADO", message, "TENTAR DE NOVO");
    }

    void Fail(string message)
    {
        state = State.Failed;
        SetStatus("");
        ShowEnd("NÃO FOI POSSÍVEL JOGAR", message, "TENTAR DE NOVO");
        Cleanup();
    }

    void ShowEnd(string title, string body, string againText)
    {
        endTitle.text = title;
        endBody.text = body;
        againLabel.text = againText;
        endPanel.SetActive(true);
    }

    void SetStatus(string text)
    {
        statusLabel.text = text;
    }
}
