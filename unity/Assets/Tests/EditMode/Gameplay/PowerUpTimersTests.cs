using FlightRecord.Gameplay;
using NUnit.Framework;

namespace FlightRecord.Gameplay.Tests
{
    public class PowerUpTimersTests
    {
        [Test]
        public void NothingIsActiveAtStart()
        {
            var timers = new PowerUpTimers();
            foreach (var type in PowerUpTimers.AllTypes)
            {
                Assert.IsFalse(timers.IsActive(type, 0f));
                Assert.AreEqual(0f, timers.Remaining(type, 0f));
            }
            Assert.IsFalse(timers.AnyActive(0f));
        }

        [Test]
        public void ActiveUntilDurationThenExpires()
        {
            var timers = new PowerUpTimers();
            timers.Activate(PowerUpType.Spread, 10f, 5f);

            Assert.IsTrue(timers.IsActive(PowerUpType.Spread, 5f));
            Assert.IsTrue(timers.IsActive(PowerUpType.Spread, 14.9f));
            Assert.AreEqual(3f, timers.Remaining(PowerUpType.Spread, 12f), 1e-4f);
            Assert.IsFalse(timers.IsActive(PowerUpType.Spread, 15f));
            Assert.AreEqual(0f, timers.Remaining(PowerUpType.Spread, 20f));
        }

        [Test]
        public void DifferentTypesStackWithSeparateTimers()
        {
            var timers = new PowerUpTimers();
            timers.Activate(PowerUpType.Spread, 10f, 0f);
            timers.Activate(PowerUpType.RapidFire, 4f, 2f);

            Assert.IsTrue(timers.IsActive(PowerUpType.Spread, 3f));
            Assert.IsTrue(timers.IsActive(PowerUpType.RapidFire, 3f));
            Assert.IsFalse(timers.IsActive(PowerUpType.Homing, 3f));

            Assert.IsTrue(timers.IsActive(PowerUpType.Spread, 7f));
            Assert.IsFalse(timers.IsActive(PowerUpType.RapidFire, 7f));
            Assert.IsTrue(timers.AnyActive(7f));
            Assert.IsFalse(timers.AnyActive(10f));
        }

        [Test]
        public void SameTypeAgainResetsTimerInsteadOfAdding()
        {
            var timers = new PowerUpTimers();
            timers.Activate(PowerUpType.Homing, 10f, 0f);
            timers.Activate(PowerUpType.Homing, 10f, 8f);

            Assert.AreEqual(10f, timers.Remaining(PowerUpType.Homing, 8f), 1e-4f);
            Assert.IsTrue(timers.IsActive(PowerUpType.Homing, 17.9f));
            Assert.IsFalse(timers.IsActive(PowerUpType.Homing, 18f));
        }

        [Test]
        public void ClearEndsEverything()
        {
            var timers = new PowerUpTimers();
            timers.Activate(PowerUpType.Spread, 10f, 0f);
            timers.Activate(PowerUpType.Homing, 10f, 0f);
            timers.Clear();

            Assert.IsFalse(timers.AnyActive(1f));
        }

        [Test]
        public void NegativeDurationNeverActivates()
        {
            var timers = new PowerUpTimers();
            timers.Activate(PowerUpType.RapidFire, -5f, 1f);

            Assert.IsFalse(timers.IsActive(PowerUpType.RapidFire, 1f));
        }
    }
}
