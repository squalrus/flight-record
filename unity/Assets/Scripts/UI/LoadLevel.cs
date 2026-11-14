using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlightRecord
{
    public class LoadLevel : MonoBehaviour
    {
        public void LoadScene()
        {
            SceneManager.LoadScene("Main");
        }
    }
}
