/// <summary>
/// Dados que atravessam as cenas (menu -> jogo).
/// </summary>
public static class GameSession
{
    public static SongEntry SelectedSong;
    public static int PlayerCount = 1;

    /// <summary>Nome da webcam escolhida (vazio = a primeira que o sistema listar).</summary>
    public static string CameraName;
}
