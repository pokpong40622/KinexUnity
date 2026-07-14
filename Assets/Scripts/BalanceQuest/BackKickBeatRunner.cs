using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// 4 low glowing pads arrive alternating behind-left/behind-right, 5s window each. Mirrors
    /// KickBeatRunner exactly but reads HipExtensionDetector's back-kick flags (hip extension —
    /// leg swings straight back) instead of LegAbductionDetector's sideways ones.
    /// </summary>
    public class BackKickBeatRunner : IQuestBeatRunner
    {
        const float WindowSeconds = 5f;
        const float TravelSeconds = 2f;
        const int TargetCount = BeatScoring.BackKickTargetCount;

        QuestContext _ctx;
        int _side, _index, _hits;
        float _windowClock;
        Transform _current;
        bool _resolved;
        float _score;

        public bool Done => false;
        public float Score01 => _score;

        public void Begin(QuestContext ctx, QuestBeat beat)
        {
            _ctx = ctx;
            _index = 0;
            _hits = 0;
            _score = 0f;
            SpawnNext();
            ctx.setCue?.Invoke("backkick", "เตะขาไปด้านหลัง!");
            ctx.voiceLine?.Invoke("เตะขาไปด้านหลังสลับซ้ายขวา!");
        }

        void SpawnNext()
        {
            _side = (_index % 2 == 0) ? -1 : 1;
            _windowClock = 0f;
            _resolved = false;
            float z = _ctx.trail.SpawnZ(TravelSeconds);
            _current = _ctx.props.SpawnBackKickPad(_side, z);
            _ctx.trail.Attach(_current);
        }

        public void Tick(float dt)
        {
            if (_index >= TargetCount) { _score = BeatScoring.BackKick(_hits); return; }

            _windowClock += dt;
            bool kicked = _side < 0 ? _ctx.JustKickedBackLeft : _ctx.JustKickedBackRight;

            if (!_resolved && kicked)
            {
                _resolved = true;
                _hits++;
                KinexFx.PopBurst(_current.position, new Color(0.6f, 0.3f, 0.95f));
                _ctx.addCoin?.Invoke();
                _ctx.trail.Detach(_current);
                _ctx.props.Despawn(_current);
                _current = null;
                Advance();
            }
            else if (!_resolved && _windowClock >= WindowSeconds)
            {
                _resolved = true;
                if (_current != null) { _ctx.trail.Detach(_current); _ctx.props.Despawn(_current); _current = null; }
                Advance();
            }

            _score = BeatScoring.BackKick(_hits);
        }

        void Advance()
        {
            _index++;
            if (_index < TargetCount) SpawnNext();
        }
    }
}
