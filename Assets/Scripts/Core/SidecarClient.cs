using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>Resultado da análise de um frame: ângulos e pontos de cada jogador.</summary>
public sealed class PoseFrame
{
    public uint Seq;
    public int TMs;                  // instante da MÚSICA (ms) em que o frame foi capturado
    public float DetectMs;
    public int Count;
    public int[] Ids = new int[PoseProtocol.MaxPlayers];
    public float[][] Angles = new float[PoseProtocol.MaxPlayers][];   // 8 ângulos, NaN = não confiável
    public float[][] Points = new float[PoseProtocol.MaxPlayers][];   // 13 pontos (x,y) em 0..1, origem no canto superior esquerdo
}

/// <summary>Constantes do protocolo (iguais às de PythonTools/sidecar_protocol.py).</summary>
public static class PoseProtocol
{
    public const int MaxPlayers = 8;
    public const int Angles = 8;
    public const int Points = 13;

    public const byte MsgConfig = 1, MsgFrame = 2, MsgBye = 3, MsgPing = 4;
    public const byte MsgReady = 0x81, MsgResult = 0x82, MsgLog = 0x83, MsgPong = 0x84;

    public const int HeaderSize = 8;
    public const int FrameHeadSize = 14;
    public const int ResultHeadSize = 14;
    public const int PlayerSize = 1 + 4 * Angles + 4 * Points * 2;   // 137

    public const byte FlagBottomUp = 1, FlagMirror = 2;
}

/// <summary>
/// Conexão TCP com o analysis_server.py. O envio de frames e a leitura dos
/// resultados acontecem em threads próprias: a Unity nunca espera pela rede.
/// Se o servidor ainda está ocupado com o frame anterior, o frame novo é pulado.
/// </summary>
public sealed class SidecarClient : IDisposable
{
    TcpClient tcp;
    NetworkStream stream;
    Thread reader, sender;
    volatile bool running;

    readonly object resultLock = new object();
    PoseFrame latest;
    uint lastTakenSeq = uint.MaxValue;

    readonly ConcurrentQueue<string> logs = new ConcurrentQueue<string>();
    readonly AutoResetEvent sendSignal = new AutoResetEvent(false);
    byte[] sendBuffer = new byte[0];
    int sendLength;
    volatile bool sendBusy;

    public volatile bool IsReady;
    public volatile bool Closed;

    /// <summary>true se um frame novo pode ser enviado agora (evita copiar pixels à toa).</summary>
    public bool CanSend { get { return running && !sendBusy && !Closed; } }

    public void Connect(int port, int players, string model)
    {
        tcp = new TcpClient { NoDelay = true };
        tcp.Connect("127.0.0.1", port);
        stream = tcp.GetStream();
        running = true;

        string cfg = "{\"players\":" + players + ",\"model\":\"" + model + "\"}";
        byte[] body = Encoding.UTF8.GetBytes(cfg);
        byte[] msg = new byte[PoseProtocol.HeaderSize + body.Length];
        WriteHeader(msg, PoseProtocol.MsgConfig, body.Length);
        Buffer.BlockCopy(body, 0, msg, PoseProtocol.HeaderSize, body.Length);
        stream.Write(msg, 0, msg.Length);

        reader = new Thread(ReadLoop) { IsBackground = true, Name = "pose-reader" };
        sender = new Thread(SendLoop) { IsBackground = true, Name = "pose-sender" };
        reader.Start();
        sender.Start();
    }

    // ------------------------------------------------------------------ envio

    /// <summary>
    /// Entrega um frame da câmera (Color32 = RGBA, linhas de baixo para cima como o
    /// GetPixels32). Devolve false se o envio anterior ainda não terminou (frame pulado).
    /// </summary>
    public bool TrySendFrame(uint seq, int tMs, Color32[] pixels, int width, int height, bool bottomUp, bool mirror)
    {
        if (!running || sendBusy) return false;

        int pixelBytes = width * height * 4;
        int total = PoseProtocol.HeaderSize + PoseProtocol.FrameHeadSize + pixelBytes;
        if (sendBuffer.Length < total) sendBuffer = new byte[total];

        WriteHeader(sendBuffer, PoseProtocol.MsgFrame, PoseProtocol.FrameHeadSize + pixelBytes);
        int o = PoseProtocol.HeaderSize;
        WriteU32(sendBuffer, o, seq);
        WriteU32(sendBuffer, o + 4, (uint)tMs);
        sendBuffer[o + 8] = (byte)(width & 0xFF);
        sendBuffer[o + 9] = (byte)(width >> 8);
        sendBuffer[o + 10] = (byte)(height & 0xFF);
        sendBuffer[o + 11] = (byte)(height >> 8);
        sendBuffer[o + 12] = 4;
        sendBuffer[o + 13] = (byte)((bottomUp ? PoseProtocol.FlagBottomUp : 0) | (mirror ? PoseProtocol.FlagMirror : 0));

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            Marshal.Copy(handle.AddrOfPinnedObject(), sendBuffer, o + PoseProtocol.FrameHeadSize, pixelBytes);
        }
        finally { handle.Free(); }

        sendLength = total;
        sendBusy = true;
        sendSignal.Set();
        return true;
    }

    void SendLoop()
    {
        try
        {
            while (running)
            {
                sendSignal.WaitOne(200);
                if (!running) break;
                if (!sendBusy) continue;
                stream.Write(sendBuffer, 0, sendLength);
                sendBusy = false;
            }
        }
        catch (Exception) { /* conexão encerrada */ }
        finally { sendBusy = false; Closed = true; }
    }

    // ---------------------------------------------------------------- leitura

    void ReadLoop()
    {
        var head = new byte[PoseProtocol.HeaderSize];
        try
        {
            while (running)
            {
                ReadExact(head, PoseProtocol.HeaderSize);
                if (head[0] != (byte)'J' || head[1] != (byte)'D') throw new Exception("cabeçalho inválido");
                byte type = head[2];
                int size = (int)ReadU32(head, 4);
                var payload = new byte[size];
                if (size > 0) ReadExact(payload, size);

                switch (type)
                {
                    case PoseProtocol.MsgReady:
                        IsReady = true;
                        break;
                    case PoseProtocol.MsgResult:
                        var frame = ParseResult(payload);
                        lock (resultLock) { latest = frame; }
                        break;
                    case PoseProtocol.MsgLog:
                        logs.Enqueue(Encoding.UTF8.GetString(payload));
                        break;
                }
            }
        }
        catch (Exception) { /* conexão encerrada */ }
        finally { Closed = true; }
    }

    void ReadExact(byte[] buffer, int count)
    {
        int got = 0;
        while (got < count)
        {
            int k = stream.Read(buffer, got, count - got);
            if (k <= 0) throw new System.IO.EndOfStreamException();
            got += k;
        }
    }

    static PoseFrame ParseResult(byte[] p)
    {
        var f = new PoseFrame
        {
            Seq = ReadU32(p, 0),
            TMs = (int)ReadU32(p, 4),
            DetectMs = BitConverter.ToSingle(p, 8),
            Count = Math.Min((int)p[12], PoseProtocol.MaxPlayers),
        };
        int o = PoseProtocol.ResultHeadSize;
        for (int i = 0; i < f.Count; i++)
        {
            f.Ids[i] = p[o];
            var angles = new float[PoseProtocol.Angles];
            var points = new float[PoseProtocol.Points * 2];
            for (int a = 0; a < angles.Length; a++) angles[a] = BitConverter.ToSingle(p, o + 1 + 4 * a);
            int po = o + 1 + 4 * PoseProtocol.Angles;
            for (int k = 0; k < points.Length; k++) points[k] = BitConverter.ToSingle(p, po + 4 * k);
            f.Angles[i] = angles;
            f.Points[i] = points;
            o += PoseProtocol.PlayerSize;
        }
        return f;
    }

    /// <summary>O resultado mais novo, se ainda não foi retirado; senão null.</summary>
    public PoseFrame TakeLatest()
    {
        lock (resultLock)
        {
            if (latest == null || latest.Seq == lastTakenSeq) return null;
            lastTakenSeq = latest.Seq;
            return latest;
        }
    }

    public bool TryReadLog(out string json) { return logs.TryDequeue(out json); }

    // ---------------------------------------------------------------- utilitários

    static void WriteHeader(byte[] buf, byte type, int size)
    {
        buf[0] = (byte)'J'; buf[1] = (byte)'D'; buf[2] = type; buf[3] = 0;
        WriteU32(buf, 4, (uint)size);
    }

    static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
    }

    static uint ReadU32(byte[] b, int o)
    {
        return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }

    public void Dispose()
    {
        if (!running && tcp == null) return;
        try
        {
            if (running && stream != null)
            {
                var bye = new byte[PoseProtocol.HeaderSize];
                WriteHeader(bye, PoseProtocol.MsgBye, 0);
                stream.Write(bye, 0, bye.Length);
            }
        }
        catch (Exception) { }
        running = false;
        sendSignal.Set();
        try { stream?.Close(); } catch (Exception) { }
        try { tcp?.Close(); } catch (Exception) { }
        tcp = null;
    }
}
