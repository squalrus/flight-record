using UnityEngine;
using UnityEngine.SceneManagement;

namespace InfinitySpace
{
    public class LoadLevel : MonoBehaviour
    {
        public void LoadScene()
        {
            SceneManager.LoadScene("Main");
        }
    }
}
