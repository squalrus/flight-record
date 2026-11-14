using UnityEngine;

namespace FlightRecord
{
    public class DamageNumber : MonoBehaviour
    {
        [SerializeField] private TextMesh _textMesh;

        public float DriftSpeed = 1.5f;
        public float FadeDuration = 0.5f;

        private float _elapsed;
        private Color _startColor;

        public void Initialize(int amount, Color color)
        {
            _textMesh.text = amount.ToString();
            _textMesh.color = color;
            _startColor = color;
        }

        private void Update()
        {
            transform.position += Vector3.up * DriftSpeed * Time.deltaTime;

            _elapsed += Time.deltaTime;
            var alpha = Mathf.Lerp(_startColor.a, 0f, _elapsed / FadeDuration);
            _textMesh.color = new Color(_startColor.r, _startColor.g, _startColor.b, alpha);

            if (_elapsed >= FadeDuration)
            {
                Destroy(gameObject);
            }
        }
    }
}
