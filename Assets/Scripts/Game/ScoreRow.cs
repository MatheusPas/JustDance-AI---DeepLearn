using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cartão de um jogador no topo da tela: cor e nome, média da música até agora e a
/// avaliação do momento (que "pula" e muda de cor quando muda de nível).
/// </summary>
public class ScoreRow : MonoBehaviour
{
    [SerializeField] Image accent;
    [SerializeField] Text nameLabel;
    [SerializeField] Text scoreLabel;
    [SerializeField] Text feedbackLabel;

    float smoothed;
    float sum;
    int samples;
    int shownScore = -1;
    string shownFeedback;

    public float Average { get { return samples == 0 ? 0f : sum / samples; } }
    public int Samples { get { return samples; } }

    public void Setup(int playerIndex)
    {
        Color c = SkeletonGraphic.PlayerColor(playerIndex);
        accent.color = c;
        nameLabel.text = "JOGADOR " + (playerIndex + 1);
        nameLabel.color = c;
        scoreLabel.text = "0";
        feedbackLabel.text = "";
    }

    /// <summary>Uma comparação válida (0..100) com a coreografia.</summary>
    public void Report(float score)
    {
        sum += score;
        samples++;
        smoothed = samples == 1 ? score : Mathf.Lerp(smoothed, score, 0.2f);
        Refresh();
    }

    /// <summary>O dançarino do vídeo aparece, mas o jogador não foi encontrado pela câmera: conta zero.</summary>
    public void ReportMissing()
    {
        sum += 0f;
        samples++;
        smoothed = Mathf.Lerp(smoothed, 0f, 0.2f);
        Refresh();
    }

    void Refresh()
    {
        int rounded = Mathf.RoundToInt(Average);
        string feedback = Scoring.Rating(smoothed);
        if (rounded != shownScore)
        {
            shownScore = rounded;
            scoreLabel.text = rounded.ToString();
        }
        if (feedback != shownFeedback)
        {
            shownFeedback = feedback;
            feedbackLabel.text = feedback;
            feedbackLabel.color = RatingColor(smoothed);
            pop = 1f;
        }
    }

    float pop;

    void Update()
    {
        if (pop <= 0f) return;
        pop = Mathf.Max(0f, pop - Time.unscaledDeltaTime * 4f);
        float scale = 1f + 0.35f * pop * pop;
        feedbackLabel.rectTransform.localScale = new Vector3(scale, scale, 1f);
    }

    static Color RatingColor(float score)
    {
        if (score >= 85f) return new Color(1f, 0.84f, 0.25f);   // PERFEITO!  dourado
        if (score >= 70f) return new Color(0.45f, 1f, 0.55f);   // MUITO BEM  verde
        if (score >= 50f) return new Color(0.4f, 0.9f, 1f);     // BOM        ciano
        if (score >= 30f) return new Color(1f, 0.65f, 0.3f);    // QUASE      laranja
        return new Color(1f, 0.45f, 0.5f);                      // TENTE DE NOVO
    }
}
