using System.Collections;
using UnityEngine;

namespace Collapse
{
    public enum FloorState
    {
        Solid,
        Warning,
        Collapsed
    }

    public class FloorHalf : MonoBehaviour
    {
        [SerializeField] private Renderer slabRenderer;
        [SerializeField] private Collider slabCollider;
        [SerializeField] private Renderer crackRenderer;
        [SerializeField] private Transform chunksRoot;
        [SerializeField] private float dropDistance = 6f;
        [SerializeField] private float dropTime = 0.6f;
        [SerializeField] private float rebuildTime = 0.8f;
        [SerializeField] private float chunkFallTime = 1.4f;
        [SerializeField] private Color warningPulseColor = new Color(1f, 0.45f, 0.05f, 1f);
        [SerializeField] private float crackGlowIntensity = 4f;
        // real fissure (GroundCrackFX) used instead of the flat decal when wired
        [SerializeField] private Material lavaMaterial;
        [SerializeField] private Material dustMaterial;
        [SerializeField] private Mesh[] crackDebrisMeshes;

        private const float PulseFrequency = 2f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Vector3 originalLocalPosition;
        private Material slabMaterial;
        private Material crackMaterial;

        private Vector3[] chunkPositions;
        private Quaternion[] chunkRotations;

        private Coroutine pulseRoutine;
        private Coroutine moveRoutine;
        private GroundCrackFX crackFX;

        // warning highlight overlay: bright rim along the slab's ragged outline,
        // faint wash over the interior — marks WHICH half is about to break
        private System.Collections.Generic.List<Vector2> slabLoop;
        private Vector2 slabCentroid;
        private GameObject highlightGO;
        private Material highlightMat;

        public FloorState State { get; private set; } = FloorState.Solid;

        private void Awake()
        {
            originalLocalPosition = transform.localPosition;
            BuildJaggedSlab();

            if (slabRenderer != null)
            {
                slabMaterial = slabRenderer.material;
            }

            if (crackRenderer != null)
            {
                crackMaterial = crackRenderer.material;
                crackRenderer.enabled = false;
            }

            if (chunksRoot != null)
            {
                int n = chunksRoot.childCount;
                chunkPositions = new Vector3[n];
                chunkRotations = new Quaternion[n];
                for (int i = 0; i < n; i++)
                {
                    var c = chunksRoot.GetChild(i);
                    chunkPositions[i] = c.localPosition;
                    chunkRotations[i] = c.localRotation;
                }
                chunksRoot.gameObject.SetActive(false);
            }
        }

        public void BeginWarning()
        {
            State = FloorState.Warning;

            StopPulse();
            SpawnCrackFX();
            ShowHighlight();
            pulseRoutine = StartCoroutine(PulseRoutine());
        }

        // additive overlay riding just above the slab top, built from the same
        // jagged perimeter loop as the slab mesh: rim vertices at full alpha
        // (strong edge), pulled-in ring + centroid faint — so the doomed half
        // reads as "edge burns, interior glows softly" during the warning
        private void ShowHighlight()
        {
            if (highlightGO == null)
            {
                BuildHighlight();
            }
            if (highlightGO != null)
            {
                highlightGO.SetActive(true);
            }
        }

        private void HideHighlight()
        {
            if (highlightGO != null)
            {
                highlightGO.SetActive(false);
            }
        }

        private void BuildHighlight()
        {
            if (slabLoop == null || slabLoop.Count < 3)
            {
                return;
            }

            // same additive ember material the crack FX borrows; dust as fallback
            Material src = null;
            var embersRef = GameObject.Find("Embers");
            if (embersRef != null && embersRef.TryGetComponent(out ParticleSystemRenderer psr))
            {
                src = psr.sharedMaterial;
            }
            if (src == null)
            {
                src = dustMaterial;
            }
            if (src == null)
            {
                return;
            }

            var go = new GameObject("WarnHighlight");
            go.transform.SetParent(transform, false);

            float sx = transform.localScale.x;
            float sz = transform.localScale.z;
            float y = 0.5f + 0.05f; // ~15mm world above the slab top (scale y 0.3)

            int count = slabLoop.Count;
            var verts = new Vector3[count * 2 + 1];
            var cols = new Color32[count * 2 + 1];
            for (int i = 0; i < count; i++)
            {
                Vector2 p = slabLoop[i];
                verts[i] = new Vector3(p.x, y, p.y);
                cols[i] = new Color32(255, 255, 255, 255);

                // inner ring pulled ~0.35 m (world) toward the centroid
                Vector2 d = slabCentroid - p;
                float worldLen = new Vector2(d.x * sx, d.y * sz).magnitude;
                float t = worldLen > 0.001f ? Mathf.Min(0.85f, 0.35f / worldLen) : 0f;
                Vector2 q = p + d * t;
                verts[count + i] = new Vector3(q.x, y, q.y);
                cols[count + i] = new Color32(255, 255, 255, 38);
            }
            verts[count * 2] = new Vector3(slabCentroid.x, y, slabCentroid.y);
            cols[count * 2] = new Color32(255, 255, 255, 22);

            // loop is already clockwise-in-xz (BuildJaggedSlab enforced it), so
            // (a, i, j) order faces up — same rule as the slab's top fan
            var tris = new System.Collections.Generic.List<int>(count * 9);
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                tris.Add(count + i); tris.Add(i); tris.Add(j);
                tris.Add(count + i); tris.Add(j); tris.Add(count + j);
                tris.Add(count * 2); tris.Add(count + i); tris.Add(count + j);
            }

            var mesh = new Mesh { name = gameObject.name + "_WarnHighlight" };
            mesh.vertices = verts;
            mesh.colors32 = cols;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            highlightMat = new Material(src) { mainTexture = null };
            var c0 = warningPulseColor;
            c0.a = 0f;
            highlightMat.color = c0;
            mr.sharedMaterial = highlightMat;

            highlightGO = go;
            go.SetActive(false);
        }

        // a real fissure tears down the middle of this half — the video-style
        // "lift your leg" signal; falls back to the flat decal when unwired
        private void SpawnCrackFX()
        {
            if (lavaMaterial == null)
            {
                return;
            }
            DismissCrackFX();
            // runs the half's full depth, hugging the half's INNER edge so the
            // ground tears open right beside the player's feet (player stays
            // centred at x=0); origin + wander keep it >=0.2m off the centre
            // line so it never cuts through the character
            float zHalf = 2.3f;
            float side = Mathf.Sign(transform.position.x);
            var origin = new Vector3(
                side * Random.Range(0.5f, 0.7f), 0f, transform.position.z - zHalf);
            var dir = Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f) * Vector3.forward;
            crackFX = GroundCrackFX.Spawn(
                origin, dir, zHalf * 2f, 0.24f, lavaMaterial, dustMaterial,
                crackDebrisMeshes, branch: false, wanderLimit: 0.3f, avoidPlayArea: false);
        }

        private void DismissCrackFX()
        {
            if (crackFX != null)
            {
                Destroy(crackFX.gameObject);
                crackFX = null;
            }
        }

        public void Collapse()
        {
            StopPulse();
            SetCrackVisible(false);
            HideHighlight();
            // the floor breaks along the fissure — the crack is consumed by it
            DismissCrackFX();

            State = FloorState.Collapsed;

            StopMove();
            if (chunksRoot != null)
            {
                moveRoutine = StartCoroutine(ChunkCollapseRoutine());
            }
            else
            {
                moveRoutine = StartCoroutine(SlabDropRoutine());
            }
        }

        public void Rebuild()
        {
            StopPulse();
            SetCrackVisible(false);
            HideHighlight();
            DismissCrackFX();

            State = FloorState.Solid;

            if (slabCollider != null)
            {
                slabCollider.enabled = true;
            }

            if (chunksRoot != null)
            {
                chunksRoot.gameObject.SetActive(false);
                ResetChunks();
            }

            StopMove();
            moveRoutine = StartCoroutine(RebuildRoutine());
        }

        private void StopPulse()
        {
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }
        }

        private void StopMove()
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
        }

        private void SetCrackVisible(bool visible)
        {
            if (crackRenderer != null)
            {
                crackRenderer.enabled = visible;
            }
        }

        private void ResetChunks()
        {
            if (chunksRoot == null)
            {
                return;
            }

            for (int i = 0; i < chunksRoot.childCount; i++)
            {
                var c = chunksRoot.GetChild(i);
                c.localPosition = chunkPositions[i];
                c.localRotation = chunkRotations[i];
            }
        }

        // both halves sample the same Perlin seam so their jagged inner edges
        // interlock exactly while solid; when one half drops, the survivor
        // shows a ragged broken edge instead of a straight cube cut
        private static float SeamProfile(float worldZ)
        {
            float bend = (Mathf.PerlinNoise(worldZ * 0.35f + 7.31f, 3.7f) - 0.5f) * 0.34f;
            float teeth = (Mathf.PerlinNoise(worldZ * 1.6f + 11.7f, 8.2f) - 0.5f) * 0.26f;
            float jitter = (Mathf.PerlinNoise(worldZ * 6.3f + 3.1f, 1.9f) - 0.5f) * 0.06f;
            return Mathf.Clamp(bend + teeth + jitter, -0.3f, 0.3f);
        }

        // inward bite depth (metres) sampled over perimeter arc length so the jags
        // flow continuously around corners; the |2n-1| term folds the noise into
        // sharp cusps so the edge reads as fracture teeth, not a soft wave. Bite
        // only goes INTO the slab, keeping the visual inside the straight
        // BoxCollider footprint (same accepted ledge trade-off as the seam)
        private static float EdgeBite(float arc, float seed)
        {
            float big = Mathf.PerlinNoise(arc * 0.9f + seed, seed * 0.37f + 4.7f);
            float cusp = Mathf.Abs(Mathf.PerlinNoise(arc * 2.6f + seed * 1.7f, seed + 8.9f) * 2f - 1f);
            return 0.08f + big * 0.18f + cusp * 0.2f;
        }

        // replaces the unit-cube slab mesh with one whose whole outline is ragged:
        // the seam-side edge follows SeamProfile exactly (both halves interlock),
        // while the outer edge and both ends get an inward EdgeBite windowed to
        // zero at the corners so the loop stays closed. Built in local space so
        // the (3, 0.3, 4) transform scale and the BoxCollider stay untouched.
        private void BuildJaggedSlab()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null)
            {
                return;
            }

            bool isLeft = transform.position.x < 0f;
            float centerX = transform.position.x;
            float sx = transform.localScale.x;
            float sz = transform.localScale.z;
            float outerX = isLeft ? -0.5f : 0.5f;
            float outSign = Mathf.Sign(outerX);

            int n = Mathf.Max(2, Mathf.CeilToInt(sz / 0.22f) + 1);
            int m = Mathf.Max(3, Mathf.CeilToInt(sx / 0.22f) + 1);

            // base perimeter in local (x, z), walked seam -> far end -> outer ->
            // near end, each station tagged with its inward bite direction; the
            // outer corners get a diagonal bite so no corner stays square. Bite
            // tapers to zero over 0.5 m approaching the seam so the two halves
            // still meet flush at the junction points.
            var basePts = new System.Collections.Generic.List<Vector2>();
            var biteDirs = new System.Collections.Generic.List<Vector2>();
            var tapers = new System.Collections.Generic.List<float>();

            var innerX = new float[n];
            var zs = new float[n];
            for (int i = 0; i < n; i++)
            {
                zs[i] = -0.5f + i / (float)(n - 1);
                float worldZ = transform.position.z + zs[i] * sz;
                innerX[i] = (SeamProfile(worldZ) - centerX) / sx;
                basePts.Add(new Vector2(innerX[i], zs[i]));
                biteDirs.Add(Vector2.zero);
                tapers.Add(0f);
            }

            // far end (z = +0.5): seam corner -> outer corner (corner included)
            for (int j = 1; j < m; j++)
            {
                float tj = j / (float)(m - 1);
                float x = Mathf.Lerp(innerX[n - 1], outerX, tj);
                basePts.Add(new Vector2(x, 0.5f));
                biteDirs.Add(j == m - 1
                    ? new Vector2(-outSign, -1f).normalized
                    : new Vector2(0f, -1f));
                tapers.Add(Mathf.Clamp01(Mathf.Abs(x - innerX[n - 1]) * sx / 0.5f));
            }

            // outer edge: z just below +0.5 down to just above -0.5
            for (int i = n - 2; i >= 1; i--)
            {
                basePts.Add(new Vector2(outerX, zs[i]));
                biteDirs.Add(new Vector2(-outSign, 0f));
                tapers.Add(1f);
            }

            // near end (z = -0.5): outer corner (included) -> back toward the seam
            for (int j = m - 1; j >= 1; j--)
            {
                float tj = j / (float)(m - 1);
                float x = Mathf.Lerp(innerX[0], outerX, tj);
                basePts.Add(new Vector2(x, -0.5f));
                biteDirs.Add(j == m - 1
                    ? new Vector2(-outSign, 1f).normalized
                    : new Vector2(0f, 1f));
                tapers.Add(Mathf.Clamp01(Mathf.Abs(x - innerX[0]) * sx / 0.5f));
            }

            // apply the bites along perimeter arc length (world metres) so the
            // fracture pattern is continuous around the corners
            float seedHalf = isLeft ? 21.4f : 63.8f;
            var loop = new System.Collections.Generic.List<Vector2>();
            float arc = 0f;
            for (int i = 0; i < basePts.Count; i++)
            {
                if (i > 0)
                {
                    Vector2 d = basePts[i] - basePts[i - 1];
                    arc += new Vector2(d.x * sx, d.y * sz).magnitude;
                }
                float bite = EdgeBite(arc, seedHalf) * tapers[i];
                loop.Add(basePts[i] + new Vector2(
                    biteDirs[i].x * bite / sx, biteDirs[i].y * bite / sz));
            }

            // enforce clockwise-in-xz order (negative shoelace) so the fixed
            // winding rules below always face top up / walls out
            float shoelace = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i];
                Vector2 b = loop[(i + 1) % loop.Count];
                shoelace += a.x * b.y - b.x * a.y;
            }
            if (shoelace > 0f)
            {
                loop.Reverse();
            }

            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();

            System.Func<Vector3, int> addVert = p =>
            {
                verts.Add(p);
                uvs.Add(new Vector2(p.x + p.z + 0.5f, p.y + p.z + 0.5f));
                return verts.Count - 1;
            };

            int count = loop.Count;
            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                centroid += loop[i];
            }
            centroid /= count;

            // top + bottom fans (bites are small, so the loop is star-shaped
            // around the centroid)
            int topC = addVert(new Vector3(centroid.x, 0.5f, centroid.y));
            int botC = addVert(new Vector3(centroid.x, -0.5f, centroid.y));
            var topIdx = new int[count];
            var botIdx = new int[count];
            for (int i = 0; i < count; i++)
            {
                topIdx[i] = addVert(new Vector3(loop[i].x, 0.5f, loop[i].y));
                botIdx[i] = addVert(new Vector3(loop[i].x, -0.5f, loop[i].y));
            }
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                tris.Add(topC); tris.Add(topIdx[i]); tris.Add(topIdx[j]);
                tris.Add(botC); tris.Add(botIdx[j]); tris.Add(botIdx[i]);
                // side wall on its own vertices so the flat top/bottom normals
                // aren't averaged with the wall; reversed-loop order faces out
                int wti = addVert(new Vector3(loop[i].x, 0.5f, loop[i].y));
                int wtj = addVert(new Vector3(loop[j].x, 0.5f, loop[j].y));
                int wbi = addVert(new Vector3(loop[i].x, -0.5f, loop[i].y));
                int wbj = addVert(new Vector3(loop[j].x, -0.5f, loop[j].y));
                tris.Add(wti); tris.Add(wbi); tris.Add(wbj);
                tris.Add(wti); tris.Add(wbj); tris.Add(wtj);
            }

            slabLoop = loop;
            slabCentroid = centroid;

            var mesh = new Mesh { name = gameObject.name + "_JaggedSlab" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.mesh = mesh;
        }

        private IEnumerator PulseRoutine()
        {
            // fissure mode: the decal stays hidden, the lava seam does the pulsing
            SetCrackVisible(crackFX == null);

            float t = 0f;
            while (true)
            {
                t += Time.deltaTime;
                float phase = (Mathf.Sin(t * PulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
                float ramp = Mathf.Clamp01(t * 0.6f);
                float glow = Mathf.Lerp(0.25f, 1f, phase) * ramp;

                if (highlightMat != null)
                {
                    // additive: rgb drives brightness, alpha the overall weight;
                    // the edge/interior contrast lives in the mesh vertex alpha
                    Color hc = warningPulseColor * (0.35f + glow * 0.9f);
                    hc.a = 0.3f + glow * 0.7f;
                    highlightMat.color = hc;
                }

                if (crackFX != null)
                {
                    crackFX.SetGlow(0.35f + glow * 1.8f);
                }
                else if (crackMaterial != null)
                {
                    Color c = warningPulseColor * (glow * crackGlowIntensity);
                    c.a = Mathf.Clamp01(0.4f + glow * 0.6f);
                    crackMaterial.SetColor(BaseColorId, c);
                }
                else if (slabMaterial != null)
                {
                    slabMaterial.EnableKeyword("_EMISSION");
                    slabMaterial.SetColor(EmissionColorId, Color.Lerp(Color.black, warningPulseColor, glow));
                }

                yield return null;
            }
        }

        private IEnumerator ChunkCollapseRoutine()
        {
            if (slabRenderer != null)
            {
                slabRenderer.enabled = false;
            }
            if (slabCollider != null)
            {
                slabCollider.enabled = false;
            }

            chunksRoot.gameObject.SetActive(true);

            int n = chunksRoot.childCount;
            var delays = new float[n];
            var spins = new Vector3[n];
            var drifts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                delays[i] = Random.Range(0f, 0.3f);
                spins[i] = new Vector3(Random.Range(-140f, 140f), Random.Range(-60f, 60f), Random.Range(-140f, 140f));
                var c = chunksRoot.GetChild(i);
                Vector3 outward = c.localPosition - Vector3.zero;
                drifts[i] = new Vector3(outward.x, 0f, outward.z).normalized * Random.Range(0.2f, 0.8f);
            }

            float t = 0f;
            while (t < chunkFallTime)
            {
                t += Time.deltaTime;
                for (int i = 0; i < n; i++)
                {
                    float ct = Mathf.Max(0f, t - delays[i]);
                    if (ct <= 0f)
                    {
                        continue;
                    }
                    var c = chunksRoot.GetChild(i);
                    c.localPosition = chunkPositions[i]
                        + Vector3.down * (9.81f * 0.5f * ct * ct)
                        + drifts[i] * ct;
                    c.localRotation = chunkRotations[i] * Quaternion.Euler(spins[i] * ct);
                }
                yield return null;
            }

            chunksRoot.gameObject.SetActive(false);
            moveRoutine = null;
        }

        private IEnumerator SlabDropRoutine()
        {
            Vector3 start = transform.localPosition;
            Vector3 end = start + Vector3.down * dropDistance;

            if (slabCollider != null)
            {
                slabCollider.enabled = false;
            }

            float t = 0f;
            while (t < dropTime)
            {
                t += Time.deltaTime;
                float normalized = Mathf.Clamp01(t / dropTime);
                float eased = normalized * normalized;
                transform.localPosition = Vector3.Lerp(start, end, eased);
                yield return null;
            }

            transform.localPosition = end;

            if (slabRenderer != null)
            {
                slabRenderer.enabled = false;
            }

            moveRoutine = null;
        }

        private IEnumerator RebuildRoutine()
        {
            // chunk mode: slab never moved, so just fade it back in place
            if (chunksRoot != null)
            {
                transform.localPosition = originalLocalPosition;
                if (slabRenderer != null)
                {
                    slabRenderer.enabled = true;
                }
                // rise from slightly below for readability
                Vector3 from = originalLocalPosition + Vector3.down * 1.2f;
                float rt = 0f;
                transform.localPosition = from;
                while (rt < rebuildTime)
                {
                    rt += Time.deltaTime;
                    float k = Mathf.Clamp01(rt / rebuildTime);
                    k = 1f - (1f - k) * (1f - k);
                    transform.localPosition = Vector3.Lerp(from, originalLocalPosition, k);
                    yield return null;
                }
                transform.localPosition = originalLocalPosition;
                moveRoutine = null;
                yield break;
            }

            Vector3 start = transform.localPosition;
            Vector3 end = originalLocalPosition;

            if (slabRenderer != null)
            {
                slabRenderer.enabled = true;
            }

            float t = 0f;
            while (t < rebuildTime)
            {
                t += Time.deltaTime;
                float normalized = Mathf.Clamp01(t / rebuildTime);
                transform.localPosition = Vector3.Lerp(start, end, normalized);
                yield return null;
            }

            transform.localPosition = end;
            moveRoutine = null;
        }
    }
}
