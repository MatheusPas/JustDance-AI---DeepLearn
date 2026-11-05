using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Um processo externo (Python, PowerShell) rodando em segundo plano. A saída é lida
/// por threads do .NET e entregue em linhas; quem usa chama TryReadLine() no Update.
/// Todos os processos abertos são encerrados quando o jogo (ou o Editor) fecha.
/// </summary>
public sealed class ProcessJob
{
    static readonly List<ProcessJob> Alive = new List<ProcessJob>();
    static bool hooked;

    readonly Process process;
    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
    int openStreams = 2;
    volatile bool exited;
    int exitCode = -1;

    public string Description { get; private set; }
    public int ExitCode { get { return exitCode; } }

    /// <summary>Terminou e toda a saída já foi lida.</summary>
    public bool IsDone { get { return exited && System.Threading.Volatile.Read(ref openStreams) <= 0 && lines.IsEmpty; } }

    ProcessJob(Process p, string description)
    {
        process = p;
        Description = description;
    }

    /// <summary>Linhas do stderr chegam com o prefixo "ERR|".</summary>
    public static ProcessJob Start(string exe, string arguments, string workingDir,
        IDictionary<string, string> env, string description)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        if (env != null)
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var job = new ProcessJob(p, description);

        p.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null) job.StreamClosed();
            else job.lines.Enqueue(e.Data);
        };
        p.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null) job.StreamClosed();
            else job.lines.Enqueue("ERR|" + e.Data);
        };
        p.Exited += (s, e) =>
        {
            try { job.exitCode = p.ExitCode; } catch (Exception) { job.exitCode = -1; }
            job.exited = true;
        };

        try
        {
            if (!p.Start()) throw new InvalidOperationException("Process.Start devolveu false");
        }
        catch (Exception ex)
        {
            Debug.LogError("[JustDance] Não consegui iniciar " + description + ": " + ex.Message);
            job.lines.Enqueue("JD|error|Não consegui iniciar " + description + ": " + ex.Message);
            job.openStreams = 0;
            job.exited = true;
            return job;
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        lock (Alive) { Alive.Add(job); }
        Hook();
        return job;
    }

    void StreamClosed()
    {
        System.Threading.Interlocked.Decrement(ref openStreams);
    }

    public bool TryReadLine(out string line)
    {
        if (lines.TryDequeue(out line))
        {
            // O PowerShell pode colocar um BOM na frente da primeira linha.
            int i = line.IndexOf("JD|", StringComparison.Ordinal);
            if (i > 0) line = line.Substring(i);
            return true;
        }
        return false;
    }

    public void Kill()
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (Exception) { /* já terminou */ }
    }

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        Application.quitting += KillAll;
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += KillAll;
#endif
    }

    public static void KillAll()
    {
        lock (Alive)
        {
            foreach (var job in Alive) job.Kill();
            Alive.Clear();
        }
    }

    /// <summary>Escapa um argumento para a linha de comando do Windows.</summary>
    public static string Quote(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in s)
        {
            if (c == '\\') { backslashes++; }
            else if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(c);
            }
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
