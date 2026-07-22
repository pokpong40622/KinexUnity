using UnityEngine;

namespace Collapse
{
    /// <summary>
    /// Selects between a primary and fallback IPoseSource. The primary wins as long as it is
    /// tracking and (when it's a MediaPipePoseSource) has a captured baseline; otherwise the
    /// fallback is used.
    /// </summary>
    public class PoseSourceMux : MonoBehaviour, IPoseSource
    {
        [SerializeField] private MonoBehaviour primarySource;
        [SerializeField] private MonoBehaviour fallbackSource;

        private IPoseSource primary;
        private IPoseSource fallback;

        private void Awake()
        {
            primary = primarySource as IPoseSource;
            if (primarySource != null && primary == null)
            {
                Debug.LogError($"[PoseSourceMux] '{primarySource.name}' does not implement IPoseSource.", this);
            }

            fallback = fallbackSource as IPoseSource;
            if (fallbackSource != null && fallback == null)
            {
                Debug.LogError($"[PoseSourceMux] '{fallbackSource.name}' does not implement IPoseSource.", this);
            }
        }

        private bool PrimaryUsable =>
            primary != null &&
            primary.IsTracking &&
            (primary as MediaPipePoseSource)?.HasBaseline != false;

        public PoseState CurrentPose
        {
            get
            {
                if (PrimaryUsable)
                {
                    return primary.CurrentPose;
                }
                return fallback != null ? fallback.CurrentPose : PoseState.None;
            }
        }

        public bool IsTracking =>
            (primary != null && primary.IsTracking) || (fallback != null && fallback.IsTracking);
    }
}
