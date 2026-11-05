using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Gera a cena Assets/Scenes/Game.unity: vídeo do dançarino em TELA CHEIA (é ele que o
/// jogador precisa enxergar), câmera do jogador em destaque no canto inferior esquerdo
/// (com o esqueleto por cima), placar no canto direito e barra de título no topo.
/// Menu: JustDance > Build Game Scene   (ou Build All para menu + jogo).
/// </summary>
public static class GameSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Game.unity";
    const string MaterialDir = "Assets/Materials";
    const string MaterialPath = "Assets/Materials/UIInvert.mat";
    // Vídeo do dançarino em negativo: desligado porque estourava a luz e dificultava ver os
    // movimentos. O shader/material continuam no projeto; troque para true para voltar.
    const bool ReferenceInNegative = false;
    static readonly Vector2 CameraSize = new Vector2(560, 420);   // 4:3, como a webcam
    const float ScoreWidth = 460f;

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
        Material invert = ReferenceInNegative ? GetInvertMaterial() : null;

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

        // Fundo
        var bg = MainMenuSceneBuilder.NewRect("Background", canvas);
        MainMenuSceneBuilder.Stretch(bg);
        MainMenuSceneBuilder.AddImage(bg, gradient, Color.white, false).raycastTarget = false;

        // ---------- Vídeo do dançarino (tela cheia) ----------
        var refBlock = MainMenuSceneBuilder.NewRect("ReferenceBlock", canvas);
        MainMenuSceneBuilder.Stretch(refBlock);
        MainMenuSceneBuilder.AddImage(refBlock, null, Color.black, false).raycastTarget = false;
        RawImage reference;
        AspectRatioFitter referenceFitter;
        NewVideoImage("Reference", refBlock, out reference, out referenceFitter);
        if (invert != null) reference.material = invert;

        // Barra escura no topo para o título e o tempo ficarem legíveis sobre o vídeo
        var topBar = MainMenuSceneBuilder.NewRect("TopBar", canvas);
        MainMenuSceneBuilder.Anchor(topBar, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 100));
        topBar.pivot = new Vector2(0.5f, 1);
        MainMenuSceneBuilder.AddImage(topBar, null, new Color(0.04f, 0.01f, 0.10f, 0.72f), false).raycastTarget = false;

        // ---------- Câmera do jogador (canto inferior esquerdo, com moldura) ----------
        var camFrame = MainMenuSceneBuilder.NewRect("CameraFrame", canvas);
        MainMenuSceneBuilder.Anchor(camFrame, Vector2.zero, Vector2.zero, new Vector2(30, 30), CameraSize + new Vector2(12, 12));
        camFrame.pivot = Vector2.zero;
        MainMenuSceneBuilder.AddImage(camFrame, round, MainMenuSceneBuilder.Pink, true).raycastTarget = false;
        var camBlock = MainMenuSceneBuilder.NewRect("CameraBlock", camFrame);
        MainMenuSceneBuilder.Stretch(camBlock);
        camBlock.offsetMin = new Vector2(6, 6);
        camBlock.offsetMax = new Vector2(-6, -6);
        MainMenuSceneBuilder.AddImage(camBlock, round, new Color(0.03f, 0.01f, 0.09f, 1f), true).raycastTarget = false;
        camBlock.gameObject.AddComponent<RectMask2D>();
        RawImage camImage;
        AspectRatioFitter camFitter;
        NewVideoImage("Camera", camBlock, out camImage, out camFitter);
        camFitter.aspectRatio = 4f / 3f;

        var skeletonGo = new GameObject("Skeleton", typeof(RectTransform), typeof(CanvasRenderer), typeof(SkeletonGraphic));
        skeletonGo.transform.SetParent(camImage.rectTransform, false);
        var skeleton = skeletonGo.GetComponent<SkeletonGraphic>();
        MainMenuSceneBuilder.Stretch(skeleton.rectTransform);
        skeleton.raycastTarget = false;
        var camTag = MainMenuSceneBuilder.NewRect("Tag", camFrame);
        MainMenuSceneBuilder.Anchor(camTag, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(150, 46));
        camTag.pivot = new Vector2(0, 0);
        MainMenuSceneBuilder.AddImage(camTag, round, MainMenuSceneBuilder.Pink, true).raycastTarget = false;
        var camTagText = MainMenuSceneBuilder.AddText(camTag, "Text", "VOCÊ", 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        MainMenuSceneBuilder.Stretch(camTagText.rectTransform);

        // ---------- HUD ----------
        var progressBar = MainMenuSceneBuilder.NewRect("ProgressBar", canvas);
        progressBar.anchorMin = new Vector2(0, 1);
        progressBar.anchorMax = new Vector2(1, 1);
        progressBar.pivot = new Vector2(0.5f, 1);
        progressBar.offsetMin = new Vector2(0, -10);
        progressBar.offsetMax = Vector2.zero;
        MainMenuSceneBuilder.AddImage(progressBar, null, new Color(1f, 1f, 1f, 0.12f), false).raycastTarget = false;
        var fill = MainMenuSceneBuilder.NewRect("Fill", progressBar);
        fill.anchorMin = new Vector2(0, 0);
        fill.anchorMax = new Vector2(0, 1);
        fill.pivot = new Vector2(0, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        MainMenuSceneBuilder.AddImage(fill, null, MainMenuSceneBuilder.Pink, false).raycastTarget = false;

        var title = MainMenuSceneBuilder.AddText(canvas, "Title", "", 46, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        MainMenuSceneBuilder.Place(title.rectTransform, 40, 22, 1200, 60);
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        var time = MainMenuSceneBuilder.AddText(canvas, "Time", "0:00 / 0:00", 40, new Color(0.9f, 0.88f, 1f),
            TextAnchor.MiddleRight, FontStyle.Bold);
        MainMenuSceneBuilder.Anchor(time.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -20), new Vector2(420, 60));
        time.rectTransform.pivot = new Vector2(1, 1);

        var status = MainMenuSceneBuilder.AddText(canvas, "Status", "Iniciando...", 28, new Color(1f, 1f, 1f, 0.75f),
            TextAnchor.MiddleLeft, FontStyle.Normal);
        MainMenuSceneBuilder.Anchor(status.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 10), new Vector2(1200, 34));
        status.rectTransform.pivot = new Vector2(1, 0);
        status.alignment = TextAnchor.MiddleRight;
        var statusShadow = status.gameObject.AddComponent<Outline>();
        statusShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);

        // Placar (canto superior direito, sobre o vídeo)
        var scoreColumn = MainMenuSceneBuilder.NewRect("Scoreboard", canvas);
        scoreColumn.anchorMin = new Vector2(1, 1);
        scoreColumn.anchorMax = new Vector2(1, 1);
        scoreColumn.pivot = new Vector2(1, 1);
        scoreColumn.anchoredPosition = new Vector2(-30, -125);
        scoreColumn.sizeDelta = new Vector2(ScoreWidth, 700);
        var vlg = scoreColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 20;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var rowTemplate = BuildScoreRowTemplate(scoreColumn, round);

        // Contagem regressiva
        var countdown = MainMenuSceneBuilder.AddText(canvas, "Countdown", "", 300, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        MainMenuSceneBuilder.Anchor(countdown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
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

    static ScoreRow BuildScoreRowTemplate(RectTransform parent, Sprite round)
    {
        var rt = MainMenuSceneBuilder.NewRect("ScoreRowTemplate", parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 150;
        MainMenuSceneBuilder.AddImage(rt, round, new Color(0.10f, 0.05f, 0.22f, 0.80f), true).raycastTarget = false;

        var accent = MainMenuSceneBuilder.NewRect("Accent", rt);
        MainMenuSceneBuilder.Anchor(accent, new Vector2(0, 0), new Vector2(0, 1), new Vector2(22, 0), new Vector2(12, -36));
        accent.pivot = new Vector2(0, 0.5f);
        var accentImg = MainMenuSceneBuilder.AddImage(accent, null, Color.white, false);
        accentImg.raycastTarget = false;

        var name = MainMenuSceneBuilder.AddText(rt, "Name", "JOGADOR 1", 30, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        MainMenuSceneBuilder.Place(name.rectTransform, 50, 12, 250, 44);
        var score = MainMenuSceneBuilder.AddText(rt, "Score", "0", 84, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
        MainMenuSceneBuilder.Place(score.rectTransform, 270, 8, 150, 110);
        score.horizontalOverflow = HorizontalWrapMode.Overflow;
        var feedback = MainMenuSceneBuilder.AddText(rt, "Feedback", "", 30, new Color(1f, 0.92f, 0.5f), TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
        MainMenuSceneBuilder.Place(feedback.rectTransform, 50, 82, 330, 50);
        feedback.horizontalOverflow = HorizontalWrapMode.Overflow;

        var row = rt.gameObject.AddComponent<ScoreRow>();
        var so = new SerializedObject(row);
        MainMenuSceneBuilder.Set(so, "accent", accentImg);
        MainMenuSceneBuilder.Set(so, "nameLabel", name);
        MainMenuSceneBuilder.Set(so, "scoreLabel", score);
        MainMenuSceneBuilder.Set(so, "feedbackLabel", feedback);
        so.ApplyModifiedPropertiesWithoutUndo();
        return row;
    }

    static void AddSceneToBuild(string scenePath)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == scenePath);
        list.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
