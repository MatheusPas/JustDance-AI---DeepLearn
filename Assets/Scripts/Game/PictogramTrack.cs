using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Faixa de pictogramas estilo jogo de dança: cartões com a próxima pose deslizam da direita para
/// a esquerda e chegam ao alvo no instante exato em que o dançarino "trava" aquela pose. Cada
/// cartão mostra o(s) dançarino(s) que os jogadores escolheram seguir, com setas do caminho que
/// mãos e pés fazem até a pose.
///
/// Fica na faixa de baixo, à direita da câmera (não cobre o vídeo), e se liga sozinha à cena do
/// jogo. Os momentos-chave e o enquadramento são calculados numa thread à parte, uma vez por
/// partida; durante o jogo só as posições dos cartões mudam (sem refazer malhas).
/// </summary>
public class PictogramTrack : MonoBehaviour
{
    const float MaxLookAhead = 3.0f;   // s (no máximo) que um cartão leva da direita até o alvo
    const float MinGap = 0.9f;         // s mínimos entre dois pictogramas
    const float HitTime = 0.35f;       // s da animação ao chegar no alvo
    const float PathSeconds = 0.6f;    // trecho de movimento desenhado como seta
    const float CardH = 176f, BarH = CardH + 20f, MaxTrackW = 760f;

    struct Key { public float Time; public float[][] Figures; public float[][][] Paths; }

    GameController controller;
    SongEntry song;
    AudioSource audioSource;
    Task<List<Key>> loading;
    bool started;
    readonly List<Key> keys = new List<Key>();
    readonly List<RectTransform> cards = new List<RectTransform>();
    readonly List<CanvasGroup> groups = new List<CanvasGroup>();
    int players;
    Color[] colors;
    float slotW, cardW, trackW, targetX, lookAhead;
    PictogramGraphic target;
    float lastT;

    // ------------------------------------------------------------ ligação com a cena

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
        var ctrl = FindAnyObjectByType<GameController>();
        var song = GameSession.SelectedSong;
        if (ctrl == null || song == null || string.IsNullOrEmpty(song.poses_path)) return;
        if (FindAnyObjectByType<PictogramTrack>() != null) return;

        // Faixa de baixo (PlayerStrip), à direita da câmera; sem ela, canto inferior direito da tela
        var strip = GameObject.Find("PlayerStrip");
        Transform parent = strip != null ? strip.transform : FindAnyObjectByType<Canvas>()?.transform;
        if (parent == null) return;

        var go = new GameObject("PictogramTrack", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<PictogramTrack>().Begin(ctrl, song, strip != null);
    }

    void Begin(GameController ctrl, SongEntry s, bool inStrip)
    {
        controller = ctrl;
        song = s;
        players = Mathf.Clamp(GameSession.PlayerCount, 1, Mathf.Clamp(song.num_people, 1, 4));
        colors = new Color[players];
        for (int i = 0; i < players; i++) colors[i] = SkeletonGraphic.PlayerColor(i);

        // com mais jogadores os bonequinhos ficam mais estreitos, para a faixa caber ao lado da câmera
        slotW = players == 1 ? 132f : players == 2 ? 104f : 84f;
        cardW = slotW * players + 24f;
        targetX = cardW / 2f + 16f;
        trackW = Mathf.Min(MaxTrackW, cardW * (players == 1 ? 4.2f : 3.2f) + 32f);
        // velocidade mínima para dois pictogramas seguidos (MinGap) nunca se sobreporem
        float travel = trackW - cardW / 2f - 10f - targetX;
        lookAhead = Mathf.Min(MaxLookAhead, travel * MinGap / (cardW + 10f));

        var rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, inStrip ? 0.5f : 0f);
        rt.pivot = new Vector2(1f, inStrip ? 0.5f : 0f);
        rt.anchoredPosition = inStrip ? new Vector2(-28f, -14f) : new Vector2(-28f, 28f);
        rt.sizeDelta = new Vector2(trackW, BarH);
        if (inStrip) MoveStatusAbove();

        var bar = NewGraphic("Bar", trackW, BarH);
        bar.Background = new Color(0.08f, 0.03f, 0.18f, 1f);
        bar.Border = new Color(1f, 1f, 1f, 0.10f);
        ((RectTransform)bar.transform).anchoredPosition = new Vector2(trackW / 2f, BarH / 2f);

        target = NewGraphic("Target", cardW + 12f, CardH + 12f);
        target.FrameOnly = true;
        target.Border = new Color(1f, 0.84f, 0.25f, 0.95f);
        ((RectTransform)target.transform).anchoredPosition = new Vector2(targetX, BarH / 2f);
    }

    /// <summary>O texto de status (carregando, F3) ficava onde a faixa entra: sobe para o canto de cima.</summary>
    void MoveStatusAbove()
    {
        var status = transform.parent.Find("Status");
        if (status == null) return;
        var srt = (RectTransform)status;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 1f);
        srt.anchoredPosition = new Vector2(-28f, -6f);
        srt.sizeDelta = new Vector2(trackW, 30f);
        var text = status.GetComponent<Text>();
        if (text != null) { text.fontSize = 22; text.alignment = TextAnchor.MiddleRight; }
    }

    PictogramGraphic NewGraphic(string name, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(PictogramGraphic));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        var g = go.GetComponent<PictogramGraphic>();
        g.raycastTarget = false;
        return g;
    }

    // ------------------------------------------------------------ jogo

    void Update()
    {
        if (!started)
        {
            if (GameSession.ChoosingDancers) return;              // espera a escolha dos dançarinos
            started = true;
            var dancers = new int[players];
            for (int p = 0; p < players; p++) dancers[p] = GameSession.DancerFor(p);
            var poses = PoseCache.Load(JustDancePaths.ResolveGameData(song.poses_path));
            loading = poses.ContinueWith(t => BuildKeys(t.Result, dancers, slotW));
        }
        if (loading != null)
        {
            if (!loading.IsCompleted) return;
            if (loading.IsFaulted || loading.Result.Count == 0)
            {
                if (loading.IsFaulted) Debug.LogWarning("[JustDance] Pictogramas desligados: " + loading.Exception?.GetBaseException().Message);
                Destroy(gameObject);
                return;
            }
            keys.AddRange(loading.Result);
            loading = null;
            foreach (var k in keys) CreateCard(k);
        }

        if (audioSource == null && controller != null) audioSource = controller.GetComponent<AudioSource>();
        float now = audioSource != null && audioSource.isPlaying ? audioSource.time : lastT;
        lastT = now;

        float pulse = 0f;
        float y = BarH / 2f;
        float travel = trackW - cardW / 2f - 10f - targetX;
        for (int i = 0; i < keys.Count; i++)
        {
            float dt = keys[i].Time - now;
            bool visible = dt <= lookAhead && dt > -HitTime;
            if (cards[i].gameObject.activeSelf != visible) cards[i].gameObject.SetActive(visible);
            if (!visible) continue;

            if (dt >= 0f)
            {
                cards[i].anchoredPosition = new Vector2(targetX + travel * dt / lookAhead, y);
                cards[i].localScale = Vector3.one;
                groups[i].alpha = Mathf.Clamp01((lookAhead - dt) / 0.4f);   // entra suave pela direita
            }
            else
            {
                float u = -dt / HitTime;                                      // 0..1 depois do instante
                cards[i].anchoredPosition = new Vector2(targetX, y);
                cards[i].localScale = Vector3.one * (1f + 0.25f * u);
                groups[i].alpha = 1f - u;
                pulse = Mathf.Max(pulse, 1f - u);
            }
        }
        target.transform.localScale = Vector3.one * (1f + 0.08f * pulse);
    }

    void CreateCard(Key k)
    {
        var g = NewGraphic("Pictogram", cardW, CardH);
        g.Figures = k.Figures;
        g.Paths = k.Paths;
        g.Colors = colors;
        g.Refresh();
        g.gameObject.SetActive(false);
        cards.Add((RectTransform)g.transform);
        groups.Add(g.gameObject.AddComponent<CanvasGroup>());
    }

    // ------------------------------------------------------------ momentos-chave (thread à parte)

    static List<Key> BuildKeys(PosesFile file, int[] dancerIds, float slotW)
    {
        var dancers = new List<PosesPlayer>();
        foreach (int id in dancerIds)
        {
            var d = file.players.Find(p => p.id == id);
            if (d != null) dancers.Add(d);
        }
        int n = file.frame_count;
        float fps = file.fps;
        if (dancers.Count == 0 || n < 2 || file.point_indices.Length != PoseFraming.Points) return new List<Key>();

        // movimento por quadro: deslocamento médio dos pontos (média entre os dançarinos)
        var motion = new float[n];
        for (int i = 1; i < n; i++)
        {
            float sum = 0f; int cnt = 0;
            foreach (var d in dancers)
            {
                if (d.present[i] == 0 || d.present[i - 1] == 0) continue;
                for (int k = 0; k < PoseFraming.Points; k++)
                {
                    int o = (i * PoseFraming.Points + k) * 2, p = ((i - 1) * PoseFraming.Points + k) * 2;
                    if (!PoseFraming.Valid(d.points, o) || !PoseFraming.Valid(d.points, p)) continue;
                    float dx = (d.points[o] - d.points[p]) * PoseFraming.VideoAspect, dy = d.points[o + 1] - d.points[p + 1];
                    sum += Mathf.Sqrt(dx * dx + dy * dy); cnt++;
                }
            }
            motion[i] = cnt > 0 ? sum / cnt : float.NaN;
        }
        var smooth = Smooth(motion, Mathf.Max(1, Mathf.RoundToInt(fps * 0.15f)));

        // candidatos: mínimos locais de movimento logo depois de um movimento forte, com todos visíveis
        int near = Mathf.Max(1, Mathf.RoundToInt(fps * 0.12f)), back = Mathf.Max(2, Mathf.RoundToInt(fps * 0.5f));
        var cand = new List<KeyValuePair<int, float>>();
        for (int i = back; i < n - near; i++)
        {
            if (float.IsNaN(smooth[i])) continue;
            bool all = true;
            foreach (var d in dancers) if (d.present[i] == 0) { all = false; break; }
            if (!all) continue;
            bool isMin = true;
            for (int k = -near; k <= near && isMin; k++)
                if (k != 0 && !float.IsNaN(smooth[i + k]) && smooth[i + k] < smooth[i]) isMin = false;
            if (!isMin) continue;
            float peak = 0f;
            for (int k = i - back; k < i; k++) if (!float.IsNaN(smooth[k])) peak = Mathf.Max(peak, smooth[k]);
            if (peak > smooth[i]) cand.Add(new KeyValuePair<int, float>(i, peak - smooth[i]));
        }
        if (cand.Count == 0) return new List<Key>();
        var strengths = cand.ConvertAll(c => c.Value);
        strengths.Sort();
        float threshold = 0.35f * strengths[strengths.Count / 2];

        cand.Sort((a, b) => b.Value.CompareTo(a.Value));
        var chosen = new List<int>();
        int gap = Mathf.RoundToInt(fps * MinGap);
        foreach (var c in cand)
        {
            if (c.Value < threshold) break;
            if (c.Key < fps * 1.0f) continue;                 // o primeiro precisa de tempo para chegar
            bool far = true;
            foreach (int j in chosen) if (Math.Abs(j - c.Key) < gap) { far = false; break; }
            if (far) chosen.Add(c.Key);
        }
        chosen.Sort();

        var torso = new float[dancers.Count];
        for (int d = 0; d < dancers.Count; d++) torso[d] = PoseFraming.MedianTorso(dancers[d], n);
        float cardAspect = (slotW * dancers.Count + 24f) / CardH;
        float slot = slotW / CardH;
        int pathFrames = Mathf.RoundToInt(fps * PathSeconds), step = Mathf.Max(1, Mathf.RoundToInt(fps / 15f));

        var result = new List<Key>();
        foreach (int i in chosen)
        {
            var figs = new float[dancers.Count][];
            var paths = new float[dancers.Count][][];
            for (int d = 0; d < dancers.Count; d++)
            {
                float slotCenter = 12f / CardH + slot * (d + 0.5f);
                var raw = PoseFraming.MovementPaths(dancers[d], i, pathFrames, step, torso[d]);
                var tf = PoseFraming.Fit(dancers[d].points, i, raw, torso[d], slot);
                figs[d] = PoseFraming.FigureInCard(dancers[d].points, i, tf, slotCenter, cardAspect);
                paths[d] = PoseFraming.PathsInCard(raw, tf, slotCenter, cardAspect);
            }
            result.Add(new Key { Time = i / fps, Figures = figs, Paths = paths });
        }
        return result;
    }

    static float[] Smooth(float[] v, int half)
    {
        var o = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            float s = 0f; int c = 0;
            for (int k = Math.Max(0, i - half); k <= Math.Min(v.Length - 1, i + half); k++)
                if (!float.IsNaN(v[k])) { s += v[k]; c++; }
            o[i] = c > 0 ? s / c : float.NaN;
        }
        return o;
    }
}

/// <summary>Lê cada poses.json uma vez (numa thread à parte) e reaproveita entre pictogramas e escolha de dançarino.</summary>
static class PoseCache
{
    static string cachedPath;
    static Task<PosesFile> cached;

    public static Task<PosesFile> Load(string path)
    {
        if (cached == null || cachedPath != path || cached.IsFaulted)
        {
            cachedPath = path;
            cached = Task.Run(() => JsonUtility.FromJson<PosesFile>(File.ReadAllText(path)));
        }
        return cached;
    }
}

/// <summary>Enquadra poses do poses.json num cartão: corpo inteiro visível, escala fixa pelo tronco.</summary>
static class PoseFraming
{
    public const int Points = 13;
    public const float VideoAspect = 16f / 9f;
    static readonly int[] PathJoints = { 5, 6, 11, 12 };          // pulsos e tornozelos
    static readonly float[] PathMinLength = { 0.9f, 0.9f, 1.2f, 1.2f };     // em tamanhos de tronco
    const int MaxPaths = 2;                                                  // só os movimentos maiores

    public struct Transform2D { public float Cx, Cy, Scale; public bool Ok; }

    public static bool Valid(float[] p, int o)
    {
        float x = p[o], y = p[o + 1];
        return !(x == 0f && y == 0f) && y > -0.3f && y < 1.3f && x > -0.3f && x < 1.3f;
    }

    public static float MedianTorso(PosesPlayer p, int n)
    {
        var list = new List<float>();
        for (int i = 0; i < n; i += 3)
        {
            if (p.present[i] == 0) continue;
            int b = i * Points * 2;
            if (!Valid(p.points, b + 2) || !Valid(p.points, b + 4) || !Valid(p.points, b + 14) || !Valid(p.points, b + 16)) continue;
            float sx = (p.points[b + 2] + p.points[b + 4]) / 2f * VideoAspect, sy = (p.points[b + 3] + p.points[b + 5]) / 2f;
            float hx = (p.points[b + 14] + p.points[b + 16]) / 2f * VideoAspect, hy = (p.points[b + 15] + p.points[b + 17]) / 2f;
            list.Add(Mathf.Sqrt((sx - hx) * (sx - hx) + (sy - hy) * (sy - hy)));
        }
        if (list.Count == 0) return 0.2f;
        list.Sort();
        return Mathf.Max(0.02f, list[list.Count / 2]);
    }

    /// <summary>Caminho (x corrigido pelo aspecto, y) de mãos e pés nos últimos quadros até i; só os que se mexem bastante.</summary>
    public static List<Vector2[]> MovementPaths(PosesPlayer p, int i, int frames, int step, float torso)
    {
        var found = new List<KeyValuePair<float, Vector2[]>>();
        for (int j = 0; j < PathJoints.Length; j++)
        {
            var pts = new List<Vector2>();
            for (int f = Mathf.Max(0, i - frames); f <= i; f += step)
            {
                if (f > i) break;
                int o = (f * Points + PathJoints[j]) * 2;
                if (p.present[f] != 0 && Valid(p.points, o)) pts.Add(new Vector2(p.points[o] * VideoAspect, p.points[o + 1]));
            }
            int last = (i * Points + PathJoints[j]) * 2;
            if (p.present[i] != 0 && Valid(p.points, last))
            {
                var end = new Vector2(p.points[last] * VideoAspect, p.points[last + 1]);
                if (pts.Count == 0 || pts[pts.Count - 1] != end) pts.Add(end);
            }
            if (pts.Count < 3) continue;
            float len = 0f;
            for (int k = 1; k < pts.Count; k++) len += Vector2.Distance(pts[k - 1], pts[k]);
            if (len >= PathMinLength[j] * torso) found.Add(new KeyValuePair<float, Vector2[]>(len / PathMinLength[j], pts.ToArray()));
        }
        found.Sort((a, b) => b.Key.CompareTo(a.Key));
        var result = new List<Vector2[]>();
        for (int k = 0; k < found.Count && k < MaxPaths; k++) result.Add(found[k].Value);
        return result;
    }

    /// <summary>Centro e escala que mostram a pose inteira (e as setas) dentro da vaga do cartão.</summary>
    public static Transform2D Fit(float[] p, int i, List<Vector2[]> paths, float torso, float slot)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int k = 0; k < Points; k++)
        {
            int o = (i * Points + k) * 2;
            if (!Valid(p, o)) continue;
            float x = p[o] * VideoAspect, y = p[o + 1];
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
        }
        if (minX > maxX) return new Transform2D();
        if (paths != null)
            foreach (var path in paths)
                foreach (var v in path)
                {
                    minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
                }
        float scale = 0.80f / (torso * 3.4f);                       // corpo sempre do mesmo tamanho...
        float w = maxX - minX, h = maxY - minY + 0.2f * torso;       // (folga para a cabeça)
        if (w * scale > slot * 0.9f) scale = slot * 0.9f / w;        // ...a não ser que a pose não caiba
        if (h * scale > 0.88f) scale = 0.88f / h;
        return new Transform2D { Cx = (minX + maxX) / 2f, Cy = (minY + maxY) / 2f, Scale = scale, Ok = true };
    }

    /// <summary>13 pontos do quadro i -> coordenadas 0..1 do cartão (y para baixo); NaN = sem ponto.</summary>
    public static float[] FigureInCard(float[] p, int i, Transform2D tf, float slotCenter, float cardAspect)
    {
        var f = new float[Points * 2];
        for (int k = 0; k < f.Length; k++) f[k] = float.NaN;
        if (!tf.Ok) return f;
        for (int k = 0; k < Points; k++)
        {
            int o = (i * Points + k) * 2;
            if (!Valid(p, o)) continue;
            f[2 * k] = (slotCenter + (p[o] * VideoAspect - tf.Cx) * tf.Scale) / cardAspect;
            f[2 * k + 1] = 0.5f + (p[o + 1] - tf.Cy) * tf.Scale;
        }
        return f;
    }

    public static float[][] PathsInCard(List<Vector2[]> paths, Transform2D tf, float slotCenter, float cardAspect)
    {
        var result = new float[paths.Count][];
        if (!tf.Ok) return new float[0][];
        for (int j = 0; j < paths.Count; j++)
        {
            var a = new float[paths[j].Length * 2];
            for (int k = 0; k < paths[j].Length; k++)
            {
                a[2 * k] = (slotCenter + (paths[j][k].x - tf.Cx) * tf.Scale) / cardAspect;
                a[2 * k + 1] = 0.5f + (paths[j][k].y - tf.Cy) * tf.Scale;
            }
            result[j] = a;
        }
        return result;
    }
}
