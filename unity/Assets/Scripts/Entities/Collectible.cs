using UnityEngine;

namespace FlightRecord
{
    // Lets Player.FixedUpdate's magnet loop pull any collectible toward the player without
    // knowing whether it's a Part or a Relic.
    public interface IMagnetAttractable
    {
        bool IsCollectable { get; }
        void Reel(Vector2 target);
    }

    // Shared by Part and Relic: bloom-delay before collection/reeling (so a kill at
    // point-blank range doesn't insta-collect everything before it scatters), the pulse
    // glow visual, and the magnet's direct flight-to-player motion. Subclasses only need
    // to say what happens on pickup (OnCollected) and, optionally, what color to shimmer.
    public abstract class Collectible : DestroyableObject, IMagnetAttractable
    {
        [Header("Bloom Delay")]
        public float BloomDelay = 0.35f;

        [Header("Pulse Glow")]
        public float PulseSpeed = 3f;
        public float PulseScaleAmount = 0.15f;
        [Range(0, 1)] public float PulseMinAlpha = 0.7f;

        [Header("Magnet Reel")]
        public float ReelStartSpeed = 2f;
        public float ReelAcceleration = 40f;
        public float ReelMaxSpeed = 18f;

        private SpriteRenderer _spriteRenderer;
        private TextMesh _textMesh;
        private Rigidbody2D _rigidbody;
        private Vector3 _baseScale;
        private Color _baseColor;
        private float _spawnTime;
        private float _reelStartTime = -1f;

        public bool IsCollectable => Time.time - _spawnTime >= BloomDelay;

        protected virtual void Start()
        {
            SetHealth(MaxHealth);

            _rigidbody = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _textMesh = GetComponentInChildren<TextMesh>();

            _baseScale = transform.localScale;
            _baseColor = _spriteRenderer != null ? _spriteRenderer.color
                : _textMesh != null ? _textMesh.color
                : Color.white;

            _spawnTime = Time.time;
        }

        protected virtual void Update()
        {
            float pulse = (Mathf.Sin(Time.time * PulseSpeed) + 1f) * 0.5f;

            transform.localScale = _baseScale * (1f + PulseScaleAmount * pulse);

            var shimmer = GetShimmerColor(pulse);
            float alpha = Mathf.Lerp(PulseMinAlpha, 1f, pulse) * _baseColor.a;
            var color = new Color(shimmer.r, shimmer.g, shimmer.b, alpha);

            if (_spriteRenderer != null) _spriteRenderer.color = color;
            else if (_textMesh != null) _textMesh.color = color;
        }

        // Default look: pulse alpha only, no hue shift. Relic overrides this for its
        // rainbow shimmer.
        protected virtual Color GetShimmerColor(float pulse) => _baseColor;

        // Called by Player.FixedUpdate once per magnetized collectible per physics step.
        // Ramps speed up over time since reeling started rather than snapping to max
        // speed, and drives the Rigidbody2D directly instead of AddForce so the pull
        // reads as a deliberate flight path rather than a physics nudge.
        public void Reel(Vector2 target)
        {
            if (_rigidbody == null) return;

            if (_reelStartTime < 0f) _reelStartTime = Time.time;
            var elapsed = Time.time - _reelStartTime;
            var speed = Mathf.Min(ReelStartSpeed + ReelAcceleration * elapsed, ReelMaxSpeed);

            var direction = (target - _rigidbody.position).normalized;
            _rigidbody.linearVelocity = direction * speed;
        }

        private void OnCollisionEnter2D(Collision2D collider)
        {
            TryCollect(collider.gameObject);
        }

        // Fires once a magnetized collectible (collider switched to a trigger in
        // Player.FixedUpdate) reaches the player, since trigger colliders don't raise
        // OnCollisionEnter2D.
        private void OnTriggerEnter2D(Collider2D collider)
        {
            TryCollect(collider.gameObject);
        }

        private void TryCollect(GameObject other)
        {
            if (other.name != "Player" || !IsCollectable) return;

            OnCollected(other);
        }

        // Safety net for Player.FixedUpdate: normal pickup relies on OnCollisionEnter2D/
        // OnTriggerEnter2D, which only fires once on first contact and never again while the
        // overlap continues uninterrupted. A pickup whose collider ends up fully inside the
        // player's own collider on that first contact (e.g. still within bloom delay, or just
        // wedged there by physics) never gets a second chance to fire the event and can stay
        // stuck indefinitely. Bypasses IsCollectable since full geometric containment inside
        // the player is unambiguous regardless of bloom delay.
        public void ForceCollect(GameObject player)
        {
            OnCollected(player);
        }

        protected abstract void OnCollected(GameObject player);
    }
}
