using System.Collections;
using UnityEngine;

namespace Kinex.FruitGame
{
    /// <summary>
    /// Lifts the PlayerCharacter ROOT with the sit→stand progress so the avatar visibly rises
    /// when the player stands (the MediaPipe detector drives the limbs; this drives the root).
    /// Also nudges the root forward briefly on a header hit so the "heading" reads.
    /// Only this component writes the root's local Y/Z — the detector's hip-sway writes the
    /// Hips BONE, not the root, so there is no fight.
    /// </summary>
    public class AvatarRootLift : MonoBehaviour
    {
        [Tooltip("Meters the root rises between fully seated (0) and fully standing (1).")]
        public float standLift = 0.35f;
        [Tooltip("Lerp speed toward the target height. Higher = snappier rise.")]
        public float lerpSpeed = 8f;
        [Tooltip("Meters of the quick forward head-nudge on a header hit (toward the zone, +Z).")]
        public float nudgeDistance = 0.18f;

        float _progress;
        float _zOffset;
        Vector3 _baseLocal;
        Coroutine _nudge;

        void Awake() => _baseLocal = transform.localPosition;

        /// <summary>0 = seated, 1 = standing. Fed each frame from the active detector/stub.</summary>
        public void SetProgress01(float p) => _progress = Mathf.Clamp01(p);

        /// <summary>Quick forward-and-back nudge — call when the avatar heads an item.</summary>
        public void HeadNudge()
        {
            if (_nudge != null) StopCoroutine(_nudge);
            _nudge = StartCoroutine(NudgeRoutine());
        }

        void Update()
        {
            var lp = transform.localPosition;
            float goalY = _baseLocal.y + _progress * standLift;
            lp.y = Mathf.Lerp(lp.y, goalY, Time.deltaTime * lerpSpeed);
            lp.z = _baseLocal.z + _zOffset;
            transform.localPosition = lp;
        }

        IEnumerator NudgeRoutine()
        {
            const float upTime = 0.12f, downTime = 0.2f;
            float t = 0f;
            while (t < upTime)
            {
                t += Time.deltaTime;
                _zOffset = Mathf.SmoothStep(0f, nudgeDistance, t / upTime);
                yield return null;
            }
            t = 0f;
            while (t < downTime)
            {
                t += Time.deltaTime;
                _zOffset = Mathf.SmoothStep(nudgeDistance, 0f, t / downTime);
                yield return null;
            }
            _zOffset = 0f;
            _nudge = null;
        }
    }
}
