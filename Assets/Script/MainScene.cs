using UnityEngine;
using UnityEngine.SceneManagement;

public class MainScene : MonoBehaviour
{
   public void StartGame() 
   {
        SceneManager.LoadScene("Cuteconomics");
   }
    public void Exit()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

}
