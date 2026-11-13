using UnityEngine;

namespace InfinitySpace
{
    [RequireComponent(typeof(ParticleSystem))]
    public class ExplosionEffect : MonoBehaviour
    {
        private ParticleSystem _ps;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();

            // A freshly AddComponent<ParticleSystem>()'d system defaults to playOnAwake = true
            // and starts playing synchronously as soon as it's added -- before this Awake() runs
            // to configure it. Modifying main.duration (or other playback-affecting fields) while
            // playing throws, so stop it first.
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 0.8f),
                new Color(1f, 0.5f, 0.1f)
            );
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 50;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 25, 35) });

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.2f;

            var colorOverLifetime = _ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white,                   0f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.05f),  0.35f),
                    new GradientColorKey(new Color(0.4f, 0.05f, 0f),   1f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f,  0f),
                    new GradientAlphaKey(0.9f, 0.4f),
                    new GradientAlphaKey(0f,  1f),
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = _ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f)
            );
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
        }

        private void Start()
        {
            _ps.Play();
        }
    }
}
