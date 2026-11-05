using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catalogação de uma dança nova (train.py). Chame Poll() uma vez por frame e leia
/// Progress / Stage / Done / Error para atualizar a tela.
/// </summary>
public sealed class TrainingJob
{
    readonly ProcessJob job;
    readonly List<string> tail = new List<string>();

    public float Progress { get; private set; }
    public string Stage { get; private set; } = "Iniciando...";
    public bool Done { get; private set; }
    public bool Failed { get; private set; }
    public string Error { get; private set; }
    public SongEntry Entry { get; private set; }
    public readonly List<string> Warnings = new List<string>();

    TrainingJob(ProcessJob job) { this.job = job; }

    public static TrainingJob Start(string videoPath, string songName, string musicPath, bool overwrite)
    {
        var args = new List<string> { "--video", videoPath, "--name", songName, "--machine" };
        if (!string.IsNullOrEmpty(musicPath)) { args.Add("--music"); args.Add(musicPath); }
        if (overwrite) args.Add("--overwrite");
        return new TrainingJob(PythonRunner.RunScript("train.py", args.ToArray()));
    }

    public void Poll()
    {
        if (Done || Failed) return;

        string line;
        while (job.TryReadLine(out line)) Handle(line);

        if (job.IsDone && !Done && !Failed)
        {
            Failed = true;
            string last = tail.Count > 0 ? string.Join("\n", tail) : "sem mensagens";
            Error = "O treinamento terminou sem concluir (código " + job.ExitCode + ").\n" + last;
        }
    }

    public void Cancel()
    {
        job.Kill();
        Failed = true;
        Error = "Cancelado.";
    }

    void Handle(string line)
    {
        if (!line.StartsWith("JD|", StringComparison.Ordinal))
        {
            tail.Add(line);
            if (tail.Count > 6) tail.RemoveAt(0);
            return;
        }

        string[] parts = line.Split(new[] { '|' }, 3);
        if (parts.Length < 3) return;
        string kind = parts[1], data = parts[2];

        switch (kind)
        {
            case "stage":
                Stage = data;
                // 1/4 e 2/4 (separar dança e música) são rápidos; o resto vem do progress.
                if (data.StartsWith("1/4", StringComparison.Ordinal)) Progress = Mathf.Max(Progress, 0.02f);
                else if (data.StartsWith("2/4", StringComparison.Ordinal)) Progress = Mathf.Max(Progress, 0.06f);
                else if (data.StartsWith("4/4", StringComparison.Ordinal)) Progress = Mathf.Max(Progress, 0.96f);
                break;
            case "progress":
                float p;
                if (float.TryParse(data, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out p))
                    Progress = Mathf.Max(Progress, 0.08f + 0.88f * Mathf.Clamp01(p));
                break;
            case "warn":
                Warnings.Add(data);
                break;
            case "error":
                Failed = true;
                Error = data;
                break;
            case "done":
                try
                {
                    Entry = JsonUtility.FromJson<SongEntry>(data);
                    Done = true;
                    Progress = 1f;
                    Stage = "Pronto!";
                }
                catch (Exception e)
                {
                    Failed = true;
                    Error = "Não entendi a resposta do treinamento: " + e.Message;
                }
                break;
        }
    }
}
