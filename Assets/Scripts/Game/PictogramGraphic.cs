using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Um cartão de pictograma: fundo escuro com borda e o(s) bonequinho(s) da pose, em silhueta
/// colorida com contorno branco (estilo dos pictogramas de jogos de dança).
/// Figures[d] = 13 pontos (x,y) já enquadrados em 0..1 do cartão, y para baixo; NaN = sem ponto.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class PictogramGraphic : MaskableGraphic
{
    // Mesma ordem de pontos do poses.json: nariz, ombros E/D, cotovelos E/D, pulsos E/D,
    // quadris E/D, joelhos E/D, tornozelos E/D
    static readonly int[,] Bones =
    {
        { 1, 3 }, { 3, 5 }, { 2, 4 }, { 4, 6 },
        { 7, 9 }, { 9, 11 }, { 8, 10 }, { 10, 12 },
        { 1, 2 }, { 7, 8 }, { 1, 7 }, { 2, 8 },
    };
    const int CircleSegments = 12;

    [System.NonSerialized] public float[][] Figures;   // preenchidos em tempo de execução
    [System.NonSerialized] public float[][][] Paths;   // por dançarino: caminhos de mãos/pés (x,y,x,y...) até a pose
    [System.NonSerialized] public Color[] Colors;
    static readonly Color ArrowColor = new Color(1f, 0.85f, 0.28f);
    static readonly Color ArrowEdge = new Color(0.08f, 0.02f, 0.16f, 0.9f);
    public bool FrameOnly;                                   // só a moldura (alvo da faixa)
    public Color Background = new Color(0.10f, 0.05f, 0.22f, 0.97f);
    public Color Border = new Color(1f, 1f, 1f, 0.35f);

    public void Refresh() { SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float s = r.height;

        if (!FrameOnly) Rect(vh, r.min, r.max, Background);
        float b = Mathf.Max(2f, s * 0.025f);
        Rect(vh, r.min, new Vector2(r.xMax, r.yMin + b), Border);
        Rect(vh, new Vector2(r.xMin, r.yMax - b), r.max, Border);
        Rect(vh, r.min, new Vector2(r.xMin + b, r.yMax), Border);
        Rect(vh, new Vector2(r.xMax - b, r.yMin), r.max, Border);

        if (Figures == null) return;
        float limb = s * 0.07f;
        for (int pass = 0; pass < 2; pass++)          // 0 = contorno branco, 1 = cor do jogador
        {
            for (int d = 0; d < Figures.Length; d++)
            {
                float[] f = Figures[d];
                if (f == null) continue;
                Color c = pass == 0 ? Color.white : Colors[d];
                float w = pass == 0 ? limb * 1.55f : limb;

                // tronco cheio (ombros + quadris)
                if (Ok(f, 1) && Ok(f, 2) && Ok(f, 8) && Ok(f, 7))
                    Quad(vh, P(r, f, 1), P(r, f, 2), P(r, f, 8), P(r, f, 7), c);
                for (int i = 0; i < Bones.GetLength(0); i++)
                {
                    int a = Bones[i, 0], e = Bones[i, 1];
                    if (Ok(f, a) && Ok(f, e)) Capsule(vh, P(r, f, a), P(r, f, e), w * 0.5f, c);
                }
                if (Ok(f, 0)) Circle(vh, P(r, f, 0), s * 0.085f + (pass == 0 ? limb * 0.3f : 0f), c);
            }
        }

        // setas do movimento de mãos e pés (por cima do bonequinho, como nos jogos de dança)
        if (Paths == null) return;
        for (int pass = 0; pass < 2; pass++)
            foreach (var dancer in Paths)
            {
                if (dancer == null) continue;
                foreach (var path in dancer)
                    Arrow(vh, r, path, pass == 0 ? limb * 0.42f : limb * 0.24f, pass == 0 ? ArrowEdge : ArrowColor, pass == 0);
            }
    }

    /// <summary>Trilha que fica mais forte perto do fim, com ponta de seta no destino.</summary>
    static void Arrow(VertexHelper vh, Rect r, float[] path, float radius, Color c, bool edge)
    {
        int n = path.Length / 2;
        if (n < 2) return;
        Vector2 Pt(int k) { return new Vector2(r.xMin + path[2 * k] * r.width, r.yMax - path[2 * k + 1] * r.height); }
        for (int k = 1; k < n; k++)
        {
            Color ck = c;
            if (!edge) ck.a = Mathf.Lerp(0.35f, 1f, k / (float)(n - 1));
            Capsule(vh, Pt(k - 1), Pt(k), radius, ck);
        }
        Vector2 tip = Pt(n - 1), from = Pt(Mathf.Max(0, n - 3));
        Vector2 dir = tip - from;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();
        Vector2 side = new Vector2(-dir.y, dir.x);
        float head = radius * 3.2f;
        Vector2 a = tip + dir * head * 0.9f, b = tip - dir * head * 0.5f + side * head, d = tip - dir * head * 0.5f - side * head;
        int i = vh.currentVertCount;
        vh.AddVert(a, c, Vector4.zero);
        vh.AddVert(b, c, Vector4.zero);
        vh.AddVert(d, c, Vector4.zero);
        vh.AddTriangle(i, i + 1, i + 2);
    }

    static bool Ok(float[] f, int k) { return !float.IsNaN(f[2 * k]); }

    static Vector2 P(Rect r, float[] f, int k)
    {
        return new Vector2(r.xMin + f[2 * k] * r.width, r.yMax - f[2 * k + 1] * r.height);
    }

    static void Rect(VertexHelper vh, Vector2 min, Vector2 max, Color c)
    {
        Quad(vh, min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y), c);
    }

    static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, color, Vector4.zero);
        vh.AddVert(b, color, Vector4.zero);
        vh.AddVert(c, color, Vector4.zero);
        vh.AddVert(d, color, Vector4.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }

    static void Capsule(VertexHelper vh, Vector2 a, Vector2 b, float radius, Color c)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude > 1e-6f)
        {
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * radius;
            Quad(vh, a + n, b + n, b - n, a - n, c);
        }
        Circle(vh, a, radius, c);
        Circle(vh, b, radius, c);
    }

    static void Circle(VertexHelper vh, Vector2 center, float radius, Color c)
    {
        int i = vh.currentVertCount;
        vh.AddVert(center, c, Vector4.zero);
        for (int k = 0; k <= CircleSegments; k++)
        {
            float ang = k * Mathf.PI * 2f / CircleSegments;
            vh.AddVert(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius, c, Vector4.zero);
            if (k > 0) vh.AddTriangle(i, i + k, i + k + 1);
        }
    }
}
