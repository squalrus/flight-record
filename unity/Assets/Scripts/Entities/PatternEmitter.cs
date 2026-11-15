using FlightRecord.Gameplay;
using UnityEngine;

namespace FlightRecord
{
    // Fires a BulletPattern (backlog 4.13). Add one to any enemy, or several for layered
    // patterns. With AutoFire it fires on the pattern's own timer; Trigger() fires a burst
    // now, which is how the audio-driven generator will fire patterns on beats (4.4), with
    // AutoFire off.
    //
    // On an Enemy it fires only while the enemy is on screen and a player is alive, and the
    // enemy's default single-cannon shot is turned off (Enemy.TryFire). Shots come from
    // EnemyProjectilePool; past its cap they are skipped, so dense patterns thin out
    // instead of dropping the frame rate.
    public class PatternEmitter : MonoBehaviour
    {
        public BulletPattern Pattern;
        [Tooltip("Where shots start. Empty: this object's position.")]
        public Transform Origin;
        [Tooltip("Fire on the pattern's own timer. Off: fire only when Trigger() is called.")]
        public bool AutoFire = true;

        private Enemy _enemy;
        private Ship _ship;
        private PatternClock _clock;
        private BulletPattern _clockPattern;
        private int _volleyIndex;
        private readonly float[] _angles = new float[BulletPatternMath.MaxShotsPerVolley];

        private void Awake()
        {
            _enemy = GetComponent<Enemy>();
            _ship = GetComponent<Ship>();
        }

        private void Update()
        {
            if (!EnsureClock() || Time.deltaTime <= 0f) return;
            _clock.AutoFire = AutoFire;
            Fire(_clock.Advance(Time.deltaTime));
        }

        // Fire one burst of the pattern now (and restart the auto timer).
        public void Trigger()
        {
            if (!EnsureClock()) return;
            _clock.Trigger();
            Fire(_clock.Advance(0f));
        }

        // Built lazily, and rebuilt if the pattern is swapped at runtime.
        private bool EnsureClock()
        {
            if (Pattern == null) return false;
            if (_clock == null || _clockPattern != Pattern)
            {
                _clock = new PatternClock(Pattern.Interval, Pattern.VolleysPerBurst, Pattern.VolleySpacing);
                _clockPattern = Pattern;
            }
            return true;
        }

        private void Fire(int volleys)
        {
            for (var i = 0; i < volleys; i++)
            {
                // The index advances even for volleys not fired (off screen), so a stream's
                // sweep and a ring's spin keep time.
                FireVolley(_volleyIndex++);
            }
        }

        private void FireVolley(int volleyIndex)
        {
            if (Time.timeScale <= 0f || Pattern.Projectile == null) return;
            if (_enemy != null && !_enemy.IsOnScreen) return;

            Vector2 origin = Origin != null ? Origin.position : transform.position;
            var target = PlayerController.NearestTo(origin);
            if (target == null) return;

            float baseAngle;
            switch (Pattern.Aim)
            {
                case PatternAim.NearestPlayer:
                    var toTarget = (Vector2)target.position - origin;
                    baseAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
                    break;
                case PatternAim.Facing:
                    var up = transform.up;
                    baseAngle = Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg;
                    break;
                default:
                    baseAngle = Pattern.Direction;
                    break;
            }

            var count = BulletPatternMath.VolleyAngles(Pattern.Shape, baseAngle, volleyIndex, _angles);
            var carried = Pattern.RideScroll ? Playfield.ScrollVelocity : Vector2.zero;

            for (var i = 0; i < count; i++)
            {
                var radians = _angles[i] * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                var position = origin + direction * Pattern.SpawnRadius;
                // Projectiles fly nose first (local +Y), as Ship.SpawnShot fires them.
                var rotation = Quaternion.Euler(0f, 0f, _angles[i] - 90f);

                var missile = EnemyProjectilePool.Spawn(Pattern.Projectile, position, rotation);
                if (missile == null) return; // at the cap

                missile.SetHealth(_ship != null ? _ship.RollFirePower() : 1);
                missile.IgnoreEnemies();
                if (Pattern.HomingTurnRate > 0f)
                    missile.SetHoming(Pattern.HomingTurnRate, Pattern.HomingDelay, Pattern.HomingRange);
                missile.Body.linearVelocity = direction * Pattern.Speed + carried;
            }
        }
    }
}
