using UnityEngine;

namespace Collapse
{
    /// Opening camera flourish for the EMBEDDED build. When the gameplay scene loads (right after
    /// the menu's PLAY), the camera starts in FRONT of the player — looking at the character's face,
    /// the way the friend's menu "face cam" did — and then ORBITS around to the normal gameplay
    /// framing (behind the player) over the countdown. This reproduces the friend's face-cam →
    /// play-cam rotation without URP camera stacking or an additive live-background load, neither of
    /// which survives being embedded under Flutter (a single, offscreen render target).
    ///
    /// The avatar faces +Z, so its face is on the +Z side and the gameplay camera sits behind at −Z.
    /// The pan therefore sweeps a half-circle around the character. The camera's AUTHORED transform
    /// is the pan's END (the exact gameplay pose); the START is derived from the player's head so it
    /// needs no hand-authoring.
    [RequireComponent(typeof(Camera))]
    public class IntroCameraPan : MonoBehaviour
    {
        [Tooltip("Player head to frame in the opening close-up. If empty, the player's skinned-mesh " +
                 "bounds are used to find the head automatically.")]
        public Transform playerHead;

        [Tooltip("Seconds the face→gameplay orbit takes. Keep <= the countdown so it finishes first.")]
        public float panDuration = 2.6f;

        [Tooltip("How far (metres) in front of the face the opening shot sits. Far enough back to " +
                 "read as a portrait of the character rather than looming in their face.")]
        public float closeUpDistance = 2.6f;

        [Tooltip("Degrees to swing the opening shot off dead-centre, for a three-quarter view. A " +
                 "perfectly head-on close-up reads as a mugshot / horror stare.")]
        public float closeUpYawDegrees = 32f;

        [Tooltip("Metres above the head the opening shot sits. The camera looks back DOWN at the " +
                 "face from here, which is the tilt that keeps the framing friendly.")]
        public float closeUpHeightOffset = 0.55f;

        [Tooltip("Fallback head height (metres) if no skinned mesh is found.")]
        public float headHeight = 1.28f;

        [Tooltip("If TRUE the orbit runs the moment the scene loads. If FALSE the camera HOLDS on the " +
                 "face (the menu background) until Play() is called — used by the in-scene menu so PLAY " +
                 "triggers the face→play-cam rotation.")]
        public bool autoPlay = true;

        // End (authored gameplay pose).
        Vector3 _endPos;
        Quaternion _endRot;
        // Orbit parameters (all around the player's vertical axis at _pivotXZ).
        Vector2 _pivotXZ;
        float _startAngle, _endAngle;
        float _startRadius, _endRadius;
        float _startHeight, _endHeight;
        Vector3 _lookLow;   // where the gameplay camera looks (eased into near the end)
        Vector3 _headWorld;

        float _t;
        bool _running;
        bool _active;
        CameraShake _shake;

        void Start()
        {
            _endPos = transform.position;
            _endRot = transform.rotation;

            // CameraShake re-pins the camera to its base pose every LateUpdate, which would fight
            // this pan. Take ownership: disable it while we hold the face / run the orbit, and hand
            // the camera back when the orbit lands exactly on the (unchanged) gameplay pose.
            _shake = GetComponent<CameraShake>();
            if (_shake != null) _shake.enabled = false;

            _headWorld = ResolveHead();
            _pivotXZ = new Vector2(_headWorld.x, _headWorld.z);

            // End: derive angle/radius/height of the authored pose around the pivot.
            Vector2 endDir = new Vector2(_endPos.x - _pivotXZ.x, _endPos.z - _pivotXZ.y);
            _endRadius = Mathf.Max(endDir.magnitude, 0.1f);
            _endAngle = Mathf.Atan2(endDir.x, endDir.y); // angle from +Z
            _endHeight = _endPos.y;

            // Start: in FRONT of the character (the side the face points to), swung off-centre and
            // raised above the head so the shot is an angled, slightly-downward portrait rather
            // than a head-on close-up pressed against the face.
            Vector3 fwd = ResolveForward();
            _startAngle = Mathf.Atan2(fwd.x, fwd.z) + closeUpYawDegrees * Mathf.Deg2Rad;
            // Sweep the SHORT way but a full half-turn: keep the delta near ±PI without wrapping oddly.
            while (_endAngle - _startAngle > Mathf.PI) _startAngle += 2f * Mathf.PI;
            while (_endAngle - _startAngle < -Mathf.PI) _startAngle -= 2f * Mathf.PI;
            _startRadius = closeUpDistance;
            _startHeight = _headWorld.y + closeUpHeightOffset;

            // The gameplay camera looks slightly below the head; ease the look-target down to match.
            _lookLow = new Vector3(_headWorld.x, _headWorld.y - 0.16f, _headWorld.z);

            ApplyOrbit(0f);   // sit on the face immediately (the menu background)
            _t = 0f;
            _active = true;
            _running = autoPlay;
        }

        /// <summary>
        /// True while this component still owns the camera — either holding the opening portrait or
        /// mid-orbit. Goes false the frame the camera lands on the gameplay pose. GameManager waits
        /// on this so the framing popup only appears once the move has finished, instead of covering
        /// the shot the player is meant to be watching.
        /// </summary>
        public bool IsPanning => _active;

        /// <summary>Start the face→gameplay orbit (called by the menu's PLAY button).</summary>
        public void Play()
        {
            _t = 0f;
            _running = true;
        }

        /// <summary>
        /// Finds the PLAYER's head. This must resolve against the player specifically: the scene
        /// also holds the pose-detection rig, a second copy of the same avatar parked ~96 m below
        /// the floor so it never renders. A blind FindFirstObjectByType&lt;SkinnedMeshRenderer&gt;
        /// returned THAT rig, which put the orbit pivot 96 m underground and is why the opening
        /// shot showed no face and the PLAY rotation looked like it did nothing at all.
        /// </summary>
        Vector3 ResolveHead()
        {
            if (playerHead != null) return playerHead.position;

            var player = Object.FindFirstObjectByType<PlayerCharacter>();
            if (player != null)
            {
                // Humanoid head bone is exact when the rig is one.
                var anim = player.GetComponentInChildren<Animator>();
                if (anim != null && anim.avatar != null && anim.avatar.isHuman)
                {
                    var head = anim.GetBoneTransform(HumanBodyBones.Head);
                    if (head != null) return head.position;
                }

                // Otherwise take the top of the player's OWN meshes.
                var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skins.Length > 0)
                {
                    var b = skins[0].bounds;
                    for (int i = 1; i < skins.Length; i++) b.Encapsulate(skins[i].bounds);
                    return new Vector3(b.center.x, b.max.y - 0.06f, b.center.z);
                }

                return player.transform.position + Vector3.up * headHeight;
            }

            return new Vector3(0f, headHeight, 0f);
        }

        Vector3 ResolveForward()
        {
            // The character's facing = opposite the direction from the character to the gameplay cam.
            // (Robust even if the avatar transform's forward isn't authored.) Fall back to +Z.
            Vector3 toCam = _endPos - new Vector3(_pivotXZ.x, _endPos.y, _pivotXZ.y);
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.01f) return Vector3.forward;
            return (-toCam).normalized; // face points away from the camera
        }

        void ApplyOrbit(float e)
        {
            float ang = Mathf.Lerp(_startAngle, _endAngle, e);
            float rad = Mathf.Lerp(_startRadius, _endRadius, e);
            float h = Mathf.Lerp(_startHeight, _endHeight, e);
            Vector3 pos = new Vector3(_pivotXZ.x + Mathf.Sin(ang) * rad, h, _pivotXZ.y + Mathf.Cos(ang) * rad);
            transform.position = pos;

            Vector3 lookTarget = Vector3.Lerp(_headWorld, _lookLow, e);
            Quaternion lookRot = Quaternion.LookRotation((lookTarget - pos).normalized, Vector3.up);
            // Ease onto the EXACT authored rotation over the final stretch so gameplay starts pixel-right.
            float endBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, e));
            transform.rotation = Quaternion.Slerp(lookRot, _endRot, endBlend);
        }

        void LateUpdate()
        {
            if (!_active) return;

            if (_running)
            {
                _t += Time.deltaTime / Mathf.Max(0.01f, panDuration);
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
                ApplyOrbit(e);
                if (_t >= 1f)
                {
                    transform.position = _endPos;
                    transform.rotation = _endRot;
                    _active = false;
                    if (_shake != null) _shake.enabled = true;   // hand the camera back for gameplay shake
                }
            }
            else
            {
                ApplyOrbit(0f);   // hold on the face (the menu background) until Play()
            }
        }
    }
}
