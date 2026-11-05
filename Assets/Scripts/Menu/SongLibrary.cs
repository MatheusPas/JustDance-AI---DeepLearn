using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Uma música/coreografia da galeria. Os nomes dos campos seguem o
/// library.json gerado pelo train.py (Python), para ser lido direto.
/// </summary>
[Serializable]
public class SongEntry
{
    public string name;
    public string video_path;
    public string audio_path;
    public string poses_path;
    public float fps;
    public int total_frames;
    public int num_people = 1;

    // Campos novos do catálogo (train.py). Os caminhos são relativos à pasta do library.json.
    public string slug;
    public string folder;
    public string thumbnail_path;
    public float duration_sec;
    public float movement_intensity;
    public string difficulty;

    public string PeopleLabel => num_people == 1 ? "1 pessoa" : num_people + " pessoas";
}

[Serializable]
class SongEntryList
{
    public List<SongEntry> entries = new List<SongEntry>();
}

public static class SongLibrary
{
    /// <summary>
    /// Procura o library.json (caminho informado, StreamingAssets ou
    /// persistentDataPath). Se não achar, devolve músicas de exemplo.
    /// </summary>
    public static List<SongEntry> Load(string overridePath, out bool isDemo)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(overridePath);
        candidates.Add(Path.Combine(Application.streamingAssetsPath, "library.json"));
        candidates.Add(Path.Combine(Application.persistentDataPath, "library.json"));

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                // O Python salva uma lista na raiz; o JsonUtility precisa de um objeto.
                string json = "{\"entries\":" + File.ReadAllText(path) + "}";
                var parsed = JsonUtility.FromJson<SongEntryList>(json);
                if (parsed != null && parsed.entries != null)
                {
                    isDemo = false;
                    return parsed.entries;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SongLibrary] Falha ao ler {path}: {e.Message}");
            }
        }

        isDemo = true;
        return Demo();
    }

    static List<SongEntry> Demo()
    {
        return new List<SongEntry>
        {
            new SongEntry { name = "Sofrência do Verão", num_people = 2, fps = 30 },
            new SongEntry { name = "Treino Solo", num_people = 1, fps = 30 },
            new SongEntry { name = "Funk da Galera", num_people = 3, fps = 30 },
            new SongEntry { name = "Pop Retrô", num_people = 1, fps = 30 },
        };
    }
}
