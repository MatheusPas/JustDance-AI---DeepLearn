using UnityEditor;
using UnityEditor.SceneManagement;

// O Play do editor sempre começa pelo menu, qualquer que seja a cena aberta.
// Sem isso, apertar Play com a SampleScene (ou a Game) aberta não inicia o jogo.
[InitializeOnLoad]
static class PlayFromMainMenu
{
    const string MenuScene = "Assets/Scenes/MainMenu.unity";

    static PlayFromMainMenu()
    {
        // delayCall: no primeiro import o AssetDatabase ainda pode não ter a cena carregada.
        EditorApplication.delayCall += () =>
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene);
    }
}
