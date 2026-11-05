using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Gera, com um clique, os sprites placeholder (PNG em Assets/Art/Placeholders),
/// o prefab do card de música e a cena Assets/Scenes/MainMenu.unity.
/// Menu: JustDance > Build Main Menu Scene.
/// Para usar arte própria, basta trocar os sprites no Inspector.
/// </summary>
public static class MainMenuSceneBuilder
{
    internal const string ArtDir = "Assets/Art/Placeholders";
    const string PrefabDir = "Assets/Prefabs";
    internal const string ScenePath = "Assets/Scenes/MainMenu.unity";

    internal static readonly Color Pink = new Color(1f, 0.24f, 0.68f);
    internal static readonly Color Cyan = new Color(0.12f, 0.62f, 0.78f);
    internal static readonly Color Violet = new Color(0.45f, 0.30f, 0.90f);
    internal static readonly Color Slate = new Color(0.22f, 0.15f, 0.36f);
    internal static readonly Color PanelDark = new Color(0.06f, 0.02f, 0.14f, 0.78f);

    internal static Font font;
    internal static Font UIFont
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            return font;
        }
    }

    [MenuItem("JustDance/Build Main Menu Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Directory.CreateDirectory(ArtDir);
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/StreamingAssets");

        Sprite round = CreateRoundedRect(ArtDir + "/rounded.png", 128, 40);
        Sprite circle = CreateCircle(ArtDir + "/circle.png", 256);
        Sprite gradient = CreateGradient(ArtDir + "/background_gradient.png");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Câmera (evita o aviso "No cameras rendering") e EventSystem do Input System novo.
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.03f, 0.16f);

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

        SongCard cardPrefab = BuildCardPrefab(round);

        // Canvas
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var canvasRt = (RectTransform)canvasGo.transform;

        BuildBackground(canvasRt, gradient, circle);

        // ---------- Painel principal ----------
        var main = NewRect("MainPanel", canvasRt);
        Stretch(main);

        var logo = AddText(main, "Logo", "JUST DANCE <color=#FF3DAE>AI</color>", 120, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
        Anchor(logo.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(140, -90), new Vector2(1000, 150));
        var subtitle = AddText(main, "Subtitle", "Dance na frente da câmera e ganhe pontos", 38, new Color(0.85f, 0.8f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
        Anchor(subtitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(146, -235), new Vector2(1000, 60));

        var buttons = NewRect("Buttons", main);
        Anchor(buttons, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(140, -90), new Vector2(620, 560));
        var vlg = buttons.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 28;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        Button play = MakeMenuButton(buttons, "PlayButton", "JOGAR", round, Pink);
        Button create = MakeMenuButton(buttons, "CreateButton", "CRIAR DANÇA", round, Cyan);
        Button options = MakeMenuButton(buttons, "OptionsButton", "OPÇÕES", round, Violet);
        Button quit = MakeMenuButton(buttons, "QuitButton", "SAIR", round, Slate);

        // Placeholder da arte do dançarino
        var art = NewRect("DancerArtPlaceholder", main);
        Anchor(art, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-170, -10), new Vector2(640, 820));
        var artImg = AddImage(art, round, new Color(1f, 1f, 1f, 0.07f), true);
        artImg.raycastTarget = false;
        var artLabel = AddText(art, "Label", "[ arte 2D do dançarino ]", 36, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter, FontStyle.Italic);
        Stretch(artLabel.rectTransform);

        var pyStatus = AddText(main, "PythonStatus", "", 28, new Color(0.6f, 1f, 0.7f, 0.8f), TextAnchor.LowerLeft, FontStyle.Normal);
        Anchor(pyStatus.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(140, 40), new Vector2(1500, 44));

        // ---------- Painel da galeria ----------
        var gallery = NewRect("GalleryPanel", canvasRt);
        Stretch(gallery);

        var galTitle = AddText(gallery, "Title", "Escolha a música", 84, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
        Anchor(galTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1200, 110));
        var galBack = MakeSmallButton(gallery, "BackButton", "< VOLTAR", round, Slate);
        Anchor((RectTransform)galBack.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(90, -70), new Vector2(260, 80));

        var scroll = NewRect("Scroll", gallery);
        Stretch(scroll);
        scroll.offsetMin = new Vector2(120, 110);
        scroll.offsetMax = new Vector2(-120, -230);
        var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        var viewport = NewRect("Viewport", scroll);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = new Vector2(0, 0);
        content.offsetMax = new Vector2(0, 0);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(440, 320);
        grid.spacing = new Vector2(40, 40);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.padding = new RectOffset(0, 0, 10, 10);
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.viewport = viewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 40;

        var empty = AddText(gallery, "EmptyLabel", "Nenhuma música treinada ainda.\nUse CRIAR DANÇA para criar a primeira.", 44, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, FontStyle.Italic);
        Stretch(empty.rectTransform);
        var hint = AddText(gallery, "HintLabel", "", 28, new Color(1f, 0.85f, 0.4f), TextAnchor.LowerCenter, FontStyle.Normal);
        Anchor(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(1600, 50));

        // ---------- Painel de informação ----------
        var info = NewRect("InfoPanel", canvasRt);
        Stretch(info);
        var box = NewRect("Box", info);
        Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 640));
        AddImage(box, round, PanelDark, true);
        var infoTitle = AddText(box, "Title", "Título", 70, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
        Anchor(infoTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(1000, 100));
        var infoBody = AddText(box, "Body", "Texto", 40, new Color(0.9f, 0.88f, 1f), TextAnchor.UpperCenter, FontStyle.Normal);
        Anchor(infoBody.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1000, 330));
        var infoBack = MakeSmallButton(box, "BackButton", "VOLTAR", round, Pink);
        Anchor((RectTransform)infoBack.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(300, 80));

        // ---------- Criar dança e quantos jogadores ----------
        CreateDancePanel createComp;
        var createRt = BuildCreatePanel(canvasRt, round, out createComp);
        Text playersTitle, playersSubtitle;
        RectTransform playersContainer;
        Button playersTemplate, playersBack;
        var playersRt = BuildPlayersPanel(canvasRt, round, out playersTitle, out playersSubtitle,
            out playersContainer, out playersTemplate, out playersBack);

        // ---------- Controller ----------
        var ctrlGo = new GameObject("MainMenuController", typeof(MainMenuController));
        var so = new SerializedObject(ctrlGo.GetComponent<MainMenuController>());
        Set(so, "mainPanel", main.gameObject);
        Set(so, "galleryPanel", gallery.gameObject);
        Set(so, "infoPanel", info.gameObject);
        Set(so, "playButton", play);
        Set(so, "createButton", create);
        Set(so, "optionsButton", options);
        Set(so, "quitButton", quit);
        Set(so, "galleryBackButton", galBack);
        Set(so, "infoBackButton", infoBack);
        Set(so, "cardContainer", content);
        Set(so, "cardPrefab", cardPrefab);
        Set(so, "emptyLabel", empty.gameObject);
        Set(so, "hintLabel", hint);
        Set(so, "infoTitle", infoTitle);
        Set(so, "infoBody", infoBody);
        Set(so, "createPanel", createRt.gameObject);
        Set(so, "playersPanel", playersRt.gameObject);
        Set(so, "createDance", createComp);
        Set(so, "pythonStatusLabel", pyStatus);
        Set(so, "playersTitle", playersTitle);
        Set(so, "playersSubtitle", playersSubtitle);
        Set(so, "playersContainer", playersContainer);
        Set(so, "playersButtonTemplate", playersTemplate);
        Set(so, "playersBackButton", playersBack);
        so.ApplyModifiedPropertiesWithoutUndo();

        gallery.gameObject.SetActive(false);
        info.gameObject.SetActive(false);
        createRt.gameObject.SetActive(false);
        playersRt.gameObject.SetActive(false);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[JustDance] Cena do menu criada em " + ScenePath + ". Aperte Play!");
    }

    // ------------------------------------------------------------ criar dança

    /// <summary>Canto superior esquerdo do elemento a (left, top) do canto superior esquerdo do pai.</summary>
    internal static void Place(RectTransform rt, float left, float top, float w, float h)
    {
        Anchor(rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(left, -top), new Vector2(w, h));
    }

    static Text FieldLabel(RectTransform parent, string name, string text, float top)
    {
        var t = AddText(parent, name, text, 34, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        Place(t.rectTransform, 60, top, 380, 70);
        return t;
    }

    static Text ValueBox(RectTransform parent, string name, string text, Sprite round, float left, float top, float width)
    {
        var box = NewRect(name, parent);
        Place(box, left, top, width, 70);
        AddImage(box, round, new Color(1f, 1f, 1f, 0.10f), true).raycastTarget = false;
        var label = AddText(box, "Label", text, 30, new Color(0.9f, 0.88f, 1f), TextAnchor.MiddleLeft, FontStyle.Italic);
        Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(20, 0);
        label.rectTransform.offsetMax = new Vector2(-12, 0);
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 16;
        label.resizeTextMaxSize = 30;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        return label;
    }

    static RectTransform BuildCreatePanel(RectTransform canvas, Sprite round, out CreateDancePanel component)
    {
        var panel = NewRect("CreatePanel", canvas);
        Stretch(panel);
        var box = NewRect("Box", panel);
        Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 900));
        AddImage(box, round, PanelDark, true);

        var title = AddText(box, "Title", "Criar dança", 72, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
        Place(title.rectTransform, 100, 28, 1300, 100);
        var sub = AddText(box, "Subtitle",
            "Escolha um vídeo com a música e o(s) dançarino(s). O jogo separa a música, o vídeo da dança e os movimentos.",
            28, new Color(0.85f, 0.8f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
        Place(sub.rectTransform, 60, 125, 1380, 80);

        // Vídeo
        FieldLabel(box, "VideoTitle", "Vídeo da dança", 230);
        var videoLabel = ValueBox(box, "VideoBox", "nenhum vídeo escolhido", round, 450, 230, 700);
        var pickVideo = MakeSmallButton(box, "PickVideoButton", "ESCOLHER", round, Cyan);
        Place((RectTransform)pickVideo.transform, 1170, 230, 270, 70);

        // Nome
        FieldLabel(box, "NameTitle", "Nome na galeria", 330);
        var inputRt = NewRect("NameInput", box);
        Place(inputRt, 450, 330, 990, 70);
        var inputBg = AddImage(inputRt, round, new Color(1f, 1f, 1f, 0.16f), true);
        var inputText = AddText(inputRt, "Text", "", 32, Color.white, TextAnchor.MiddleLeft, FontStyle.Normal);
        var placeholder = AddText(inputRt, "Placeholder", "Ex.: Sofrência do Verão", 32,
            new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Italic);
        foreach (var t in new[] { inputText, placeholder })
        {
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(20, 6);
            t.rectTransform.offsetMax = new Vector2(-20, -6);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
        inputText.supportRichText = false;
        var input = inputRt.gameObject.AddComponent<InputField>();
        input.targetGraphic = inputBg;
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 60;

        // Música (opcional)
        FieldLabel(box, "MusicTitle", "Música (opcional)", 430);
        var musicLabel = ValueBox(box, "MusicBox", "a do próprio vídeo", round, 450, 430, 480);
        var pickMusic = MakeSmallButton(box, "PickMusicButton", "ESCOLHER", round, Cyan);
        Place((RectTransform)pickMusic.transform, 950, 430, 270, 70);
        var clearMusic = MakeSmallButton(box, "ClearMusicButton", "LIMPAR", round, Slate);
        Place((RectTransform)clearMusic.transform, 1230, 430, 210, 70);

        // Andamento
        var progress = NewRect("Progress", box);
        Place(progress, 60, 535, 1380, 100);
        var bar = NewRect("Bar", progress);
        Place(bar, 0, 0, 1380, 34);
        AddImage(bar, null, new Color(1f, 1f, 1f, 0.15f), false).raycastTarget = false;
        var fill = NewRect("Fill", bar);
        fill.anchorMin = new Vector2(0, 0);
        fill.anchorMax = new Vector2(0, 1);
        fill.pivot = new Vector2(0, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        AddImage(fill, null, Pink, false).raycastTarget = false;
        var stage = AddText(progress, "Stage", "", 30, Color.white, TextAnchor.MiddleLeft, FontStyle.Normal);
        Place(stage.rectTransform, 0, 46, 1380, 50);

        var message = AddText(box, "Message", "", 30, new Color(0.9f, 0.88f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
        Place(message.rectTransform, 60, 650, 1380, 130);

        // Botões de baixo
        var back = MakeSmallButton(box, "BackButton", "< VOLTAR", round, Slate);
        Anchor((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(60, 40), new Vector2(260, 90));
        var train = MakeSmallButton(box, "TrainButton", "TREINAR", round, Pink);
        Anchor((RectTransform)train.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-60, 40), new Vector2(420, 90));
        var gallery = MakeSmallButton(box, "GalleryButton", "VER NA GALERIA", round, Violet);
        Anchor((RectTransform)gallery.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-500, 40), new Vector2(420, 90));
        var install = MakeSmallButton(box, "InstallButton", "INSTALAR PYTHON", round, Cyan);
        Anchor((RectTransform)install.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(480, 90));

        component = panel.gameObject.AddComponent<CreateDancePanel>();
        var so = new SerializedObject(component);
        Set(so, "videoLabel", videoLabel);
        Set(so, "musicLabel", musicLabel);
        Set(so, "nameInput", input);
        Set(so, "pickVideoButton", pickVideo);
        Set(so, "pickMusicButton", pickMusic);
        Set(so, "clearMusicButton", clearMusic);
        Set(so, "trainButton", train);
        Set(so, "trainLabel", train.GetComponentInChildren<Text>());
        Set(so, "backButton", back);
        Set(so, "galleryButton", gallery);
        Set(so, "installButton", install);
        Set(so, "progressGroup", progress.gameObject);
        Set(so, "progressFill", fill);
        Set(so, "stageLabel", stage);
        Set(so, "messageLabel", message);
        so.ApplyModifiedPropertiesWithoutUndo();

        progress.gameObject.SetActive(false);
        gallery.gameObject.SetActive(false);
        install.gameObject.SetActive(false);
        return panel;
    }

    static RectTransform BuildPlayersPanel(RectTransform canvas, Sprite round, out Text title, out Text subtitle,
        out RectTransform container, out Button template, out Button back)
    {
        var panel = NewRect("PlayersPanel", canvas);
        Stretch(panel);

        title = AddText(panel, "Title", "Quantos vão dançar?", 84, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
        Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1400, 110));
        subtitle = AddText(panel, "Subtitle", "", 40, new Color(0.85f, 0.8f, 1f), TextAnchor.UpperCenter, FontStyle.Normal);
        Anchor(subtitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -185), new Vector2(1400, 60));
        back = MakeSmallButton(panel, "BackButton", "< VOLTAR", round, Slate);
        Anchor((RectTransform)back.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(90, -70), new Vector2(260, 80));

        container = NewRect("Buttons", panel);
        Anchor(container, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1800, 260));
        var hlg = container.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 30;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        template = MakeMenuButton(container, "PlayersButtonTemplate", "1\nJOGADOR", round, Pink);
        var le = template.GetComponent<LayoutElement>();
        le.preferredWidth = 270;
        le.preferredHeight = 220;
        template.GetComponentInChildren<Text>().fontSize = 40;
        return panel;
    }

    // ---------------------------------------------------------------- fundo

    internal static void BuildBackground(RectTransform canvas, Sprite gradient, Sprite circle)
    {
        var bg = NewRect("Background", canvas);
        Stretch(bg);
        AddImage(bg, gradient, Color.white, false).raycastTarget = false;

        MakeDeco(bg, circle, new Vector2(-300, 250), 900, new Color(1f, 0.24f, 0.68f, 0.16f));
        MakeDeco(bg, circle, new Vector2(520, -280), 700, new Color(0.12f, 0.8f, 0.95f, 0.13f));
        MakeDeco(bg, circle, new Vector2(-650, -380), 520, new Color(0.55f, 0.35f, 1f, 0.18f));
        MakeDeco(bg, circle, new Vector2(760, 330), 300, new Color(1f, 0.85f, 0.3f, 0.12f));
    }

    internal static void MakeDeco(RectTransform parent, Sprite circle, Vector2 pos, float size, Color color)
    {
        var rt = NewRect("Deco", parent);
        Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
        AddImage(rt, circle, color, false).raycastTarget = false;
    }

    // ------------------------------------------------------------- card prefab

    internal static SongCard BuildCardPrefab(Sprite round)
    {
        var root = new GameObject("SongCard", typeof(RectTransform));
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(440, 320);
        var bg = AddImage(rt, round, new Color(0.14f, 0.07f, 0.28f, 0.92f), true);
        var button = root.AddComponent<Button>();
        button.targetGraphic = bg;
        SetTint(button);

        var cover = NewRect("Cover", rt);
        Anchor(cover, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -16), new Vector2(-32, 180));
        cover.pivot = new Vector2(0.5f, 1);
        var coverImg = AddImage(cover, round, new Color(0.6f, 0.4f, 0.9f), true);
        coverImg.raycastTarget = false;
        var initial = AddText(cover, "Initial", "A", 110, new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(initial.rectTransform);

        var nameLabel = AddText(rt, "Name", "Nome da música", 34, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        Anchor(nameLabel.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 74), new Vector2(-40, 50));
        nameLabel.resizeTextForBestFit = true;
        nameLabel.resizeTextMinSize = 20;
        nameLabel.resizeTextMaxSize = 34;

        var badge = NewRect("PeopleBadge", rt);
        Anchor(badge, new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 18), new Vector2(210, 42));
        badge.pivot = new Vector2(0, 0);
        var badgeImg = AddImage(badge, round, Pink, true);
        badgeImg.raycastTarget = false;
        var people = AddText(badge, "Label", "1 pessoa", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(people.rectTransform);

        var card = root.AddComponent<SongCard>();
        var so = new SerializedObject(card);
        Set(so, "cover", coverImg);
        Set(so, "initial", initial);
        Set(so, "nameLabel", nameLabel);
        Set(so, "peopleLabel", people);
        Set(so, "button", button);
        so.ApplyModifiedPropertiesWithoutUndo();

        string path = PrefabDir + "/SongCard.prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved.GetComponent<SongCard>();
    }

    // ----------------------------------------------------------------- botões

    internal static Button MakeMenuButton(RectTransform parent, string name, string label, Sprite round, Color color)
    {
        var rt = NewRect(name, parent);
        var img = AddImage(rt, round, color, true);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        SetTint(btn);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 110;
        var text = AddText(rt, "Label", label, 50, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(text.rectTransform);
        return btn;
    }

    internal static Button MakeSmallButton(RectTransform parent, string name, string label, Sprite round, Color color)
    {
        var rt = NewRect(name, parent);
        var img = AddImage(rt, round, color, true);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        SetTint(btn);
        var text = AddText(rt, "Label", label, 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(text.rectTransform);
        return btn;
    }

    internal static void SetTint(Button btn)
    {
        var c = btn.colors;
        c.normalColor = Color.white;
        c.highlightedColor = new Color(0.82f, 0.82f, 0.82f);
        c.pressedColor = new Color(0.62f, 0.62f, 0.62f);
        c.selectedColor = Color.white;
        c.colorMultiplier = 1f;
        btn.colors = c;
    }

    // ---------------------------------------------------------- helpers de UI

    internal static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    internal static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    internal static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    internal static Image AddImage(RectTransform rt, Sprite sprite, Color color, bool sliced)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        return img;
    }

    internal static Text AddText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor, FontStyle style)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = UIFont;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    internal static void Set(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p == null) { Debug.LogError("[JustDance] Campo não encontrado: " + prop); return; }
        p.objectReferenceValue = value;
    }

    internal static void Set(SerializedObject so, string prop, Component value) => Set(so, prop, (Object)value);

    internal static void AddToBuildSettings(string scenePath)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == scenePath);
        list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ------------------------------------------------ sprites placeholder (PNG)

    internal static Sprite CreateRoundedRect(string path, int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size / 2f;
        float inner = half - radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs(x + 0.5f - half) - inner;
                float py = Mathf.Abs(y + 0.5f - half) - inner;
                float outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude;
                float dist = outside + Mathf.Min(Mathf.Max(px, py), 0) - radius;
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(0.5f - dist)));
            }
        }
        return SaveSprite(tex, path, new Vector4(radius, radius, radius, radius));
    }

    internal static Sprite CreateCircle(string path, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude - half;
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(0.5f - d)));
            }
        }
        return SaveSprite(tex, path, Vector4.zero);
    }

    internal static Sprite CreateGradient(string path)
    {
        var tex = new Texture2D(4, 256, TextureFormat.RGBA32, false);
        var top = new Color(0.07f, 0.03f, 0.18f);
        var bottom = new Color(0.30f, 0.07f, 0.40f);
        for (int y = 0; y < 256; y++)
        {
            var c = Color.Lerp(bottom, top, y / 255f);
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }
        return SaveSprite(tex, path, Vector4.zero);
    }

    internal static Sprite SaveSprite(Texture2D tex, string assetPath, Vector4 border)
    {
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.spritePixelsPerUnit = 100;
        imp.spriteBorder = border;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }
}
