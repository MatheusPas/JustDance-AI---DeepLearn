using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Onde ficam as coisas do projeto. A mesma regra vale no Editor e numa build:
/// a "raiz" é a pasta que contém Assets (Editor) ou o .exe (build).
///
///   raiz/PythonRuntime   Python 3.11 portátil (Tools/setup_python_runtime.bat instala)
///   raiz/PythonTools     scripts Python (train.py, analysis_server.py...)
///   raiz/GameData        library.json, danças catalogadas, modelos de pose
/// </summary>
public static class JustDancePaths
{
    public static string ProjectRoot
    {
        get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
    }

    public static string GameData { get { return Path.Combine(ProjectRoot, "GameData"); } }
    public static string LibraryJson { get { return Path.Combine(GameData, "library.json"); } }
    public static string PythonRuntimeDir { get { return Path.Combine(ProjectRoot, "PythonRuntime"); } }
    public static string PythonExe { get { return Path.Combine(PythonRuntimeDir, "python.exe"); } }
    public static string PythonToolsDir { get { return Path.Combine(ProjectRoot, "PythonTools"); } }
    public static string ToolsDir { get { return Path.Combine(ProjectRoot, "Tools"); } }

    public static bool PythonInstalled { get { return File.Exists(PythonExe); } }
    public static bool PythonToolsPresent
    {
        get { return File.Exists(Path.Combine(PythonToolsDir, "train.py")); }
    }

    /// <summary>Caminho guardado no library.json (relativo à GameData, com "/") -> caminho completo.</summary>
    public static string ResolveGameData(string relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        return Path.Combine(GameData, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>file:///C:/... com espaços e acentos codificados (para UnityWebRequest).</summary>
    public static string ToFileUrl(string path)
    {
        return new Uri(path).AbsoluteUri;
    }
}
