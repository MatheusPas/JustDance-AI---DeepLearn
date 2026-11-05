using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Navegação do menu: principal -> galeria -> (quantos jogadores) -> jogo, e
/// principal -> criar dança. As referências são ligadas pelo MainMenuSceneBuilder
/// (menu JustDance).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Painéis")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject galleryPanel;
    [SerializeField] GameObject infoPanel;
    [SerializeField] GameObject createPanel;
    [SerializeField] GameObject playersPanel;

    [Header("Botões do menu principal")]
    [SerializeField] Button playButton;
    [SerializeField] Button createButton;
    [SerializeField] Button optionsButton;
    [SerializeField] Button quitButton;
    [SerializeField] Text pythonStatusLabel;

    [Header("Botões de voltar")]
    [SerializeField] Button galleryBackButton;
    [SerializeField] Button infoBackButton;

    [Header("Galeria")]
    [SerializeField] RectTransform cardContainer;
    [SerializeField] SongCard cardPrefab;
    [SerializeField] GameObject emptyLabel;
    [SerializeField] Text hintLabel;
    [Tooltip("Opcional: caminho completo de um library.json. Se vazio, usa GameData/library.json " +
             "(o que o CRIAR DANÇA alimenta).")]
    [SerializeField] string libraryJsonPath;

    [Header("Painel de informação")]
    [SerializeField] Text infoTitle;
    [SerializeField] Text infoBody;

    [Header("Criar dança")]
    [SerializeField] CreateDancePanel createDance;

    [Header("Quantos jogadores")]
    [SerializeField] Text playersTitle;
    [SerializeField] Text playersSubtitle;
    [SerializeField] RectTransform playersContainer;
    [SerializeField] Button playersButtonTemplate;
    [SerializeField] Button playersBackButton;

    [Header("Cena do jogo")]
    [SerializeField] string gameSceneName = "Game";

    GameObject current;

    void Awake()
    {
        // Cena gerada por uma versão antiga do MainMenuSceneBuilder: faltam os painéis novos.
        if (createPanel == null || playersPanel == null || createDance == null || pythonStatusLabel == null ||
            playersButtonTemplate == null || playersBackButton == null)
        {
            Debug.LogError("[JustDance] A cena MainMenu está desatualizada. Pare o Play e rode " +
                           "JustDance > Build All (Menu + Game) para gerar as cenas de novo.");
            enabled = false;
            return;
        }

        playButton.onClick.AddListener(OpenGallery);
        createButton.onClick.AddListener(() => Show(createPanel));
        optionsButton.onClick.AddListener(() => ShowInfo("Opções",
            "Em breve: volume, escolha da câmera e dificuldade.\n\n" +
            "Durante a partida, F3 mostra a velocidade da análise (fps, atraso)."));
        quitButton.onClick.AddListener(Quit);
        galleryBackButton.onClick.AddListener(() => Show(mainPanel));
        infoBackButton.onClick.AddListener(() => Show(mainPanel));
        playersBackButton.onClick.AddListener(OpenGallery);
        playersButtonTemplate.gameObject.SetActive(false);
        createDance.Init(() => Show(mainPanel), OpenGallery);

        Show(mainPanel);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || current == mainPanel) return;

        if (current == createPanel && !createDance.CanLeave) return;      // treinando: use CANCELAR
        if (current == playersPanel) OpenGallery();
        else Show(mainPanel);
    }

    void Show(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        galleryPanel.SetActive(panel == galleryPanel);
        infoPanel.SetActive(panel == infoPanel);
        createPanel.SetActive(panel == createPanel);
        playersPanel.SetActive(panel == playersPanel);
        current = panel;
        if (panel == mainPanel) RefreshPythonStatus();
    }

    void RefreshPythonStatus()
    {
        bool ok = JustDancePaths.PythonInstalled && JustDancePaths.PythonToolsPresent;
        pythonStatusLabel.text = ok
            ? "Python do projeto: instalado"
            : "Python do projeto: NÃO instalado. Entre em CRIAR DANÇA e aperte INSTALAR PYTHON.";
        pythonStatusLabel.color = ok ? new Color(0.6f, 1f, 0.7f, 0.8f) : new Color(1f, 0.85f, 0.4f);
    }

    void ShowInfo(string title, string body)
    {
        infoTitle.text = title;
        infoBody.text = body;
        Show(infoPanel);
    }

    void OpenGallery()
    {
        for (int i = cardContainer.childCount - 1; i >= 0; i--)
            Destroy(cardContainer.GetChild(i).gameObject);

        List<SongEntry> songs = SongLibrary.Load(libraryJsonPath, out bool isDemo);
        foreach (var song in songs)
        {
            var card = Instantiate(cardPrefab, cardContainer);
            card.Setup(song, OnSongSelected);
        }

        emptyLabel.SetActive(songs.Count == 0);
        hintLabel.gameObject.SetActive(isDemo);
        hintLabel.text = "Músicas de exemplo. Crie a sua em CRIAR DANÇA.";
        Show(galleryPanel);
    }

    void OnSongSelected(SongEntry song)
    {
        if (string.IsNullOrEmpty(song.poses_path))
        {
            ShowInfo("Música de exemplo",
                song.name + "\n" + song.PeopleLabel + "\n\nEsta é só uma música de exemplo.\n" +
                "Em CRIAR DANÇA você transforma um vídeo seu numa dança jogável.");
            return;
        }

        GameSession.SelectedSong = song;
        if (song.num_people <= 1) StartGame(1);
        else ShowPlayerPicker(song);
    }

    void ShowPlayerPicker(SongEntry song)
    {
        for (int i = playersContainer.childCount - 1; i >= 0; i--)
        {
            var child = playersContainer.GetChild(i).gameObject;
            if (child != playersButtonTemplate.gameObject) Destroy(child);
        }

        playersTitle.text = "Quantos vão dançar?";
        playersSubtitle.text = song.name + "  -  " + song.PeopleLabel + " no vídeo";

        int max = Mathf.Min(song.num_people, PoseProtocol.MaxPlayers);
        for (int n = 1; n <= max; n++)
        {
            int players = n;
            var button = Instantiate(playersButtonTemplate, playersContainer);
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<Text>().text = n == 1 ? "1\nJOGADOR" : n + "\nJOGADORES";
            button.onClick.AddListener(() => StartGame(players));
        }
        Show(playersPanel);
    }

    void StartGame(int players)
    {
        GameSession.PlayerCount = players;

        if (Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        ShowInfo("Quase lá",
            GameSession.SelectedSong.name + "\n\nA cena do jogo ainda não foi criada.\n" +
            "Na Unity: menu JustDance > Build All (Menu + Game).");
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
