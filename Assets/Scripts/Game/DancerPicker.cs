using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// "Quem você vai ser?": em danças com mais de uma pessoa, antes da contagem cada jogador escolhe
/// qual dançarino vai seguir (clique no cartão ou tecla 1..9). Os cartões mostram cada dançarino
/// numa pose em que todos aparecem, na mesma ordem do vídeo (esquerda para a direita).
/// O GameController espera GameSession.ChoosingDancers ficar falso para começar a contagem, e a
/// pontuação usa GameSession.DancerFor(jogador). Também liga os marcadores J1/J2 no vídeo.
/// </summary>
public class DancerPicker : MonoBehaviour
{
    const float CardW = 230f, CardH = 300f;

    int players, current;
    readonly List<int> choice = new List<int>();
    readonly List<int> dancerIds = new List<int>();
    readonly List<PictogramGraphic> cards = new List<PictogramGraphic>();
    readonly List<Text> tags = new List<Text>();
    System.Threading.Tasks.Task<PosesFile> loading;
    RectTransform row;
    Text subtitle;
    Font font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryAttach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { TryAttach(); }

    static void TryAttach()
    {
        GameSession.ChoosingDancers = false;
        var ctrl = FindAnyObjectByType<GameController>();
        if (ctrl == null) return;
        GameSession.DancerOrder = null;                       // escolha vale só para esta partida
        var song = GameSession.SelectedSong;
        if (song == null || string.IsNullOrEmpty(song.poses_path) || song.num_people <= 1) return;
        if (FindAnyObjectByType<DancerPicker>() != null) return;

        int players = Mathf.Clamp(GameSession.PlayerCount, 1, Mathf.Min(song.num_people, PoseProtocol.MaxPlayers));
        GameSession.ChoosingDancers = true;                   // a partida espera a escolha
        var go = new GameObject("DancerPicker", typeof(RectTransform));
        go.AddComponent<DancerPicker>().Begin(song, players);
        DancerMarkers.Attach(ctrl, song, players);
    }

    void OnDestroy() { GameSession.ChoosingDancers = false; }

    void Begin(SongEntry song, int count)
    {
        players = count;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;                            // por cima de toda a tela do jogo
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var dim = NewRect("Dim", transform);
        Stretch(dim);
        var img = dim.gameObject.AddComponent<Image>();
        img.color = new Color(0.04f, 0.01f, 0.10f, 0.94f);

        NewText(dim, "QUEM VOCÊ VAI SER?", 78, Color.white, new Vector2(0, -150), new Vector2(1600, 110));
        subtitle = NewText(dim, "Carregando os dançarinos...", 40, Color.white, new Vector2(0, -250), new Vector2(1600, 60));
        row = NewRect("Cards", dim);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.anchoredPosition = new Vector2(0, -40);
        NewText(dim, "Clique no cartão ou aperte o número   ·   Esc volta ao menu", 26,
                new Color(1f, 1f, 1f, 0.55f), new Vector2(0, -980), new Vector2(1600, 40));

        loading = PoseCache.Load(JustDancePaths.ResolveGameData(song.poses_path));
    }

    void Update()
    {
        if (loading != null)
        {
            if (!loading.IsCompleted) return;
            if (loading.IsFaulted) { Destroy(gameObject); return; }
            BuildCards(loading.Result);
            loading = null;
            UpdateSubtitle();
        }

        var kb = Keyboard.current;
        if (kb == null) return;
        for (int k = 0; k < cards.Count && k < 9; k++)
            if (kb[Key.Digit1 + k].wasPressedThisFrame || kb[Key.Numpad1 + k].wasPressedThisFrame) Choose(k);
    }

    void BuildCards(PosesFile file)
    {
        var dancers = new List<PosesPlayer>(file.players);
        dancers.Sort((a, b) => a.id.CompareTo(b.id));        // ids já vêm da esquerda para a direita

        // um quadro em que o maior número de dançarinos aparece (no miolo da música)
        int n = file.frame_count, best = n / 2, bestCount = -1, step = Mathf.Max(1, Mathf.RoundToInt(file.fps / 2f));
        for (int i = n / 5; i < n * 4 / 5; i += step)
        {
            int c = 0;
            foreach (var d in dancers) if (d.present[i] != 0) c++;
            if (c > bestCount) { bestCount = c; best = i; }
        }

        float total = dancers.Count * CardW + (dancers.Count - 1) * 36f;
        for (int k = 0; k < dancers.Count; k++)
        {
            var d = dancers[k];
            int frame = NearestPresent(d, best);
            float torso = PoseFraming.MedianTorso(d, n);
            float aspect = CardW / CardH;
            float[] fig = null;
            if (frame >= 0)
            {
                var tf = PoseFraming.Fit(d.points, frame, null, torso, aspect);
                fig = PoseFraming.FigureInCard(d.points, frame, tf, aspect / 2f, aspect);
            }

            var card = NewRect("Dancer" + (k + 1), row);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(CardW, CardH);
            card.anchoredPosition = new Vector2(-total / 2f + CardW / 2f + k * (CardW + 36f), 0);
            card.gameObject.AddComponent<CanvasRenderer>();
            var g = card.gameObject.AddComponent<PictogramGraphic>();
            g.Figures = fig != null ? new[] { fig } : null;
            g.Colors = new[] { new Color(0.85f, 0.85f, 0.95f) };
            g.Border = new Color(1f, 1f, 1f, 0.45f);
            g.Refresh();
            int index = k;
            card.gameObject.AddComponent<Button>().onClick.AddListener(() => Choose(index));

            string where = k == 0 ? "mais à esquerda" : k == dancers.Count - 1 ? "mais à direita" : "no meio";
            NewText(card, (k + 1) + "  ·  " + where, 28, Color.white, new Vector2(0, -CardH - 8), new Vector2(CardW + 40, 40));
            var tag = NewText(card, "", 46, Color.white, new Vector2(0, -6), new Vector2(CardW, 60));
            tag.fontStyle = FontStyle.Bold;
            cards.Add(g);
            tags.Add(tag);
            dancerIds.Add(d.id);
        }
    }

    static int NearestPresent(PosesPlayer d, int around)
    {
        for (int off = 0; off < d.present.Length; off++)
        {
            if (around + off < d.present.Length && d.present[around + off] != 0) return around + off;
            if (around - off >= 0 && d.present[around - off] != 0) return around - off;
        }
        return -1;
    }

    void Choose(int k)
    {
        if (current >= players || k >= cards.Count || choice.Contains(dancerIds[k])) return;
        Color c = SkeletonGraphic.PlayerColor(current);
        cards[k].Border = c;
        cards[k].Colors = new[] { c };
        cards[k].Refresh();
        tags[k].text = "J" + (current + 1);
        tags[k].color = c;
        choice.Add(dancerIds[k]);
        current++;
        if (current < players) { UpdateSubtitle(); return; }

        GameSession.DancerOrder = choice.ToArray();
        GameSession.ChoosingDancers = false;
        Destroy(gameObject, 0.6f);                            // deixa ver a última escolha
        subtitle.text = "Prontos!";
    }

    void UpdateSubtitle()
    {
        subtitle.text = "JOGADOR " + (current + 1) + ": escolha o seu dançarino";
        subtitle.color = SkeletonGraphic.PlayerColor(current);
    }

    // ------------------------------------------------------------ peças de UI

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>Texto ancorado no topo-centro do pai, deslocado por pos.</summary>
    Text NewText(Transform parent, string content, int size, Color color, Vector2 pos, Vector2 box)
    {
        var rt = NewRect("Text", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        return t;
    }
}
