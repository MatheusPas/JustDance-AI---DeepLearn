using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Uma linha do placar: cor e nome do jogador, nota atual e a média da música até agora.
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
        }
    }
}
