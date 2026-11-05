using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Atalhos para rodar o Python do projeto (PythonRuntime) e o PowerShell.
/// </summary>
public static class PythonRunner
{
    static Dictionary<string, string> PythonEnv()
    {
        return new Dictionary<string, string>
        {
            { "JUSTDANCE_ASSETS", JustDancePaths.GameData },
        };
    }

    /// <summary>Roda PythonTools/&lt;script&gt; com os argumentos dados.</summary>
    public static ProcessJob RunScript(string script, params string[] args)
    {
        string scriptPath = Path.Combine(JustDancePaths.PythonToolsDir, script);
        var sb = new StringBuilder(ProcessJob.Quote(scriptPath));
        foreach (var a in args) sb.Append(' ').Append(ProcessJob.Quote(a));
        Directory.CreateDirectory(JustDancePaths.GameData);
        return ProcessJob.Start(JustDancePaths.PythonExe, sb.ToString(), JustDancePaths.ProjectRoot,
            PythonEnv(), "python " + script);
    }

    /// <summary>Instala o Python portátil do projeto (Tools/setup_python_runtime.ps1).</summary>
    public static ProcessJob RunSetup(bool force)
    {
        string ps1 = Path.Combine(JustDancePaths.ToolsDir, "setup_python_runtime.ps1");
        string args = "-NoProfile -ExecutionPolicy Bypass -File " + ProcessJob.Quote(ps1) + (force ? " -Force" : "");
        return ProcessJob.Start("powershell.exe", args, JustDancePaths.ProjectRoot, null, "instalação do Python");
    }

    /// <summary>
    /// Abre a janela "Abrir arquivo" do Windows sem travar o jogo. A resposta chega
    /// como uma linha "JD|file|caminho" (nenhuma linha = o usuário cancelou).
    /// </summary>
    public static ProcessJob PickFile(string title, string filter)
    {
        string script =
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
            "Add-Type -AssemblyName System.Windows.Forms\n" +
            "$owner = New-Object System.Windows.Forms.Form\n" +
            "$owner.TopMost = $true\n" +
            "$d = New-Object System.Windows.Forms.OpenFileDialog\n" +
            "$d.Title = '" + title.Replace("'", "''") + "'\n" +
            "$d.Filter = '" + filter.Replace("'", "''") + "'\n" +
            "if ($d.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) { Write-Output ('JD|file|' + $d.FileName) }\n";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return ProcessJob.Start("powershell.exe", "-NoProfile -STA -EncodedCommand " + encoded,
            JustDancePaths.ProjectRoot, null, "escolher arquivo");
    }
}
