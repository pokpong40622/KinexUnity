using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Tweens the avatar root's local X between the 3 lanes (screen-space: -1 left, 0 center,
    /// +1 right) as the player steps. Reads QuestContext.CurrentScreenLane, which already applies
    /// the selfie-mirror sign decision documented on IQuestBeatRunner.cs.
    /// </summary>
    public class AvatarLaneMover : MonoBehaviour
    {
        [SerializeField] float tweenSeconds = 0.22f;

        int _lastScreenLane = int.MinValue;

        public void Tick(QuestContext ctx)
        {
            if (ctx == null) return;
            int screenLane = ctx.CurrentScreenLane;
            if (screenLane == _lastScreenLane) return;
            _lastScreenLane = screenLane;
            var target = new Vector3(screenLane * TrailScroller.LaneWidth, transform.localPosition.y, transform.localPosition.z);
            SimpleTween.MoveTo(transform, target, tweenSeconds, this);
        }

        public void SnapToCenter()
        {
            _lastScreenLane = 0;
            transform.localPosition = new Vector3(0f, transform.localPosition.y, transform.localPosition.z);
        }
    }
}
