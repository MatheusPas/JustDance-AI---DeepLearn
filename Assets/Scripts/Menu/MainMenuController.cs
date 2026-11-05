using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controla a navegação do menu: principal -> galeria de músicas / info.
/// As referências são ligadas pelo MainMenuSceneBuilder (menu JustDance).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Painéis")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject galleryPanel;
    [SerializeField] GameObject infoPanel;

    [Header("Botões do menu principal")]
    [SerializeField] Button playButton;
    [SerializeField] Button createButton;
    [SerializeField] Button optionsButton;
    [SerializeField] Button quitButton;

    [Header("Botões de voltar")]
    [SerializeField] Button galleryBackButton;
    [SerializeField] Button infoBackButton;

    [Header("Galeria")]
    [SerializeField] RectTransform cardContainer;
    [SerializeField] SongCard cardPrefab;
    [SerializeField] GameObject emptyLabel;
    [SerializeField] Text hintLabel;
    [Tooltip("Opcional: caminho completo do library.json gerado pelo train.py. " +
             "Se vazio, procura em StreamingAssets e persistentDataPath.")]
    [SerializeField] string libraryJsonPath;

    [Header("Painel de informação")]
    [SerializeField] Text infoTitle;
    [SerializeField] Text infoBody;

    [Header("Cena do jogo (quando existir)")]
    [SerializeField] string gameSceneName = "Game";

    GameObject current;

    void Awake()
    {
        playButton.onClick.AddListener(OpenGallery);
        createButton.onClick.AddListener(() => ShowInfo("Criar dança",
            "Em breve!\n\nPor enquanto, crie novas coreografias pelo Python:\n" +
            "python train.py --video meu_video.mp4 --name \"Nome da música\""));
        optionsButton.onClick.AddListener(() => ShowInfo("Opções",
            "Em breve: volume, escolha da câmera e dificuldade."));
        quitButton.onClick.AddListener(Quit);
        galleryBackButton.onClick.AddListener(() => Show(mainPanel));
        infoBackButton.onClick.AddListener(() => Show(mainPanel));

        Show(mainPanel);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && current != mainPanel)
            Show(mainPanel);
    }

    void Show(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        galleryPanel.SetActive(panel == galleryPanel);
        infoPanel.SetActive(panel == infoPanel);
        current = panel;
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
        hintLabel.text = "Músicas de exemplo. Coloque o library.json do train.py em Assets/StreamingAssets.";
        Show(galleryPanel);
    }

    void OnSongSelected(SongEntry song)
    {
        GameSession.SelectedSong = song;
        GameSession.PlayerCount = 1;

        if (Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        ShowInfo("Música selecionada",
            $"{song.name}\n{song.PeopleLabel}\n\nA cena de jogo ainda não foi criada (próximo passo).");
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
