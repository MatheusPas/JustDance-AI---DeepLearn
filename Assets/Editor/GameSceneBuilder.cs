using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Gera a cena Assets/Scenes/Game.unity no estilo Just Dance: vídeo da dança em NEGATIVO
/// ocupando quase a tela toda, cartões dos jogadores no topo e a câmera numa faixa embaixo.
/// Menu: JustDance > Build Game Scene   (ou Build All para menu + jogo).
/// </summary>
public static class GameSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Game.unity";
    const string MaterialDir = "Assets/Materials";
    const string MaterialPath = "Assets/Materials/UIInvert.mat";
    // Layout (estilo Just Dance): o vídeo da dança ocupa quase toda a tela, os jogadores
    // aparecem em cartões no topo e a câmera fica numa faixa embaixo.
    const float CameraStrip = 0.25f;      // altura da faixa de baixo (fração da tela)
    const float CameraBoxWidth = 340f;    // largura da moldura da câmera (px na resolução 1920x1080)
    const float CardWidth = 290f;         // cartão de cada jogador no topo
    const float CardMinWidth = 190f;      // com muitos jogadores os cartões encolhem até aqui
    const float CardHeight = 118f;

    [MenuItem("JustDance/Build All (Menu + Game)")]
    public static void BuildAll()
    {
        MainMenuSceneBuilder.Build();
        Build();
        EditorSceneManager.OpenScene(MainMenuSceneBuilder.ScenePath);
        Debug.Log("[JustDance] Menu e jogo criados. Abra Assets/Scenes/MainMenu e aperte Play.");
    }

    [MenuItem("JustDance/Build Game Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Directory.CreateDirectory(MainMenuSceneBuilder.ArtDir);
        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory("Assets/Scenes");

        Sprite round = MainMenuSceneBuilder.CreateRoundedRect(MainMenuSceneBuilder.ArtDir + "/rounded.png", 128, 40);
        Sprite gradient = MainMenuSceneBuilder.CreateGradient(MainMenuSceneBuilder.ArtDir + "/background_gradient.png");
        Material invert = GetInvertMaterial();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.02f, 0.12f);

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var canvas = (RectTransform)canvasGo.transform;

        // Fundo (aparece nas laterais quando o vídeo não é 16:9, ex.: vídeo de celular em pé)
        var bg = MainMenuSceneBuilder.NewRect("Background", canvas);
        MainMenuSceneBuilder.Stretch(bg);
        MainMenuSceneBuilder.AddImage(bg, gradient, Color.white, false).raycastTarget = false;

        // ---------- Vídeo da dança (em negativo): quase a tela inteira ----------
        var refBlock = MainMenuSceneBuilder.NewRect("ReferenceBlock", canvas);
        refBlock.anchorMin = new Vector2(0, CameraStrip);
        refBlock.anchorMax = new Vector2(1, 1);
        refBlock.offsetMin = Vector2.zero;
        refBlock.offsetMax = Vector2.zero;
        RawImage reference;
        AspectRatioFitter referenceFitter;
        NewVideoImage("Reference", refBlock, out reference, out referenceFitter);
        if (invert != null) reference.material = invert;

        // ---------- Faixa de baixo: câmera do jogador no centro ----------
        var strip = MainMenuSceneBuilder.NewRect("PlayerStrip", canvas);
        strip.anchorMin = Vector2.zero;
        strip.anchorMax = new Vector2(1, CameraStrip);
        strip.offsetMin = Vector2.zero;
        strip.offsetMax = Vector2.zero;
        MainMenuSceneBuilder.AddImage(strip, null, new Color(0.03f, 0.01f, 0.09f, 0.92f), false).raycastTarget = false;

        var camBlock = NewBlock("CameraBlock", strip, round,
            new Vector2(0.5f, 0), new Vector2(0.5f, 1),
            new Vector2(-CameraBoxWidth / 2f, 14), new Vector2(CameraBoxWidth / 2f, -22));
        RawImage camImage;
        AspectRatioFitter camFitter;
        NewVideoImage("Camera", camBlock, out camImage, out camFitter);
        camFitter.aspectRatio = 4f / 3f;

        var skeletonGo = new GameObject("Skeleton", typeof(RectTransform), typeof(CanvasRenderer), typeof(SkeletonGraphic));
        skeletonGo.transform.SetParent(camImage.rectTransform, false);
        var skeleton = skeletonGo.GetComponent<SkeletonGraphic>();
        MainMenuSceneBuilder.Stretch(skeleton.rectTransform);
        skeleton.raycastTarget = false;
        var camTag = MainMenuSceneBuilder.AddText(camBlock, "Tag", "VOCÊ", 20, new Color(1f, 1f, 1f, 0.6f),
            TextAnchor.UpperLeft, FontStyle.Bold);
        MainMenuSceneBuilder.Place(camTag.rectTransform, 12, 6, 150, 28);

        // Linha do tempo: na divisa entre o vídeo e a faixa de baixo
        var progressBar = MainMenuSceneBuilder.NewRect("ProgressBar", strip);
        progressBar.anchorMin = new Vector2(0, 1);
        progressBar.anchorMax = new Vector2(1, 1);
        progressBar.pivot = new Vector2(0.5f, 1);
        progressBar.offsetMin = new Vector2(0, -8);
        progressBar.offsetMax = Vector2.zero;
        MainMenuSceneBuilder.AddImage(progressBar, null, new Color(1f, 1f, 1f, 0.12f), false).raycastTarget = false;
        var fill = MainMenuSceneBuilder.NewRect("Fill", progressBar);
        fill.anchorMin = new Vector2(0, 0);
        fill.anchorMax = new Vector2(0, 1);
        fill.pivot = new Vector2(0, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        MainMenuSceneBuilder.AddImage(fill, null, MainMenuSceneBuilder.Pink, false).raycastTarget = false;

        // À esquerda da câmera: nome da música e tempo
        var title = MainMenuSceneBuilder.AddText(strip, "Title", "", 44, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
        MainMenuSceneBuilder.Anchor(title.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 34), new Vector2(680, 64));
        title.rectTransform.pivot = new Vector2(0, 0.5f);
        title.rectTransform.anchoredPosition = new Vector2(40, 34);
        var time = MainMenuSceneBuilder.AddText(strip, "Time", "0:00 / 0:00", 36, new Color(0.9f, 0.88f, 1f),
            TextAnchor.UpperLeft, FontStyle.Bold);
        MainMenuSceneBuilder.Anchor(time.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, -30), new Vector2(680, 50));
        time.rectTransform.pivot = new Vector2(0, 0.5f);
        time.rectTransform.anchoredPosition = new Vector2(40, -30);

        // À direita da câmera: mensagens (carregando, F3 = desempenho)
        var status = MainMenuSceneBuilder.AddText(strip, "Status", "Iniciando...", 26, new Color(1f, 1f, 1f, 0.75f),
            TextAnchor.MiddleRight, FontStyle.Normal);
        MainMenuSceneBuilder.Anchor(status.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(680, 200));
        status.rectTransform.pivot = new Vector2(1, 0.5f);
        status.rectTransform.anchoredPosition = new Vector2(-40, 0);

        // ---------- Jogadores: cartões lado a lado no topo, por cima do vídeo ----------
        var scoreColumn = MainMenuSceneBuilder.NewRect("Scoreboard", canvas);
        scoreColumn.anchorMin = new Vector2(0, 1);
        scoreColumn.anchorMax = new Vector2(1, 1);
        scoreColumn.pivot = new Vector2(0.5f, 1);
        scoreColumn.offsetMin = new Vector2(40, -(CardHeight + 24));
        scoreColumn.offsetMax = new Vector2(-40, -24);
        var hlg = scoreColumn.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 22;
        hlg.childAlignment = TextAnchor.UpperCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        var rowTemplate = BuildScoreRowTemplate(scoreColumn, round);

        // Contagem regressiva no meio do vídeo
        var countdown = MainMenuSceneBuilder.AddText(canvas, "Countdown", "", 300, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        float videoCenter = (CameraStrip + 1f) / 2f;
        MainMenuSceneBuilder.Anchor(countdown.rectTransform, new Vector2(0.5f, videoCenter), new Vector2(0.5f, videoCenter),
            Vector2.zero, new Vector2(1000, 420));
        countdown.horizontalOverflow = HorizontalWrapMode.Overflow;
        var outline = countdown.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.1f, 0f, 0.2f, 0.95f);
        outline.effectDistance = new Vector2(8, -8);

        // ---------- Tela final / erro ----------
        var end = MainMenuSceneBuilder.NewRect("EndPanel", canvas);
        MainMenuSceneBuilder.Stretch(end);
        MainMenuSceneBuilder.AddImage(end, null, new Color(0f, 0f, 0f, 0.82f), false);
        var endBox = MainMenuSceneBuilder.NewRect("Box", end);
        MainMenuSceneBuilder.Anchor(endBox, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 700));
        MainMenuSceneBuilder.AddImage(endBox, round, new Color(0.10f, 0.04f, 0.22f, 0.98f), true);
        var endTitle = MainMenuSceneBuilder.AddText(endBox, "Title", "", 76, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
        MainMenuSceneBuilder.Anchor(endTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(1100, 100));
        var endBody = MainMenuSceneBuilder.AddText(endBox, "Body", "", 44, new Color(0.9f, 0.88f, 1f),
            TextAnchor.UpperCenter, FontStyle.Normal);
        MainMenuSceneBuilder.Anchor(endBody.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1060, 380));
        var again = MainMenuSceneBuilder.MakeSmallButton(endBox, "AgainButton", "JOGAR DE NOVO", round, MainMenuSceneBuilder.Pink);
        MainMenuSceneBuilder.Anchor((RectTransform)again.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-250, 50), new Vector2(460, 90));
        var menu = MainMenuSceneBuilder.MakeSmallButton(endBox, "MenuButton", "MENU", round, MainMenuSceneBuilder.Slate);
        MainMenuSceneBuilder.Anchor((RectTransform)menu.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(250, 50), new Vector2(380, 90));

        // ---------- Controller ----------
        var ctrlGo = new GameObject("GameController", typeof(GameController));
        var so = new SerializedObject(ctrlGo.GetComponent<GameController>());
        MainMenuSceneBuilder.Set(so, "referenceImage", reference);
        MainMenuSceneBuilder.Set(so, "referenceFitter", referenceFitter);
        MainMenuSceneBuilder.Set(so, "cameraImage", camImage);
        MainMenuSceneBuilder.Set(so, "cameraFitter", camFitter);
        MainMenuSceneBuilder.Set(so, "skeleton", skeleton);
        MainMenuSceneBuilder.Set(so, "titleLabel", title);
        MainMenuSceneBuilder.Set(so, "timeLabel", time);
        MainMenuSceneBuilder.Set(so, "progressFill", fill);
        MainMenuSceneBuilder.Set(so, "statusLabel", status);
        MainMenuSceneBuilder.Set(so, "countdownLabel", countdown);
        MainMenuSceneBuilder.Set(so, "scoreContainer", scoreColumn);
        MainMenuSceneBuilder.Set(so, "scoreRowTemplate", rowTemplate);
        MainMenuSceneBuilder.Set(so, "endPanel", end.gameObject);
        MainMenuSceneBuilder.Set(so, "endTitle", endTitle);
        MainMenuSceneBuilder.Set(so, "endBody", endBody);
        MainMenuSceneBuilder.Set(so, "againButton", again);
        MainMenuSceneBuilder.Set(so, "againLabel", again.GetComponentInChildren<Text>());
        MainMenuSceneBuilder.Set(so, "menuButton", menu);
        so.ApplyModifiedPropertiesWithoutUndo();

        end.gameObject.SetActive(false);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuild(ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[JustDance] Cena do jogo criada em " + ScenePath);
    }

    // ------------------------------------------------------------------ peças

    static Material GetInvertMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var shader = Shader.Find("JustDance/UIInvert");
        if (shader == null)
        {
            Debug.LogError("[JustDance] Shader JustDance/UIInvert não encontrado (Assets/Shaders/UIInvert.shader). " +
                           "O vídeo de referência aparecerá sem o efeito negativo.");
            return existing;
        }
        if (existing != null)
        {
            existing.shader = shader;
            EditorUtility.SetDirty(existing);
            return existing;
        }
        var mat = new Material(shader) { name = "UIInvert" };
        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    /// <summary>Moldura arredondada; o conteúdo entra como filho esticado dentro dela.</summary>
    static RectTransform NewBlock(string name, RectTransform parent, Sprite round,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = MainMenuSceneBuilder.NewRect(name, parent);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        MainMenuSceneBuilder.AddImage(rt, round, new Color(0.03f, 0.01f, 0.09f, 0.92f), true).raycastTarget = false;
        return rt;
    }

    /// <summary>RawImage que cabe inteira dentro da moldura mantendo a proporção da imagem.</summary>
    static void NewVideoImage(string name, RectTransform block, out RawImage raw, out AspectRatioFitter fitter)
    {
        var rt = MainMenuSceneBuilder.NewRect(name, block);
        MainMenuSceneBuilder.Stretch(rt);
        raw = rt.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        fitter = rt.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;
    }

    /// <summary>
    /// Cartão de um jogador no topo: quadradinho na cor dele + nome e nota na 1ª linha,
    /// avaliação grande embaixo (PERFEITO!, MUITO BEM...).
    /// </summary>
    static ScoreRow BuildScoreRowTemplate(RectTransform parent, Sprite round)
    {
        var rt = MainMenuSceneBuilder.NewRect("ScoreRowTemplate", parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = CardWidth;
        le.minWidth = CardMinWidth;
        le.preferredHeight = CardHeight;
        MainMenuSceneBuilder.AddImage(rt, round, new Color(0.06f, 0.02f, 0.14f, 0.72f), true).raycastTarget = false;

        var accent = MainMenuSceneBuilder.NewRect("Accent", rt);
        MainMenuSceneBuilder.Place(accent, 18, 18, 24, 24);
        var accentImg = MainMenuSceneBuilder.AddImage(accent, null, Color.white, false);
        accentImg.raycastTarget = false;

        var name = MainMenuSceneBuilder.AddText(rt, "Name", "JOGADOR 1", 26, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        StretchTop(name.rectTransform, 52, 100, 8, 50);
        name.horizontalOverflow = HorizontalWrapMode.Overflow;

        var score = MainMenuSceneBuilder.AddText(rt, "Score", "0", 40, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
        MainMenuSceneBuilder.Anchor(score.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -30), new Vector2(110, 50));
        score.rectTransform.pivot = new Vector2(1, 0.5f);
        score.rectTransform.anchoredPosition = new Vector2(-16, -30);
        score.horizontalOverflow = HorizontalWrapMode.Overflow;

        var feedback = MainMenuSceneBuilder.AddText(rt, "Feedback", "", 34, new Color(1f, 0.92f, 0.5f),
            TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
        StretchTop(feedback.rectTransform, 8, 8, 56, 52);
        feedback.horizontalOverflow = HorizontalWrapMode.Overflow;
        var fbOutline = feedback.gameObject.AddComponent<Outline>();
        fbOutline.effectColor = new Color(0.08f, 0f, 0.16f, 0.9f);
        fbOutline.effectDistance = new Vector2(2, -2);

        var row = rt.gameObject.AddComponent<ScoreRow>();
        var so = new SerializedObject(row);
        MainMenuSceneBuilder.Set(so, "accent", accentImg);
        MainMenuSceneBuilder.Set(so, "nameLabel", name);
        MainMenuSceneBuilder.Set(so, "scoreLabel", score);
        MainMenuSceneBuilder.Set(so, "feedbackLabel", feedback);
        so.ApplyModifiedPropertiesWithoutUndo();
        return row;
    }

    /// <summary>Esticado na largura do pai (margens left/right), a "top" px do topo, com altura fixa.</summary>
    static void StretchTop(RectTransform rt, float left, float right, float top, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(left, -(top + height));
        rt.offsetMax = new Vector2(-right, -top);
    }

    static void AddSceneToBuild(string scenePath)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == scenePath);
        list.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
