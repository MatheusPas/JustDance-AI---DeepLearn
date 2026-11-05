using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Desenha o esqueleto de cada jogador por cima da imagem da câmera. Fica como filho
/// (esticado) do RawImage da câmera: os pontos chegam em 0..1 com origem no canto
/// superior esquerdo da imagem já espelhada, igual ao que aparece na tela.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class SkeletonGraphic : MaskableGraphic
{
    // Ordem dos 13 pontos: nariz, ombros E/D, cotovelos E/D, pulsos E/D, quadris E/D, joelhos E/D, tornozelos E/D
    static readonly int[,] Bones =
    {
        { 1, 2 }, { 7, 8 }, { 1, 7 }, { 2, 8 },
        { 1, 3 }, { 3, 5 }, { 2, 4 }, { 4, 6 },
        { 7, 9 }, { 9, 11 }, { 8, 10 }, { 10, 12 },
        { 0, 1 },
    };

    static readonly Color[] Palette =
    {
        new Color(1f, 0.24f, 0.68f), new Color(0.12f, 0.85f, 0.95f), new Color(1f, 0.85f, 0.3f),
        new Color(0.4f, 1f, 0.5f), new Color(1f, 0.55f, 0.2f), new Color(0.65f, 0.5f, 1f),
        new Color(1f, 1f, 1f), new Color(0.6f, 0.9f, 0.2f),
    };

    static readonly UIVertex[] quad = new UIVertex[4];

    [SerializeField] float thickness = 7f;

    PoseFrame frame;

    public static Color PlayerColor(int index) { return Palette[((index % Palette.Length) + Palette.Length) % Palette.Length]; }

    public void SetFrame(PoseFrame f)
    {
        frame = f;
        SetVerticesDirty();
    }

    public void Clear()
    {
        if (frame == null) return;
        frame = null;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (frame == null) return;

        Rect r = GetPixelAdjustedRect();
        for (int i = 0; i < frame.Count; i++)
        {
            float[] pts = frame.Points[i];
            if (pts == null) continue;
            Color color = PlayerColor(frame.Ids[i]);

            for (int b = 0; b < Bones.GetLength(0); b++)
            {
                Vector2 a, c;
                if (TryPoint(pts, Bones[b, 0], r, out a) && TryPoint(pts, Bones[b, 1], r, out c))
                    AddLine(vh, a, c, thickness, color);
            }
            for (int k = 0; k < 13; k++)
            {
                Vector2 p;
                if (TryPoint(pts, k, r, out p)) AddDot(vh, p, thickness * 0.9f, Color.white);
            }

            // Número do jogador sobre o quadril (um quadrado colorido, o texto vem do placar)
            Vector2 lh, rh;
            if (TryPoint(pts, 7, r, out lh) && TryPoint(pts, 8, r, out rh))
                AddDot(vh, (lh + rh) * 0.5f, thickness * 2.2f, color);
        }
    }

    static bool TryPoint(float[] pts, int index, Rect r, out Vector2 p)
    {
        float x = pts[index * 2], y = pts[index * 2 + 1];
        if (float.IsNaN(x) || float.IsNaN(y)) { p = Vector2.zero; return false; }
        p = new Vector2(r.xMin + x * r.width, r.yMax - y * r.height);
        return true;
    }

    static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        if (d.sqrMagnitude < 0.01f) return;
        d.Normalize();
        Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);
        Emit(vh, a - n, a + n, b + n, b - n, color);
    }

    static void AddDot(VertexHelper vh, Vector2 c, float size, Color color)
    {
        float h = size * 0.5f;
        Emit(vh, c + new Vector2(-h, -h), c + new Vector2(-h, h), c + new Vector2(h, h), c + new Vector2(h, -h), color);
    }

    static void Emit(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color color)
    {
        Color32 c32 = color;
        quad[0] = Vertex(p0, c32);
        quad[1] = Vertex(p1, c32);
        quad[2] = Vertex(p2, c32);
        quad[3] = Vertex(p3, c32);
        vh.AddUIVertexQuad(quad);
    }

    static UIVertex Vertex(Vector2 p, Color32 color)
    {
        var v = UIVertex.simpleVert;
        v.position = p;
        v.color = color;
        return v;
    }
}
