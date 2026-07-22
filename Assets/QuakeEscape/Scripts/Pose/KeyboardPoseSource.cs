using UnityEngine;
using UnityEngine.InputSystem;

namespace Collapse
{
    public class KeyboardPoseSource : MonoBehaviour, IPoseSource
    {
        public PoseState CurrentPose { get; private set; }
        public bool IsTracking { get; private set; }

        private void Update()
        {
            if (Keyboard.current == null)
            {
                CurrentPose = PoseState.None;
                IsTracking = false;
                return;
            }

            IsTracking = true;

            // Priority: S > A > D
            if (Keyboard.current.sKey.isPressed)
            {
                CurrentPose = PoseState.Tiptoe;
            }
            else if (Keyboard.current.aKey.isPressed)
            {
                CurrentPose = PoseState.OneLegLeft;
            }
            else if (Keyboard.current.dKey.isPressed)
            {
                CurrentPose = PoseState.OneLegRight;
            }
            else
            {
                CurrentPose = PoseState.None;
            }
        }
    }
}
