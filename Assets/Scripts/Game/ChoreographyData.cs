using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ---- Formato do poses.json (colunar e achatado; ver PythonTools/README.md) ----

[Serializable]
class PosesFile
{
    public int format_version;
    public float fps;
    public int frame_count;
    public float duration_sec;
    public string[] angle_names;
    public int[] point_indices;
    public List<PosesPlayer> players = new List<PosesPlayer>();
}

[Serializable]
class PosesPlayer
{
    public int id;
    public float coverage;
    public float intensity;
    public int[] present;     // [frame_count]            1 = dançarino localizado neste frame
    public int[] valid;       // [frame_count * 8]        1 = ângulo confiável
    public float[] angles;    // [frame_count * 8]        graus
    public float[] points;    // [frame_count * 13 * 2]   x,y normalizados
}

/// <summary>
/// O "gabarito" de uma dança: ângulos das articulações de cada dançarino, quadro a quadro.
/// Tempo ↔ índice: i = round(t * fps). Igual ao Choreography do Python.
/// </summary>
public sealed class Choreography
{
    public const int FormatVersion = 1;
    public const int AngleCount = 8;

    readonly Dictionary<int, PosesPlayer> byId = new Dictionary<int, PosesPlayer>();
    public float Fps { get; private set; }
    public int FrameCount { get; private set; }
    public float Duration { get { return FrameCount / Fps; } }
    public int DancerCount { get { return byId.Count; } }

    public static Choreography Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Não achei os movimentos da dança: " + path);

        var file = JsonUtility.FromJson<PosesFile>(File.ReadAllText(path));
        if (file == null || file.format_version != FormatVersion)
            throw new InvalidDataException("A dança está num formato antigo. Refaça em CRIAR DANÇA.");
        if (file.angle_names == null || file.angle_names.Length != AngleCount)
            throw new InvalidDataException("poses.json com número de ângulos inesperado.");
        if (file.fps <= 0f || file.frame_count <= 0)
            throw new InvalidDataException("poses.json sem frames.");

        var c = new Choreography { Fps = file.fps, FrameCount = file.frame_count };
        foreach (var p in file.players)
        {
            if (p.present == null || p.present.Length != file.frame_count ||
                p.angles == null || p.angles.Length != file.frame_count * AngleCount ||
                p.valid == null || p.valid.Length != file.frame_count * AngleCount)
                throw new InvalidDataException("poses.json incompleto (dançarino " + p.id + ").");
            c.byId[p.id] = p;
        }
        return c;
    }

    public int IndexAt(double t)
    {
        int i = (int)(t * Fps + 0.5);
        return Mathf.Clamp(i, 0, FrameCount - 1);
    }

    /// <summary>
    /// Preenche dest[8] com os ângulos do dançarino no instante t (NaN = não confiável).
    /// Devolve false se esse dançarino não aparece no vídeo nesse instante.
    /// </summary>
    public bool TryGetAngles(int dancerId, double t, float[] dest)
    {
        PosesPlayer p;
        if (!byId.TryGetValue(dancerId, out p)) return false;
        int i = IndexAt(t);
        if (p.present[i] == 0) return false;
        int o = i * AngleCount;
        for (int k = 0; k < AngleCount; k++)
            dest[k] = p.valid[o + k] != 0 ? p.angles[o + k] : float.NaN;
        return true;
    }
}

/// <summary>A mesma pontuação do Python (pose_utils.score_angles).</summary>
public static class Scoring
{
    public const float ToleranceDegrees = 35f;
    public const int MinJoints = 3;

    /// <summary>
    /// 0..100 comparando só as articulações confiáveis nos dois lados.
    /// Devolve false se houver poucas em comum (não dá para comparar).
    /// </summary>
    public static bool TryScore(float[] player, float[] reference, out float score)
    {
        float sum = 0f;
        int n = 0;
        for (int k = 0; k < player.Length && k < reference.Length; k++)
        {
            float a = player[k], b = reference[k];
            if (float.IsNaN(a) || float.IsNaN(b)) continue;
            sum += Mathf.Abs(a - b);
            n++;
        }
        if (n < MinJoints) { score = 0f; return false; }
        float avg = sum / n;
        score = Mathf.Max(0f, 100f * (1f - avg / (ToleranceDegrees * 2f)));
        return true;
    }

    public static string Rating(float score)
    {
        if (score >= 85f) return "PERFEITO!";
        if (score >= 70f) return "MUITO BEM";
        if (score >= 50f) return "BOM";
        if (score >= 30f) return "QUASE";
        return "TENTE DE NOVO";
    }
}
