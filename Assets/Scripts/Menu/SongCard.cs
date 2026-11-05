using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Card de uma música na galeria: capa (placeholder colorido com a inicial),
/// nome e quantidade de pessoas.
/// </summary>
public class SongCard : MonoBehaviour
{
    [SerializeField] Image cover;
    [SerializeField] Text initial;
    [SerializeField] Text nameLabel;
    [SerializeField] Text peopleLabel;
    [SerializeField] Button button;

    public void Setup(SongEntry song, Action<SongEntry> onClick)
    {
        string songName = string.IsNullOrEmpty(song.name) ? "Sem nome" : song.name;
        nameLabel.text = songName;
        peopleLabel.text = song.PeopleLabel;
        initial.text = songName.Substring(0, 1).ToUpperInvariant();

        // Cor da capa estável por nome (hash simples e determinístico).
        uint hash = 17;
        foreach (char c in songName) hash = hash * 31 + c;
        cover.color = Color.HSVToRGB((hash % 360) / 360f, 0.55f, 0.85f);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke(song));
    }
}
