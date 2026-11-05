using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela "Criar dança": escolhe o vídeo (e, se quiser, uma música própria), dá um nome e
/// treina. O treino roda no Python do projeto (train.py) em outro processo: a tela continua
/// viva e mostra o andamento. Quando termina, a dança já está na galeria.
/// </summary>
public class CreateDancePanel : MonoBehaviour
{
    [Header("Campos")]
    [SerializeField] Text videoLabel;
    [SerializeField] Text musicLabel;
    [SerializeField] InputField nameInput;

    [Header("Botões")]
    [SerializeField] Button pickVideoButton;
    [SerializeField] Button pickMusicButton;
    [SerializeField] Button clearMusicButton;
    [SerializeField] Button trainButton;
    [SerializeField] Text trainLabel;
    [SerializeField] Button backButton;
    [SerializeField] Button galleryButton;
    [SerializeField] Button installButton;

    [Header("Andamento")]
    [SerializeField] GameObject progressGroup;
    [SerializeField] RectTransform progressFill;
    [SerializeField] Text stageLabel;
    [SerializeField] Text messageLabel;

    static readonly Color Info = new Color(0.9f, 0.88f, 1f);
    static readonly Color Good = new Color(0.55f, 1f, 0.65f);
    static readonly Color Bad = new Color(1f, 0.55f, 0.55f);
    static readonly Color Warn = new Color(1f, 0.85f, 0.4f);

    Action onBack, onGallery;
    string videoPath, musicPath;
    ProcessJob pickerJob;
    bool pickingVideo;
    TrainingJob training;
    float trainingStart;
    ProcessJob install;
    readonly List<string> installTail = new List<string>();
    bool overwriteArmed;
    string armedName;

    /// <summary>Não dá para sair no meio do treino ou da instalação.</summary>
    public bool CanLeave { get { return training == null && install == null; } }

    public void Init(Action back, Action openGallery)
    {
        onBack = back;
        onGallery = openGallery;

        pickVideoButton.onClick.AddListener(() => StartPick(true));
        pickMusicButton.onClick.AddListener(() => StartPick(false));
        clearMusicButton.onClick.AddListener(() => { musicPath = null; Refresh(); });
        trainButton.onClick.AddListener(OnTrainClicked);
        backButton.onClick.AddListener(() => { if (CanLeave && onBack != null) onBack(); });
        galleryButton.onClick.AddListener(() => { if (onGallery != null) onGallery(); });
        installButton.onClick.AddListener(StartInstall);
        nameInput.onValueChanged.AddListener(_ => { overwriteArmed = false; });
    }

    void OnEnable()
    {
        if (training == null && install == null) Refresh();
    }

    // ------------------------------------------------------------------ estado

    bool PythonReady { get { return JustDancePaths.PythonInstalled && JustDancePaths.PythonToolsPresent; } }

    bool Busy { get { return training != null || install != null || pickerJob != null; } }

    void Refresh()
    {
        videoLabel.text = string.IsNullOrEmpty(videoPath) ? "nenhum vídeo escolhido" : Path.GetFileName(videoPath);
        musicLabel.text = string.IsNullOrEmpty(musicPath) ? "a do próprio vídeo" : Path.GetFileName(musicPath);

        bool busy = Busy;
        bool ready = PythonReady;

        pickVideoButton.interactable = !busy && ready;
        pickMusicButton.interactable = !busy && ready;
        clearMusicButton.interactable = !busy && !string.IsNullOrEmpty(musicPath);
        nameInput.interactable = !busy;
        trainButton.interactable = ready && (training != null || !busy);
        trainLabel.text = training != null ? "CANCELAR" : "TREINAR";
        backButton.interactable = CanLeave && pickerJob == null;
        installButton.gameObject.SetActive(!ready);
        installButton.interactable = !busy;
        progressGroup.SetActive(training != null || install != null);

        if (!ready && install == null)
            Say("O Python do projeto ainda não foi instalado.\nAperte INSTALAR PYTHON (precisa de internet, leva alguns minutos).", Warn);
    }

    void Say(string text, Color color)
    {
        messageLabel.text = text;
        messageLabel.color = color;
    }

    void SetProgress(float p)
    {
        progressFill.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
    }

    // --------------------------------------------------------- escolher arquivos

    void StartPick(bool video)
    {
        if (Busy) return;
        pickingVideo = video;
        pickerJob = video
            ? PythonRunner.PickFile("Escolha o vídeo da dança",
                "Vídeos|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv|Todos os arquivos|*.*")
            : PythonRunner.PickFile("Escolha a música (opcional)",
                "Áudio|*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.flac|Todos os arquivos|*.*");
        Refresh();
    }

    void PollPicker()
    {
        if (pickerJob == null) return;
        string line;
        while (pickerJob.TryReadLine(out line))
        {
            if (!line.StartsWith("JD|file|", StringComparison.Ordinal)) continue;
            string path = line.Substring(8).Trim();
            if (path.Length == 0) continue;
            if (pickingVideo)
            {
                videoPath = path;
                if (string.IsNullOrWhiteSpace(nameInput.text))
                    nameInput.text = Path.GetFileNameWithoutExtension(path);
            }
            else musicPath = path;
            Say("", Info);
        }
        if (pickerJob.IsDone)
        {
            pickerJob = null;
            Refresh();
        }
    }

    // ------------------------------------------------------------------ treino

    void OnTrainClicked()
    {
        if (training != null)
        {
            training.Cancel();
            return;
        }
        if (Busy) return;

        string songName = nameInput.text == null ? "" : nameInput.text.Trim();
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            Say("Escolha o vídeo da dança primeiro.", Bad);
            return;
        }
        if (songName.Length == 0)
        {
            Say("Dê um nome para a música.", Bad);
            return;
        }
        if (!string.IsNullOrEmpty(musicPath) && !File.Exists(musicPath))
        {
            Say("Não achei o arquivo da música escolhida.", Bad);
            return;
        }

        bool exists = NameExists(songName);
        if (exists && !(overwriteArmed && armedName == songName))
        {
            overwriteArmed = true;
            armedName = songName;
            Say("Já existe uma dança chamada \"" + songName + "\".\nAperte TREINAR de novo para substituir, ou mude o nome.", Warn);
            return;
        }

        galleryButton.gameObject.SetActive(false);
        training = TrainingJob.Start(videoPath, songName, musicPath, exists);
        trainingStart = Time.realtimeSinceStartup;
        SetProgress(0f);
        stageLabel.text = "Iniciando...";
        Say("Pode demorar alguns minutos, dependendo do tamanho do vídeo. Não feche o jogo.", Info);
        Refresh();
    }

    static bool NameExists(string songName)
    {
        bool demo;
        foreach (var e in SongLibrary.Load(null, out demo))
        {
            if (demo) return false;
            if (string.Equals(e.name, songName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    void PollTraining()
    {
        if (training == null) return;
        training.Poll();

        SetProgress(training.Progress);
        float elapsed = Time.realtimeSinceStartup - trainingStart;
        stageLabel.text = string.Format("{0}   ({1}:{2:00})", training.Stage, (int)elapsed / 60, (int)elapsed % 60);

        if (training.Done)
        {
            var entry = training.Entry;
            string text = "Pronta! \"" + entry.name + "\"  -  " + entry.PeopleLabel +
                          "  -  dificuldade: " + entry.difficulty;
            int shown = Mathf.Min(2, training.Warnings.Count);
            for (int i = 0; i < shown; i++) text += "\nAviso: " + training.Warnings[i];
            Say(text, Good);

            training = null;
            videoPath = null;
            musicPath = null;
            nameInput.text = "";
            overwriteArmed = false;
            Refresh();
            progressGroup.SetActive(false);
            galleryButton.gameObject.SetActive(true);
        }
        else if (training.Failed)
        {
            Say(training.Error, Bad);
            training = null;
            Refresh();
        }
    }

    // ------------------------------------------------------------- instalar Python

    void StartInstall()
    {
        if (Busy) return;
        install = PythonRunner.RunSetup(false);
        installTail.Clear();
        SetProgress(0.05f);
        stageLabel.text = "Instalando o Python do projeto...";
        Say("Isso baixa o Python portátil e as bibliotecas (precisa de internet). Pode levar alguns minutos.", Info);
        Refresh();
    }

    void PollInstall()
    {
        if (install == null) return;
        string line;
        while (install.TryReadLine(out line))
        {
            if (line.StartsWith("JD|setup|", StringComparison.Ordinal))
            {
                stageLabel.text = line.Substring(9);
                SetProgress(Mathf.Min(0.95f, progressFill.anchorMax.x + 0.15f));
            }
            else if (line.StartsWith("JD|setup-error|", StringComparison.Ordinal))
            {
                installTail.Add(line.Substring(15));
            }
            else
            {
                installTail.Add(line);
                if (installTail.Count > 5) installTail.RemoveAt(0);
            }
        }

        if (!install.IsDone) return;

        int code = install.ExitCode;
        install = null;
        if (code == 0 && PythonReady)
        {
            Refresh();
            Say("Python instalado! Agora é só escolher o vídeo.", Good);
        }
        else
        {
            Refresh();
            Say("A instalação não terminou (código " + code + ").\n" + string.Join("\n", installTail), Bad);
        }
        progressGroup.SetActive(false);
    }

    // ------------------------------------------------------------------ laço

    void Update()
    {
        PollPicker();
        PollTraining();
        PollInstall();
    }

    void OnDisable()
    {
        // Saiu do painel no meio de uma escolha de arquivo: só o seletor é descartado.
        if (pickerJob != null) { pickerJob.Kill(); pickerJob = null; }
    }
}
