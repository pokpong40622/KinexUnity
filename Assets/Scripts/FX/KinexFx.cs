using UnityEngine;

namespace Kinex.FX
{
    /// <summary>
    /// Code-configured ParticleSystems for game-feel "juice" — no particle assets, built and
    /// cached at runtime the same way RingGauge builds its sprites and CorrectEffect builds
    /// its backdrop. Shared by both mini-games.
    /// </summary>
    public static class KinexFx
    {
        static Material s_ParticleMaterial;

        static Material ParticleMaterial()
        {
            if (s_ParticleMaterial != null) return s_ParticleMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            s_ParticleMaterial = new Material(shader);
            return s_ParticleMaterial;
        }

        static ParticleSystem NewParticleSystem(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static ParticleSystem.MinMaxGradient MultiColorGradient(params Color[] colors)
        {
            var gradient = new Gradient();
            var colorKeys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                colorKeys[i] = new GradientColorKey(colors[i], colors.Length > 1 ? i / (float)(colors.Length - 1) : 0f);
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>One-shot multi-colour confetti burst with gravity. Destroys its GameObject once finished.</summary>
        public static void ConfettiBurst(Vector3 pos, int count = 60)
        {
            var ps = NewParticleSystem("ConfettiBurst", pos);

            var main = ps.main;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = MultiColorGradient(
                new Color(0.95f, 0.25f, 0.25f), new Color(0.25f, 0.55f, 0.95f),
                new Color(0.98f, 0.85f, 0.15f), new Color(0.35f, 0.9f, 0.4f), new Color(0.8f, 0.35f, 0.9f));
            main.gravityModifier = 1f;
            main.maxParticles = count * 2;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-360f, 360f);

            ps.Play();
            Object.Destroy(ps.gameObject, main.duration + main.startLifetime.constantMax + 0.5f);
        }

        /// <summary>One-shot single-colour radial sparkle burst. Destroys its GameObject once finished.</summary>
        public static void PopBurst(Vector3 pos, Color color, int count = 24)
        {
            var ps = NewParticleSystem("PopBurst", pos);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = color;
            main.gravityModifier = 0f;
            main.maxParticles = count * 2;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.02f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fade;

            ps.Play();
            Object.Destroy(ps.gameObject, main.duration + main.startLifetime.constantMax + 0.5f);
        }

        /// <summary>Looping gentle trail parented to a moving transform. Caller Stop()s / destroys it when done.</summary>
        public static ParticleSystem SparkleTrail(Transform parent)
        {
            var ps = NewParticleSystem("SparkleTrail", parent.position);
            ps.transform.SetParent(parent, false);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new Color(1f, 0.95f, 0.6f, 0.9f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 12f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            ps.Play();
            return ps;
        }

        /// <summary>Looping slow-drifting glowing motes inside a box volume — ambient scene dressing.</summary>
        public static ParticleSystem AmbientMotes(Vector3 center, Vector3 boxSize, Color color, int rate = 6)
        {
            var ps = NewParticleSystem("AmbientMotes", center);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = color;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.2f;

            ps.Play();
            return ps;
        }

        /// <summary>Looping stretched streaks rushing past along <paramref name="direction"/> — a
        /// motion cue for a scrolling track, meant to sit just outside the gameplay-readable
        /// area (screen edges). Caller toggles <c>ps.Emission.enabled</c> to turn the rush on/off
        /// (e.g. only while the track is actually scrolling).</summary>
        public static ParticleSystem SpeedStreaks(Vector3 center, Vector3 boxSize, Vector3 direction, float speed, Color color)
        {
            var ps = NewParticleSystem("SpeedStreaks", center);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.1f;
            renderer.lengthScale = 3f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = 0f; // velocity comes from velocityOverLifetime below, not radial startSpeed
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = new Color(color.r, color.g, color.b, 0.45f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;

            var dir = direction.normalized * speed;
            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = new ParticleSystem.MinMaxCurve(dir.x);
            vol.y = new ParticleSystem.MinMaxCurve(dir.y);
            vol.z = new ParticleSystem.MinMaxCurve(dir.z);

            ps.Play();
            return ps;
        }
    }
}
