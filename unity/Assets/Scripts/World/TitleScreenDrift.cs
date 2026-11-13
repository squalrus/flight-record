using UnityEngine;

namespace InfinitySpace
{
    // Attach to the Start scene's Main Camera. Drifting the camera is enough to animate
    // every ParallaxBackground layer at once, since each layer follows the camera and
    // derives its scroll offset from the camera's travel distance.
    public class TitleScreenDrift : MonoBehaviour
    {
        [SerializeField] float _driftSpeed = 0.5f;
        [SerializeField] float _bobAmplitude = 0.3f;
        [SerializeField] float _bobFrequency = 0.15f;

        private Vector3 _basePosition;
        private Vector2 _driftDirection;

        private void Start()
        {
            _basePosition = transform.position;
            _driftDirection = Random.insideUnitCircle.normalized;
        }

        private void Update()
        {
            _basePosition += (Vector3)_driftDirection * _driftSpeed * Time.deltaTime;

            float bobX = Mathf.Sin(Time.time * _bobFrequency * Mathf.PI * 2f) * _bobAmplitude;
            float bobY = Mathf.Cos(Time.time * _bobFrequency * Mathf.PI * 2f * 0.7f) * _bobAmplitude * 0.6f;

            transform.position = _basePosition + new Vector3(bobX, bobY, 0f);
        }
    }
}
