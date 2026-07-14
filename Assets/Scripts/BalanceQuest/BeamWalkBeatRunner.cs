using UnityEngine;
using Kinex.Motion;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Marching-in-place tandem-walk simulation on a long glowing beam: 10 alternating knee-raise
    /// "steps" while staying centered (LaneDetector.Lane == 0). A dedicated KneeRaiseDetector
    /// instance — separate from the shared per-frame detectors — counts steps; "centered" is read
    /// through QuestContext.Lane, exactly like every other lane check in the game. Lane 0 is the
    /// same value in both detector-space and screen-space, so no mirroring conversion is needed
    /// here (only the LEFT/RIGHT sign flips, not the CENTER test).
    /// </summary>
    public class BeamWalkBeatRunner : IQuestBeatRunner
    {
        const int TargetSteps = BeatScoring.BeamWalkStepTarget;

        readonly KneeRaiseDetector _kneeRaise = new KneeRaiseDetector();
        QuestContext _ctx;
        bool _baselineSet;
        float _centeredSeconds, _totalSeconds;
        float _score;

        public bool Done => false;
        public float Score01 => _score;

        public void Begin(QuestContext ctx, QuestBeat beat)
        {
            _ctx = ctx;
            _baselineSet = false;
            _centeredSeconds = 0f;
            _totalSeconds = 0f;
            _score = 0f;

            ctx.props.SpawnBridge(8f); // long plank reused as the beam
            ctx.setCue?.Invoke("march", "เดินต่อเท้าบนคาน ยกเข่าเบา ๆ");
            ctx.voiceLine?.Invoke("เดินต่อเท้าบนคาน ยกเข่าเบา ๆ นะครับ");
        }

        public void Tick(float dt)
        {
            _totalSeconds += dt;
            bool centered = _ctx.Lane == 0;
            if (centered) _centeredSeconds += dt;

            int steps;
            if (_ctx.useKeyboardStub)
            {
                steps = Mathf.Min(_ctx.AlternatingCount, TargetSteps);
            }
            else
            {
                var kp = _ctx.Keypoints;
                var conf = _ctx.Confidence;
                if (kp != null)
                {
                    if (!_baselineSet) { _kneeRaise.SetBaseline(kp); _baselineSet = true; }
                    _kneeRaise.Tick(kp, conf, dt);
                }
                steps = Mathf.Min(_kneeRaise.AlternatingCount, TargetSteps);
            }

            float centeredFrac = _totalSeconds > 0f ? _centeredSeconds / _totalSeconds : 0f;
            _score = BeatScoring.BeamWalk(steps, centeredFrac);
        }
    }
}
