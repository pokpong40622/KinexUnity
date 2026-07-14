using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// One gate: an arch with a single open lane approaches over ApproachSeconds; resolved at
    /// arrival by whether the avatar's current screen lane matches the gate's target lane.
    /// Never blocks — a miss just ghosts the arch and the avatar passes through anyway.
    /// </summary>
    public class GateBeatRunner : IQuestBeatRunner
    {
        public const float ApproachSeconds = 4f;

        QuestContext _ctx;
        QuestBeat _beat;
        Transform _gate;
        float _t;
        bool _resolved;
        float _score;

        public bool Done => false;
        public float Score01 => _score;

        public void Begin(QuestContext ctx, QuestBeat beat)
        {
            _ctx = ctx;
            _beat = beat;
            _t = 0f;
            _resolved = false;
            _score = BeatScoring.GateFailScore; // partial credit even if the beat is cut short

            float z = ctx.trail.SpawnZ(ApproachSeconds);
            _gate = ctx.props.SpawnGate(beat.lane, z);
            ctx.trail.Attach(_gate);

            ctx.setCue?.Invoke(ArrowGlyph(beat.lane), CueText(beat.lane));
            ctx.voiceLine?.Invoke(CueText(beat.lane) + "!");
        }

        public void Tick(float dt)
        {
            _t += dt;
            if (_resolved || _t < ApproachSeconds) return;

            _resolved = true;
            bool pass = _ctx.CurrentScreenLane == _beat.lane;
            _score = BeatScoring.Gate(pass);

            if (pass)
            {
                KinexFx.SparkleTrail(_gate);
                _ctx.addCoin?.Invoke();
                _ctx.addCoin?.Invoke(); // gate pass pays 2 coins
            }
            else
            {
                _ctx.props.Ghost(_gate);
                _ctx.setCue?.Invoke(ArrowGlyph(_beat.lane), "ไม่เป็นไร ครั้งหน้าลองใหม่นะ");
            }
        }

        static string ArrowGlyph(int lane) => lane < 0 ? "<-" : lane > 0 ? "->" : "|";
        static string CueText(int lane) => lane < 0 ? "ก้าวไปทางซ้าย" : lane > 0 ? "ก้าวไปทางขวา" : "ยืนตรงกลาง";
    }
}
