using UnityEngine;

namespace FlightRecord
{
    public class Part : Collectible
    {
        [Header("Transferrable Parts")]
        [Range(0, 100)]
        public int TransferrableParts;

        protected override void OnCollected(GameObject player)
        {
            player.GetComponent<PlayerController>()?.AddParts(TransferrableParts);
            if (GameControl.control != null) GameControl.control.PartsCollected++;
            Destroy(gameObject);
        }
    }
}
