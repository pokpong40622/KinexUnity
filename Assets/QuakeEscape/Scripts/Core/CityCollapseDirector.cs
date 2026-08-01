using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Collapse
{
    // Ambient set dressing: background buildings shudder, keel over and shed debris
    // while the game runs, so the whole city feels like it is coming down.
    // Debris uses the Blender-fractured slab meshes (debrisMeshes) so collapses read
    // as broken concrete rather than tidy cubes; dust clouds are one-shot particles.
    public class CityCollapseDirector : MonoBehaviour
    {
        [SerializeField] private Transform cityRoot;
        [SerializeField] private AudioClip rumbleClip;
        [SerializeField] private AudioClip crashClip;
        [SerializeField] private float minInterval = 6f;
        [SerializeField] private float maxInterval = 12f;
        [SerializeField] private int preCollapsedCount = 3;
        [SerializeField] private float minDistanceFromPlayer = 10f;
        [SerializeField] private Mesh[] debrisMeshes;
        [SerializeField] private Material dustMaterial;
        [SerializeField] private Material lavaMaterial;

        // street surface sits at y=-0.25; the crack mask must hover just above it
        private const float CrackSurfaceY = -0.24f;

        private readonly List<Transform> standing = new List<Transform>();
        private AudioSource source;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.55f;

            if (cityRoot == null)
            {
                return;
            }

            foreach (Transform child in cityRoot)
            {
                // only buildings far enough away that a fall reads as backdrop, not threat
                if (child.name.StartsWith("Bldg") && child.position.magnitude >= minDistanceFromPlayer)
                {
                    standing.Add(child);
                }
            }
        }

        private void Start()
        {
            for (int i = 0; i < preCollapsedCount && standing.Count > 0; i++)
            {
                MakeRubblePile(TakeRandomStanding());
            }

            // a couple of fissures already split the street when the game starts
            for (int i = 0; i < 2; i++)
            {
                var p = new Vector3(
                    (i == 0 ? -1f : 1f) * Random.Range(3.8f, 6f), CrackSurfaceY, Random.Range(3f, 9f));
                Vector2 d2 = Random.insideUnitCircle.normalized;
                GroundCrackFX.Spawn(
                    p, new Vector3(d2.x, 0f, d2.y), Random.Range(6f, 9f),
                    Random.Range(0.45f, 0.7f), lavaMaterial, dustMaterial, debrisMeshes);
            }

            StartCoroutine(DirectorLoop());
        }

        private IEnumerator DirectorLoop()
        {
            while (standing.Count > 0)
            {
                yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));

                var gm = GameManager.Instance;
                if (gm != null && (gm.Phase == GamePhase.Victory || gm.Phase == GamePhase.GameOver))
                {
                    continue;
                }

                yield return CollapseBuilding(TakeRandomStanding());
            }
        }

        private Transform TakeRandomStanding()
        {
            // prefer buildings actually inside the camera frustum so the player
            // sees them come down; fall back to any building left
            var visible = new List<Transform>();
            var cam = Camera.main;
            Plane[] planes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
            foreach (var b in standing)
            {
                bool seen = planes != null && b.TryGetComponent(out MeshRenderer mr)
                    ? GeometryUtility.TestPlanesAABB(planes, mr.bounds)
                    : Mathf.Abs(b.position.x) <= b.position.z * 0.35f + 2f;
                if (seen)
                {
                    visible.Add(b);
                }
            }

            Transform pick;
            if (visible.Count > 0 && cam != null)
            {
                // frontmost first: the buildings filling the screen come down before
                // the skyline, picking among the nearest few so it isn't a fixed order
                visible.Sort((a, b) =>
                    (a.position - cam.transform.position).sqrMagnitude.CompareTo(
                        (b.position - cam.transform.position).sqrMagnitude));
                pick = visible[Random.Range(0, Mathf.Min(3, visible.Count))];
            }
            else
            {
                var pool = visible.Count > 0 ? visible : standing;
                pick = pool[Random.Range(0, pool.Count)];
            }
            standing.Remove(pick);
            return pick;
        }

        // A broken-concrete piece. The fractured meshes are neither unit-sized nor
        // pivot-centred (bounds up to 2.3 wide, centres up to 1.6 off origin), so
        // the mesh is mounted on a child normalised to a centred 1x1x1 — callers
        // scale the parent like a unit cube and pieces land exactly where placed
        // instead of drifting into their neighbours; a default BoxCollider on the
        // parent fits exactly. Falls back to a cube if no meshes are wired.
        private GameObject MakeChunk(Material mat)
        {
            GameObject go;
            MeshRenderer mr;
            if (debrisMeshes != null && debrisMeshes.Length > 0)
            {
                go = new GameObject("Chunk");
                Mesh mesh = debrisMeshes[Random.Range(0, debrisMeshes.Length)];
                var child = new GameObject("Mesh");
                child.transform.SetParent(go.transform, false);
                Vector3 s = mesh.bounds.size;
                var inv = new Vector3(
                    1f / Mathf.Max(s.x, 0.01f),
                    1f / Mathf.Max(s.y, 0.01f),
                    1f / Mathf.Max(s.z, 0.01f));
                child.transform.localScale = inv;
                child.transform.localPosition = -Vector3.Scale(mesh.bounds.center, inv);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                mr = child.AddComponent<MeshRenderer>();
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                mr = go.GetComponent<MeshRenderer>();
            }

            if (mat != null)
            {
                mr.sharedMaterial = mat;
            }
            return go;
        }

        // One-shot rolling dust cloud; destroys itself when it finishes.
        private void SpawnDustCloud(Vector3 pos, float radius, int burst)
        {
            var go = new GameObject("DustCloud");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system playing; stop it before configuring duration
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startColor = new Color(0.32f, 0.29f, 0.27f, 0.5f);
            main.maxParticles = burst;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.35f, 0.31f, 0.29f), 0f),
                    new GradientColorKey(new Color(0.22f, 0.20f, 0.20f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.55f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.4f;
            limit.dampen = 0.6f;

            if (dustMaterial != null)
            {
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial;
            }
            ps.Play();
        }

        // pre-collapsed buildings start the game as a heap of broken slabs
        private void MakeRubblePile(Transform b)
        {
            var renderer = b.GetComponent<MeshRenderer>();
            Material mat = renderer != null ? renderer.sharedMaterial : null;
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds(b.position, Vector3.one * 6f);

            b.gameObject.SetActive(false);

            // one slab per grid cell, stacked in shallow layers thinning toward the
            // top, so the pile reads as settled rubble instead of chunks spawned at
            // random positions interpenetrating each other
            int nx = Mathf.Max(2, Mathf.RoundToInt(bounds.size.x / 1.6f));
            int nz = Mathf.Max(2, Mathf.RoundToInt(bounds.size.z / 1.6f));
            float cellX = bounds.size.x / nx;
            float cellZ = bounds.size.z / nz;
            float maxFoot = Mathf.Min(cellX, cellZ);
            for (int layer = 0; layer < 3; layer++)
            {
                float keep = 1f - layer * 0.3f;
                for (int ix = 0; ix < nx; ix++)
                {
                    for (int iz = 0; iz < nz; iz++)
                    {
                        if (Random.value > keep)
                        {
                            continue;
                        }
                        var chunk = MakeChunk(mat);
                        // footprint stays inside its cell even after a random yaw
                        float foot = maxFoot * Random.Range(0.5f, 0.75f);
                        // z is real thickness now that meshes are normalised
                        chunk.transform.localScale = new Vector3(
                            foot, foot * Random.Range(0.8f, 1.1f), Random.Range(0.15f, 0.35f));
                        chunk.transform.position = new Vector3(
                            bounds.min.x + cellX * (ix + 0.5f) + Random.Range(-0.12f, 0.12f) * cellX,
                            bounds.min.y + 0.2f + layer * 0.45f,
                            bounds.min.z + cellZ * (iz + 0.5f) + Random.Range(-0.12f, 0.12f) * cellZ);
                        // near-flat stacking with scatter, like slabs that pancaked long ago
                        chunk.transform.rotation = Quaternion.Euler(
                            90f + Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
                        chunk.transform.SetParent(b.parent, true);
                    }
                }
            }
        }

        private IEnumerator CollapseBuilding(Transform b)
        {
            if (rumbleClip != null)
            {
                source.PlayOneShot(rumbleClip);
            }
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.015f, 1.1f);
            }

            // pre-shake: the building shudders before letting go
            Quaternion baseRot = b.rotation;
            Vector3 basePos = b.position;
            float t = 0f;
            while (t < 1.1f)
            {
                t += Time.deltaTime;
                float jitter = 0.7f * Mathf.Clamp01(t);
                b.rotation = baseRot * Quaternion.Euler(
                    (Mathf.PerlinNoise(t * 14f, 0f) - 0.5f) * jitter,
                    0f,
                    (Mathf.PerlinNoise(0f, t * 14f) - 0.5f) * jitter);
                yield return null;
            }
            b.rotation = baseRot;

            if (crashClip != null)
            {
                source.PlayOneShot(crashClip);
            }
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.06f, 1.6f);
            }
            b.position = basePos;

            yield return FractureAndCollapse(b);
        }

        // real-life style collapse: the building itself stays intact and sinks
        // into its own footprint (demolition drop), shedding only small fractured
        // debris from the roofline and base while dust rolls out — it never swaps
        // into visible box sections; the lower storeys survive as a stub
        private IEnumerator FractureAndCollapse(Transform b)
        {
            var renderer = b.GetComponent<MeshRenderer>();
            Material mat = renderer != null ? renderer.sharedMaterial : null;
            Bounds world = renderer != null
                ? renderer.bounds
                : new Bounds(b.position + Vector3.up * 8f, new Vector3(4f, 16f, 4f));
            Vector3 size = world.size;
            float baseY = world.min.y;

            // invisible slab to catch debris — background buildings stand over the void
            var ground = new GameObject("CollapseGround");
            ground.transform.position = new Vector3(world.center.x, baseY - 0.55f, world.center.z);
            ground.AddComponent<BoxCollider>().size =
                new Vector3(size.x * 5f, 1f, size.z * 5f);

            // the impact splits the street: a fissure shoots from the building's
            // foot toward the play area
            Vector3 cdir = -new Vector3(world.center.x, 0f, world.center.z).normalized;
            cdir = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * cdir;
            Vector3 cstart = new Vector3(world.center.x, CrackSurfaceY, world.center.z)
                + cdir * (Mathf.Max(size.x, size.z) * 0.6f);
            GroundCrackFX.Spawn(cstart, cdir, Random.Range(7f, 11f),
                Random.Range(0.5f, 0.8f), lavaMaterial, dustMaterial, debrisMeshes);

            // the intact building drops into its own footprint: y-scale compresses
            // with the base pinned, so the roofline accelerates downward like a
            // demolition drop while the facade never breaks into visible sections
            float stubFrac = Random.Range(0.28f, 0.42f);
            float dur = Random.Range(2.4f, 3.2f);
            Vector3 s0 = b.localScale;
            float p0y = b.position.y;
            Quaternion r0 = b.rotation;
            // a slight lean sells structural failure; it stays on the stub after
            Quaternion lean = r0 * Quaternion.Euler(
                Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f));

            var fragments = new List<Rigidbody>();
            float t = 0f;
            float nextSpill = 0f;
            float nextDust = 0f;
            float nextShake = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / dur);
                // gravity-flavoured drop: slow first crack, then it really goes
                float drop = u * u * (3f - 2f * u);
                drop = Mathf.Pow(drop, 1.35f);
                float k = Mathf.Lerp(1f, stubFrac, drop);

                b.localScale = new Vector3(s0.x, s0.y * k, s0.z);
                // keep the base where it was while the pivot follows the shrink
                b.position = new Vector3(
                    b.position.x, baseY + k * (p0y - baseY), b.position.z);
                float wob = 0.45f * drop;
                b.rotation = Quaternion.Slerp(r0, lean, drop) * Quaternion.Euler(
                    (Mathf.PerlinNoise(t * 11f, 3f) - 0.5f) * wob, 0f,
                    (Mathf.PerlinNoise(7f, t * 11f) - 0.5f) * wob);

                float roofY = baseY + size.y * k;
                if (t >= nextSpill && fragments.Count < 70)
                {
                    nextSpill = t + Random.Range(0.08f, 0.16f);
                    // small fragments shear off the descending roofline edge —
                    // only debris ever leaves the building, never whole sections
                    int burst = Random.Range(1, 4);
                    for (int i = 0; i < burst; i++)
                    {
                        SpawnFrag(mat, fragments,
                            EdgePoint(world, roofY - Random.Range(0f, size.y * 0.12f)),
                            OutwardVel(world, 1.2f + 2.2f * drop));
                    }
                }
                if (t >= nextDust)
                {
                    nextDust = t + 0.35f;
                    // dust erupts at the base and boils up around the crush line
                    SpawnDustCloud(new Vector3(world.center.x, baseY + 0.4f, world.center.z),
                        Mathf.Max(size.x, size.z) * (0.55f + 0.35f * drop), 10);
                    SpawnDustCloud(EdgePoint(world, roofY), size.x * 0.3f, 5);
                }
                if (t >= nextShake && CameraShake.Instance != null)
                {
                    nextShake = t + 0.4f;
                    CameraShake.Instance.Shake(0.015f, 0.35f + 0.5f * drop);
                }
                yield return null;
            }

            // the surviving stub keeps the real facade; heap extra debris that
            // sprayed out at the very end of the drop
            b.localScale = new Vector3(s0.x, s0.y * stubFrac, s0.z);
            for (int i = 0; i < 14; i++)
            {
                SpawnFrag(mat, fragments,
                    EdgePoint(world, baseY + size.y * stubFrac + Random.Range(0f, 0.6f)),
                    OutwardVel(world, Random.Range(2f, 4f)));
            }
            SpawnDustCloud(new Vector3(world.center.x, baseY + 0.6f, world.center.z),
                Mathf.Max(size.x, size.z) * 1.1f, 24);
            if (CameraShake.Instance != null)
            {
                CameraShake.Instance.Shake(0.055f, 1.4f);
            }

            // let the debris settle, then freeze it so physics goes idle; the
            // fragments stay behind as rubble around the stub
            yield return new WaitForSeconds(5f);
            foreach (var rb in fragments)
            {
                if (rb != null)
                {
                    rb.isKinematic = true;
                }
            }
            Destroy(ground);
        }

        // random point just outside the building footprint's perimeter at a given
        // height — clear of the facade collider, or depenetration catapults the
        // fragment to infinity and floods the console with invalid-AABB asserts
        private static Vector3 EdgePoint(Bounds world, float y)
        {
            const float push = 0.6f;
            float u = Random.Range(-0.5f, 0.5f);
            bool xSide = Random.value < 0.5f;
            float sign = Random.value < 0.5f ? -1f : 1f;
            return xSide
                ? new Vector3(world.center.x + sign * (world.size.x * 0.5f + push), y,
                    world.center.z + u * world.size.z)
                : new Vector3(world.center.x + u * world.size.x, y,
                    world.center.z + sign * (world.size.z * 0.5f + push));
        }

        private static Vector3 OutwardVel(Bounds world, float speed)
        {
            Vector2 d = Random.insideUnitCircle;
            if (d.sqrMagnitude < 1e-4f)
            {
                d = Vector2.right;
            }
            d.Normalize();
            return new Vector3(d.x, Random.Range(-0.2f, 0.4f), d.y) * speed;
        }

        // one small tumbling fragment of the building's own material
        private void SpawnFrag(Material mat, List<Rigidbody> fragments, Vector3 pos, Vector3 vel)
        {
            var frag = MakeChunk(mat);
            frag.transform.localScale = Vector3.one * Random.Range(0.15f, 0.45f);
            frag.transform.position = pos;
            frag.transform.rotation = Random.rotation;
            frag.AddComponent<BoxCollider>();
            var frb = frag.AddComponent<Rigidbody>();
            frb.mass = 0.6f;
            frb.linearVelocity = vel;
            frb.angularVelocity = Random.insideUnitSphere * 5f;
            fragments.Add(frb);
        }
    }
}
