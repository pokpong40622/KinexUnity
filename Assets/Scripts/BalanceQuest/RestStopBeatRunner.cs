using UnityEngine;
using Kinex.Motion;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// Two-part strength stop at the rest bench: 5 chair stands (first 45s), then 6 alternating
    /// SEATED knee raises (remaining 30s). Uses SitStandDetector.EstimateSeatedFromStanding() so
    /// the player isn't asked to sit still for a second calibration mid-route — a fresh standing
    /// baseline is captured right as this beat begins (the player just walked/checkpointed, so
    /// they're already standing), and "seated" is derived 0.55*torso0 below it. A sturdy chair
    /// placed behind the player before the session starts (see the Intro/Calib safety copy)
    /// doubles as a physical safety catch for the whole balance game, not just this beat.
    /// </summary>
    public class RestStopBeatRunner : IQuestBeatRunner
    {
        const float StandPartSeconds = 45f;

        readonly SitStandDetector _sitStand = new SitStandDetector();
        readonly KneeRaiseDetector _kneeRaise = new KneeRaiseDetector();

        QuestContext _ctx;
        float _clock;
        bool _part2BaselineSet;
        float _score;

        public bool Done => false;
        public float Score01 => _score;

        public void Begin(QuestContext ctx, QuestBeat beat)
        {
            _ctx = ctx;
            _clock = 0f;
            _part2BaselineSet = false;
            _score = 0f;

            if (!ctx.useKeyboardStub)
            {
                var kp = ctx.Keypoints;
                if (kp != null)
                {
                    _sitStand.CalibrateStanding(kp);
                    _sitStand.EstimateSeatedFromStanding();
                }
            }

            ctx.setCue?.Invoke("chair", "นั่ง-ลุก 5 ครั้ง!");
            ctx.voiceLine?.Invoke("จุดพัก! มีเก้าอี้ด้านหลัง นั่งลงเลย นั่ง-ลุก 5 ครั้งนะครับ");
        }

        public void Tick(float dt)
        {
            _clock += dt;

            if (_ctx.useKeyboardStub)
            {
                int stands = Mathf.Min(_ctx.StandCount, BeatScoring.RestStopStandTarget);
                int raises = Mathf.Min(_ctx.AlternatingCount, BeatScoring.RestStopRaiseTarget);
                _score = BeatScoring.RestStop(stands, raises);
                return;
            }

            var kp = _ctx.Keypoints;
            var conf = _ctx.Confidence;
            if (kp != null)
            {
                if (_clock < StandPartSeconds)
                {
                    _sitStand.Tick(kp, conf, dt);
                }
                else
                {
                    if (!_part2BaselineSet) { _kneeRaise.SetBaseline(kp); _part2BaselineSet = true; }
                    _kneeRaise.Tick(kp, conf, dt);
                }
            }

            int standsDone = Mathf.Min(_sitStand.StandCount, BeatScoring.RestStopStandTarget);
            int raisesDone = Mathf.Min(_kneeRaise.AlternatingCount, BeatScoring.RestStopRaiseTarget);
            _score = BeatScoring.RestStop(standsDone, raisesDone);
        }
    }
}
