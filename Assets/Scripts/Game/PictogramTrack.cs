using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Faixa de pictogramas estilo jogo de dança: cartões com a próxima pose deslizam da direita para
/// a esquerda e chegam ao alvo no instante exato em que o dançarino "trava" aquela pose.
///
/// Fica no canto inferior direito do vídeo (onde os vídeos de gameplay já trazem os pictogramas
/// originais, que ficam cobertos) e se liga sozinha à cena do jogo: não altera GameController,
/// GameSceneBuilder nem as cenas. Para remover, basta apagar este arquivo e PictogramGraphic.cs.
///
/// Os momentos-chave e o enquadramento são calculados a partir do poses.json numa thread à parte,
/// uma vez por partida; durante o jogo só as posições dos cartões mudam (sem refazer malhas).
/// </summary>
public class PictogramTrack : MonoBehaviour
{
    const float LookAhead = 3.0f;      // s que um cartão leva da direita até o alvo
    const float MinGap = 0.9f;         // s mínimos entre dois pictogramas
    const float HitTime = 0.35f;       // s da animação ao chegar no alvo
    const float CardH = 168f, Margin = 0f;   // encostada no canto do vídeo
    const float BarH = CardH + 48f;    // alta o bastante para esconder os pictogramas originais
    const float VideoAspect = 16f / 9f;

    struct Key { public float Time; public float[][] Figures; }

    GameController controller;
    AudioSource audioSource;
    Task<List<Key>> loading;
    readonly List<Key> keys = new List<Key>();
    readonly List<RectTransform> cards = new List<RectTransform>();
    readonly List<CanvasGroup> groups = new List<CanvasGroup>();
    Color[] colors;
    float slotW, cardW, trackW, targetX;   // slotW = largura de cada bonequinho no cartão
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

        // Dentro do retângulo do vídeo (RawImage "Reference"), para cobrir os pictogramas originais
        var block = GameObject.Find("ReferenceBlock");
        Transform parent = block != null ? (block.transform.Find("Reference") ?? block.transform) : null;
        if (parent == null)
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;
            parent = canvas.transform;
        }

        var go = new GameObject("PictogramTrack", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<PictogramTrack>().Begin(ctrl, song);
    }

    void Begin(GameController ctrl, SongEntry song)
    {
        controller = ctrl;
        int dancers = Mathf.Clamp(GameSession.PlayerCount, 1, Mathf.Clamp(song.num_people, 1, 4));
        colors = new Color[dancers];
        for (int i = 0; i < dancers; i++) colors[i] = SkeletonGraphic.PlayerColor(i);

        // com mais dançarinos os bonequinhos ficam mais estreitos, para a faixa não cobrir a dança
        slotW = dancers == 1 ? 128f : dancers == 2 ? 100f : 82f;
        cardW = slotW * dancers + 24f;
        targetX = cardW / 2f + 18f;
        trackW = cardW * (dancers == 1 ? 4.2f : 3.2f) + 36f;
        var rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-Margin, Margin);
        rt.sizeDelta = new Vector2(trackW, BarH);

        var bar = NewGraphic("Bar", cardW: trackW, height: BarH);
        bar.FrameOnly = false;
        // opaca: cobre os pictogramas originais do vídeo (no espaço de cor linear do projeto,
        // qualquer transparência sobre o vídeo claro vira cinza)
        bar.Background = new Color(0.03f, 0.01f, 0.08f, 1f);
        bar.Border = new Color(1f, 1f, 1f, 0.12f);
        ((RectTransform)bar.transform).anchoredPosition = new Vector2(trackW / 2f, (BarH) / 2f);

        target = NewGraphic("Target", cardW + 14f, CardH + 14f);
        target.FrameOnly = true;
        target.Border = new Color(1f, 0.84f, 0.25f, 0.95f);
        ((RectTransform)target.transform).anchoredPosition = new Vector2(targetX, (BarH) / 2f);

        string path = JustDancePaths.ResolveGameData(song.poses_path);
        loading = Task.Run(() => BuildKeys(path, dancers));
    }

    PictogramGraphic NewGraphic(string name, float cardW, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(PictogramGraphic));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(cardW, height);
        var g = go.GetComponent<PictogramGraphic>();
        g.raycastTarget = false;
        return g;
    }

    // ------------------------------------------------------------ jogo

    void Update()
    {
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
        float t = audioSource != null && audioSource.isPlaying ? audioSource.time : lastT;
        lastT = t;

        float pulse = 0f;
        float y = (BarH) / 2f;
        float travel = trackW - cardW / 2f - 12f - targetX;
        for (int i = 0; i < keys.Count; i++)
        {
            float dt = keys[i].Time - t;
            bool visible = dt <= LookAhead && dt > -HitTime;
            if (cards[i].gameObject.activeSelf != visible) cards[i].gameObject.SetActive(visible);
            if (!visible) continue;

            if (dt >= 0f)
            {
                cards[i].anchoredPosition = new Vector2(targetX + travel * dt / LookAhead, y);
                cards[i].localScale = Vector3.one;
                groups[i].alpha = Mathf.Clamp01((LookAhead - dt) / 0.4f);   // entra suave pela direita
            }
            else
            {
                float u = -dt / HitTime;                                      // 0..1 depois do instante
                cards[i].anchoredPosition = new Vector2(targetX, y);
                cards[i].localScale = Vector3.one * (1f + 0.3f * u);
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
        g.Colors = colors;
        g.Refresh();
        g.gameObject.SetActive(false);
        cards.Add((RectTransform)g.transform);
        groups.Add(g.gameObject.AddComponent<CanvasGroup>());
    }

    // ------------------------------------------------------------ momentos-chave (thread à parte)

    List<Key> BuildKeys(string posesPath, int wanted)
    {
        var file = JsonUtility.FromJson<PosesFile>(File.ReadAllText(posesPath));
        var dancers = new List<PosesPlayer>(file.players);
        dancers.Sort((a, b) => a.id.CompareTo(b.id));
        if (dancers.Count > wanted) dancers.RemoveRange(wanted, dancers.Count - wanted);
        int n = file.frame_count, pts = file.point_indices.Length;
        float fps = file.fps;
        if (dancers.Count == 0 || n < 2 || pts != 13) return new List<Key>();

        // movimento por quadro: deslocamento médio dos pontos (média entre os dançarinos)
        var motion = new float[n];
        for (int i = 1; i < n; i++)
        {
            float sum = 0f; int cnt = 0;
            foreach (var d in dancers)
            {
                if (d.present[i] == 0 || d.present[i - 1] == 0) continue;
                for (int k = 0; k < pts; k++)
                {
                    int o = (i * pts + k) * 2, p = ((i - 1) * pts + k) * 2;
                    if (!Valid(d.points, o) || !Valid(d.points, p)) continue;
                    float dx = (d.points[o] - d.points[p]) * VideoAspect, dy = d.points[o + 1] - d.points[p + 1];
                    sum += Mathf.Sqrt(dx * dx + dy * dy); cnt++;
                }
            }
            motion[i] = cnt > 0 ? sum / cnt : float.NaN;
        }
        var smooth = Smooth(motion, Mathf.Max(1, Mathf.RoundToInt(fps * 0.15f)));

        // candidatos: mínimos locais de movimento logo depois de um movimento forte
        int near = Mathf.Max(1, Mathf.RoundToInt(fps * 0.12f)), back = Mathf.Max(2, Mathf.RoundToInt(fps * 0.5f));
        var cand = new List<KeyValuePair<int, float>>();
        for (int i = back; i < n - near; i++)
        {
            if (float.IsNaN(smooth[i])) continue;
            bool all = true;                                   // todos os dançarinos mostrados visíveis
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

        // enquadramento: cada dançarino centralizado na sua vaga, com escala fixa pelo tamanho do tronco
        var torso = new float[dancers.Count];
        for (int d = 0; d < dancers.Count; d++) torso[d] = MedianTorso(dancers[d], n);
        var result = new List<Key>();
        float cardAspect = (slotW * dancers.Count + 24f) / CardH;
        float slot = (cardAspect - 24f / CardH) / dancers.Count;
        foreach (int i in chosen)
        {
            var figs = new float[dancers.Count][];
            for (int d = 0; d < dancers.Count; d++)
            {
                if (dancers[d].present[i] == 0) continue;
                float slotCenter = 12f / CardH + slot * (d + 0.5f);
                figs[d] = Frame(dancers[d].points, i, torso[d], slotCenter, slot, cardAspect);
            }
            result.Add(new Key { Time = i / fps, Figures = figs });
        }
        return result;
    }

    static bool Valid(float[] p, int o)
    {
        float x = p[o], y = p[o + 1];
        return !(x == 0f && y == 0f) && y > -0.3f && y < 1.3f && x > -0.3f && x < 1.3f;
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

    static float MedianTorso(PosesPlayer p, int n)
    {
        var list = new List<float>();
        for (int i = 0; i < n; i += 3)
        {
            if (p.present[i] == 0) continue;
            int b = i * 13 * 2;
            if (!Valid(p.points, b + 2) || !Valid(p.points, b + 4) || !Valid(p.points, b + 14) || !Valid(p.points, b + 16)) continue;
            float sx = (p.points[b + 2] + p.points[b + 4]) / 2f * VideoAspect, sy = (p.points[b + 3] + p.points[b + 5]) / 2f;
            float hx = (p.points[b + 14] + p.points[b + 16]) / 2f * VideoAspect, hy = (p.points[b + 15] + p.points[b + 17]) / 2f;
            list.Add(Mathf.Sqrt((sx - hx) * (sx - hx) + (sy - hy) * (sy - hy)));
        }
        if (list.Count == 0) return 0.2f;
        list.Sort();
        return Mathf.Max(0.02f, list[list.Count / 2]);
    }

    /// <summary>13 pontos do quadro i -> coordenadas 0..1 do cartão (y para baixo), corpo inteiro visível.</summary>
    static float[] Frame(float[] p, int i, float torso, float slotCenter, float slot, float cardAspect)
    {
        var x = new float[13]; var y = new float[13]; var ok = new bool[13];
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int k = 0; k < 13; k++)
        {
            int o = (i * 13 + k) * 2;
            if (!Valid(p, o)) continue;
            ok[k] = true;
            x[k] = p[o] * VideoAspect; y[k] = p[o + 1];
            minX = Mathf.Min(minX, x[k]); maxX = Mathf.Max(maxX, x[k]);
            minY = Mathf.Min(minY, y[k]); maxY = Mathf.Max(maxY, y[k]);
        }
        var f = new float[26];
        for (int k = 0; k < 26; k++) f[k] = float.NaN;
        if (minX > maxX) return f;

        // escala fixa (corpo sempre do mesmo tamanho), reduzida só se a pose não couber
        float scale = 0.82f / (torso * 3.4f);
        float w = maxX - minX, h = maxY - minY + 0.18f * torso;      // folga para a cabeça
        if (w * scale > slot * 0.9f) scale = slot * 0.9f / w;
        if (h * scale > 0.88f) scale = 0.88f / h;
        float cx = (minX + maxX) / 2f, cy = (minY + maxY) / 2f;
        for (int k = 0; k < 13; k++)
        {
            if (!ok[k]) continue;
            f[2 * k] = (slotCenter + (x[k] - cx) * scale) / cardAspect;
            f[2 * k + 1] = 0.5f + (y[k] - cy) * scale;
        }
        return f;
    }
}
