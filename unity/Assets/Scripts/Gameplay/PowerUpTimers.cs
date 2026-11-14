using System;

namespace FlightRecord.Gameplay
{
    // Timed weapon power-ups (backlog 4.9, D-014). Kept engine-free so the stacking and
    // expiry rules can be unit-tested in EditMode without a scene.
    public enum PowerUpType
    {
        Spread = 0,
        RapidFire = 1,
        Homing = 2,
    }

    // One expiry time per power-up type. Different types stack, each with its own timer;
    // picking up a type that is already running resets its timer to the full duration
    // (it does not add the durations together). Times are whatever clock the caller uses
    // (Player passes Time.time, which stops while the game is paused).
    public sealed class PowerUpTimers
    {
        public static readonly PowerUpType[] AllTypes = (PowerUpType[])Enum.GetValues(typeof(PowerUpType));

        private readonly float[] _expiresAt = new float[AllTypes.Length];

        public PowerUpTimers()
        {
            Clear();
        }

        public void Activate(PowerUpType type, float duration, float now)
        {
            _expiresAt[(int)type] = now + Math.Max(0f, duration);
        }

        public bool IsActive(PowerUpType type, float now) => now < _expiresAt[(int)type];

        public float Remaining(PowerUpType type, float now) => Math.Max(0f, _expiresAt[(int)type] - now);

        public bool AnyActive(float now)
        {
            foreach (var type in AllTypes)
                if (IsActive(type, now)) return true;
            return false;
        }

        public void Clear()
        {
            for (var i = 0; i < _expiresAt.Length; i++) _expiresAt[i] = float.NegativeInfinity;
        }
    }
}
