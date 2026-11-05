using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Depois de gerar a build do Windows, copia para o lado do .exe o que o jogo usa em tempo de
/// execução: o Python portátil, os scripts, as danças/modelos e Tools (botão INSTALAR PYTHON).
/// O jogo procura tudo em JustDancePaths.ProjectRoot, que numa build é a pasta do .exe.
/// </summary>
class CopyPythonToBuild : IPostprocessBuildWithReport
{
    static readonly string[] Folders = { "PythonRuntime", "PythonTools", "GameData", "Tools" };

    public int callbackOrder { get { return 0; } }

    public void OnPostprocessBuild(BuildReport report)
    {
        var target = report.summary.platform;
        if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows) return;

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string buildDir = Path.GetDirectoryName(report.summary.outputPath);
        foreach (var folder in Folders)
        {
            string from = Path.Combine(root, folder);
            if (!Directory.Exists(from))
            {
                Debug.LogWarning("[JustDance] Pasta " + folder + " não existe; a build vai sem ela." +
                                 (folder == "PythonRuntime" ? " Rode Tools\\setup_python_runtime.bat antes." : ""));
                continue;
            }
            CopyDir(from, Path.Combine(buildDir, folder));
        }
        Debug.Log("[JustDance] Python, danças e ferramentas copiados para " + buildDir);
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(from))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith(".tmp")) continue;   // treino interrompido (GameData/dances/.tmp_*)
            CopyDir(dir, Path.Combine(to, name));
        }
    }
}
