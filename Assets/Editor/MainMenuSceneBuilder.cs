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
    const string ArtDir = "Assets/Art/Placeholders";
    const string PrefabDir = "Assets/Prefabs";
    const string ScenePath = "Assets/Scenes/MainMenu.unity";

    static readonly Color Pink = new Color(1f, 0.24f, 0.68f);
    static readonly Color Cyan = new Color(0.12f, 0.62f, 0.78f);
    static readonly Color Violet = new Color(0.45f, 0.30f, 0.90f);
    static readonly Color Slate = new Color(0.22f, 0.15f, 0.36f);
    static readonly Color PanelDark = new Color(0.06f, 0.02f, 0.14f, 0.78f);

    static Font font;
    static Font UIFont
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

        var empty = AddText(gallery, "EmptyLabel", "Nenhuma música treinada ainda.\nUse o train.py para criar uma.", 44, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, FontStyle.Italic);
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
        so.ApplyModifiedPropertiesWithoutUndo();

        gallery.gameObject.SetActive(false);
        info.gameObject.SetActive(false);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[JustDance] Cena do menu criada em " + ScenePath + ". Aperte Play!");
    }

    // ---------------------------------------------------------------- fundo

    static void BuildBackground(RectTransform canvas, Sprite gradient, Sprite circle)
    {
        var bg = NewRect("Background", canvas);
        Stretch(bg);
        AddImage(bg, gradient, Color.white, false).raycastTarget = false;

        MakeDeco(bg, circle, new Vector2(-300, 250), 900, new Color(1f, 0.24f, 0.68f, 0.16f));
        MakeDeco(bg, circle, new Vector2(520, -280), 700, new Color(0.12f, 0.8f, 0.95f, 0.13f));
        MakeDeco(bg, circle, new Vector2(-650, -380), 520, new Color(0.55f, 0.35f, 1f, 0.18f));
        MakeDeco(bg, circle, new Vector2(760, 330), 300, new Color(1f, 0.85f, 0.3f, 0.12f));
    }

    static void MakeDeco(RectTransform parent, Sprite circle, Vector2 pos, float size, Color color)
    {
        var rt = NewRect("Deco", parent);
        Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
        AddImage(rt, circle, color, false).raycastTarget = false;
    }

    // ------------------------------------------------------------- card prefab

    static SongCard BuildCardPrefab(Sprite round)
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

    static Button MakeMenuButton(RectTransform parent, string name, string label, Sprite round, Color color)
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

    static Button MakeSmallButton(RectTransform parent, string name, string label, Sprite round, Color color)
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

    static void SetTint(Button btn)
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

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static Image AddImage(RectTransform rt, Sprite sprite, Color color, bool sliced)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        return img;
    }

    static Text AddText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor, FontStyle style)
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

    static void Set(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p == null) { Debug.LogError("[JustDance] Campo não encontrado: " + prop); return; }
        p.objectReferenceValue = value;
    }

    static void Set(SerializedObject so, string prop, Component value) => Set(so, prop, (Object)value);

    static void AddToBuildSettings(string scenePath)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == scenePath);
        list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ------------------------------------------------ sprites placeholder (PNG)

    static Sprite CreateRoundedRect(string path, int size, int radius)
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

    static Sprite CreateCircle(string path, int size)
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

    static Sprite CreateGradient(string path)
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

    static Sprite SaveSprite(Texture2D tex, string assetPath, Vector4 border)
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
