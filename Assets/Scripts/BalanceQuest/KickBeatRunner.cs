using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// 4 KickTargets arrive alternating sides, each with a 5s window. JustKickedLeft/Right on the
    /// matching side scores a bullseye hit. Score = hits/4.
    /// </summary>
    public class KickBeatRunner : IQuestBeatRunner
    {
        const float WindowSeconds = 5f;
        const float TravelSeconds = 2f;
        const int TargetCount = BeatScoring.KickTargetCount;

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
            ctx.setCue?.Invoke("side", "เตะด้านข้าง!");
            ctx.voiceLine?.Invoke("เตะด้านข้างสลับซ้ายขวา!");
        }

        void SpawnNext()
        {
            _side = (_index % 2 == 0) ? -1 : 1;
            _windowClock = 0f;
            _resolved = false;
            float z = _ctx.trail.SpawnZ(TravelSeconds);
            _current = _ctx.props.SpawnKickTarget(_side, z);
            _ctx.trail.Attach(_current);
        }

        public void Tick(float dt)
        {
            if (_index >= TargetCount) { _score = BeatScoring.Kick(_hits); return; }

            _windowClock += dt;
            bool kicked = _side < 0 ? _ctx.JustKickedLeft : _ctx.JustKickedRight;

            if (!_resolved && kicked)
            {
                _resolved = true;
                _hits++;
                KinexFx.PopBurst(_current.position, new Color(0.95f, 0.2f, 0.2f));
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

            _score = BeatScoring.Kick(_hits);
        }

        void Advance()
        {
            _index++;
            if (_index < TargetCount) SpawnNext();
        }
    }
}
