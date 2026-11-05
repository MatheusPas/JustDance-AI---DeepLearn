using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu JustDance: instala (ou refaz) o Python do projeto e confere se está tudo certo.
/// Faz o mesmo que Tools/setup_python_runtime.bat, com uma barra de andamento na Unity.
/// </summary>
public static class PythonSetupMenu
{
    static ProcessJob job;
    static string stage = "";
    static float progress;
    static readonly List<string> tail = new List<string>();
    static double started;

    [MenuItem("JustDance/Instalar Python do projeto")]
    static void Install() { Start(false); }

    [MenuItem("JustDance/Instalar Python do projeto (refazer do zero)")]
    static void Reinstall()
    {
        if (!EditorUtility.DisplayDialog("Refazer o Python do projeto",
                "Isso apaga a pasta PythonRuntime e instala tudo de novo (precisa de internet). Continuar?",
                "Refazer", "Cancelar")) return;
        Start(true);
    }

    [MenuItem("JustDance/Verificar Python do projeto")]
    static void Check()
    {
        if (!JustDancePaths.PythonInstalled)
        {
            Debug.LogWarning("[JustDance] O Python do projeto não está instalado. Use JustDance > Instalar Python do projeto.");
            return;
        }
        if (job != null) return;
        job = PythonRunner.RunScript("check_runtime.py", "--smoke", "lite");
        BeginPolling("Verificando o Python do projeto...");
    }

    [MenuItem("JustDance/Abrir pasta do projeto")]
    static void OpenRoot() { EditorUtility.RevealInFinder(JustDancePaths.ProjectRoot); }

    static void Start(bool force)
    {
        if (job != null)
        {
            Debug.Log("[JustDance] Já tem uma instalação em andamento.");
            return;
        }
        if (!File.Exists(Path.Combine(JustDancePaths.ToolsDir, "setup_python_runtime.ps1")))
        {
            Debug.LogError("[JustDance] Não achei Tools/setup_python_runtime.ps1 na pasta do projeto.");
            return;
        }
        job = PythonRunner.RunSetup(force);
        BeginPolling("Instalando o Python do projeto...");
    }

    static void BeginPolling(string first)
    {
        stage = first;
        progress = 0.03f;
        tail.Clear();
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (job == null) { EditorApplication.update -= Tick; EditorUtility.ClearProgressBar(); return; }

        string line;
        while (job.TryReadLine(out line))
        {
            if (line.StartsWith("JD|setup|", System.StringComparison.Ordinal))
            {
                stage = line.Substring(9);
                progress = Mathf.Min(0.95f, progress + 0.14f);
                Debug.Log("[JustDance] " + stage);
            }
            else if (line.StartsWith("JD|setup-error|", System.StringComparison.Ordinal))
            {
                Debug.LogError("[JustDance] " + line.Substring(15));
                tail.Add(line.Substring(15));
            }
            else if (line.StartsWith("JD|check|", System.StringComparison.Ordinal))
            {
                Debug.Log("[JustDance] " + line.Substring(9));
            }
            else
            {
                tail.Add(line);
                if (tail.Count > 6) tail.RemoveAt(0);
            }
        }

        int elapsed = (int)(EditorApplication.timeSinceStartup - started);
        bool cancel = EditorUtility.DisplayCancelableProgressBar("JustDance - Python do projeto",
            string.Format("{0}   ({1}:{2:00})", stage, elapsed / 60, elapsed % 60), progress);

        if (cancel)
        {
            job.Kill();
            Finish("Cancelado.", true);
            return;
        }
        if (!job.IsDone) return;

        bool ok = job.ExitCode == 0;
        Finish(ok ? "Pronto: o Python do projeto está funcionando."
                  : "Não terminou (código " + job.ExitCode + ").\n" + string.Join("\n", tail), !ok);
    }

    static void Finish(string message, bool error)
    {
        EditorApplication.update -= Tick;
        EditorUtility.ClearProgressBar();
        job = null;
        if (error) Debug.LogError("[JustDance] " + message);
        else Debug.Log("[JustDance] " + message);
        EditorUtility.DisplayDialog("JustDance - Python do projeto", message, "OK");
    }
}
