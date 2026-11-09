using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Em danças com mais de uma pessoa, desenha "J1", "J2"... em cima da cabeça do dançarino que cada
/// jogador está seguindo, acompanhando o vídeo. Fica no bloco do vídeo ("ReferenceBlock"), ao lado
/// do RawImage "Reference", e converte os pontos 0..1 do poses.json para o retângulo da imagem.
/// </summary>
public class DancerMarkers : MonoBehaviour
{
    const float Width = 88f, Height = 68f;

    RectTransform video;
    GameController controller;
    AudioSource audioSource;
    PosesFile poses;
    System.Threading.Tasks.Task<PosesFile> loading;
    PosesPlayer[] dancers;
    RectTransform[] markers;
    Vector2[] smoothed;
    float[] torso;
    float lastT;

    public static void Attach(GameController ctrl, SongEntry song, int players)
    {
        var block = GameObject.Find("ReferenceBlock");
        var video = block != null ? block.transform.Find("Reference") : null;
        if (video == null) return;
        var go = new GameObject("DancerMarkers", typeof(RectTransform));
        go.transform.SetParent(block.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var m = go.AddComponent<DancerMarkers>();
        m.video = (RectTransform)video;
        m.controller = ctrl;
        m.loading = PoseCache.Load(JustDancePaths.ResolveGameData(song.poses_path));
        m.markers = new RectTransform[players];
        m.smoothed = new Vector2[players];
        for (int p = 0; p < players; p++) m.markers[p] = m.NewMarker(p);
    }

    RectTransform NewMarker(int player)
    {
        Color c = SkeletonGraphic.PlayerColor(player);
        var go = new GameObject("J" + (player + 1), typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);                    // a ponta do ponteiro fica na posição
        rt.sizeDelta = new Vector2(Width, Height);

        // ponteiro: quadrado girado 45° embaixo da etiqueta
        var tip = NewImage("Ponteiro", rt, c);
        tip.anchorMin = tip.anchorMax = new Vector2(0.5f, 0f);
        tip.sizeDelta = new Vector2(Height * 0.42f, Height * 0.42f);
        tip.anchoredPosition = new Vector2(0f, Height * 0.24f);
        tip.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var box = NewImage("Etiqueta", rt, c);
        box.anchorMin = new Vector2(0f, 0.3f); box.anchorMax = Vector2.one;
        box.offsetMin = box.offsetMax = Vector2.zero;

        var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var lrt = (RectTransform)label.transform;
        lrt.SetParent(box, false);
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var text = label.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "J" + (player + 1);
        text.fontSize = 30;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        go.SetActive(false);
        return rt;
    }

    static RectTransform NewImage(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return (RectTransform)go.transform;
    }

    void Update()
    {
        if (poses == null)
        {
            if (loading == null || !loading.IsCompleted) return;
            if (loading.IsFaulted) { Destroy(gameObject); return; }
            poses = loading.Result;
        }
        if (GameSession.ChoosingDancers) { foreach (var m in markers) m.gameObject.SetActive(false); return; }
        if (dancers == null)
        {
            dancers = new PosesPlayer[markers.Length];
            torso = new float[markers.Length];
            for (int p = 0; p < markers.Length; p++)
            {
                int id = GameSession.DancerFor(p);
                dancers[p] = poses.players.Find(d => d.id == id);
                if (dancers[p] != null) torso[p] = PoseFraming.MedianTorso(dancers[p], poses.frame_count);
            }
        }

        if (audioSource == null && controller != null) audioSource = controller.GetComponent<AudioSource>();
        float t = audioSource != null && audioSource.isPlaying ? audioSource.time : lastT;   // mesmo relógio da faixa
        lastT = t;
        int i = Mathf.Clamp(Mathf.RoundToInt(t * poses.fps), 0, poses.frame_count - 1);
        Rect r = video.rect;                                   // retângulo da imagem do vídeo
        Rect own = ((RectTransform)transform).rect;

        for (int p = 0; p < markers.Length; p++)
        {
            var d = dancers[p];
            Vector2 head = Vector2.zero;
            bool show = d != null && d.present[i] != 0 && Head(d, i, torso[p], out head);
            if (markers[p].gameObject.activeSelf != show) markers[p].gameObject.SetActive(show);
            if (!show) continue;
            // ponto 0..1 do vídeo -> local do vídeo -> mundo -> local deste objeto (relativo ao centro)
            var inVideo = new Vector2(r.xMin + head.x * r.width, r.yMax - head.y * r.height);
            Vector2 local = transform.InverseTransformPoint(video.TransformPoint(inVideo));
            var target = local - own.center;
            smoothed[p] = smoothed[p] == Vector2.zero ? target : Vector2.Lerp(smoothed[p], target, 0.35f);
            markers[p].anchoredPosition = smoothed[p];
        }
    }

    /// <summary>Ponto um pouco acima da cabeça (0..1 do vídeo, y para baixo).</summary>
    static bool Head(PosesPlayer d, int i, float torso, out Vector2 head)
    {
        int o = i * PoseFraming.Points * 2;
        head = Vector2.zero;
        if (PoseFraming.Valid(d.points, o)) head = new Vector2(d.points[o], d.points[o + 1]);
        else if (PoseFraming.Valid(d.points, o + 2) && PoseFraming.Valid(d.points, o + 4))
            head = new Vector2((d.points[o + 2] + d.points[o + 4]) / 2f, (d.points[o + 3] + d.points[o + 5]) / 2f - torso * 0.5f);
        else return false;
        head.y -= torso * 0.3f;                               // logo acima da cabeça
        return true;
    }
}
