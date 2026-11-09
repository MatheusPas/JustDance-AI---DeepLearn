/// <summary>
/// Dados que atravessam as cenas (menu -> jogo).
/// </summary>
public static class GameSession
{
    public static SongEntry SelectedSong;
    public static int PlayerCount = 1;

    /// <summary>Nome da webcam escolhida (vazio = a primeira que o sistema listar).</summary>
    public static string CameraName;

    /// <summary>Dançarino (id do poses.json) que cada jogador segue. Null = jogador i segue o dançarino i.</summary>
    public static int[] DancerOrder;

    /// <summary>True enquanto a tela de escolha de dançarino está aberta (a partida espera).</summary>
    public static bool ChoosingDancers;

    public static int DancerFor(int player)
    {
        return DancerOrder != null && player >= 0 && player < DancerOrder.Length ? DancerOrder[player] : player;
    }
}
