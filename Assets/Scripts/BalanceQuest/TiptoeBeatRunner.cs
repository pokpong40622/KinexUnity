using UnityEngine;
using Kinex.FX;

namespace Kinex.BalanceQuest
{
    /// <summary>
    /// 3 hanging fruits arrive at 4s intervals across a 15s window. JustRaised while a fruit is
    /// overhead grabs it (pop + coin). Score = grabbed/3.
    /// </summary>
    public class TiptoeBeatRunner : IQuestBeatRunner
    {
        const float SpawnIntervalSeconds = 4f;
        const float TravelSeconds = 2.5f;
        const float OverheadWindowSeconds = 1f;
        const int FruitCount = BeatScoring.TiptoeFruitCount;

        QuestContext _ctx;
        readonly Transform[] _fruit = new Transform[FruitCount];
        readonly float[] _arriveAt = new float[FruitCount];
        readonly bool[] _spawned = new bool[FruitCount];
        readonly bool[] _grabbed = new bool[FruitCount];
        readonly bool[] _missed = new bool[FruitCount];
        float _clock;
        float _score;

        public bool Done => false;
        public float Score01 => _score;

        public void Begin(QuestContext ctx, QuestBeat beat)
        {
            _ctx = ctx;
            _clock = 0f;
            _score = 0f;
            for (int i = 0; i < FruitCount; i++)
            {
                _fruit[i] = null;
                _spawned[i] = false;
                _grabbed[i] = false;
                _missed[i] = false;
                _arriveAt[i] = i * SpawnIntervalSeconds + TravelSeconds;
            }
            ctx.setCue?.Invoke("up", "เขย่งเท้า!");
            ctx.voiceLine?.Invoke("เขย่งเท้าหยิบผลไม้!");
        }

        public void Tick(float dt)
        {
            _clock += dt;
            for (int i = 0; i < FruitCount; i++)
            {
                if (!_spawned[i] && _clock >= i * SpawnIntervalSeconds)
                {
                    _spawned[i] = true;
                    float z = _ctx.trail.SpawnZ(TravelSeconds);
                    _fruit[i] = _ctx.props.SpawnHangingFruit(i % 2 == 0, z);
                    _ctx.trail.Attach(_fruit[i]);
                }

                if (_spawned[i] && !_grabbed[i] && !_missed[i] && _fruit[i] != null)
                {
                    bool overhead = Mathf.Abs(_clock - _arriveAt[i]) <= OverheadWindowSeconds;
                    if (overhead && _ctx.JustRaised)
                    {
                        _grabbed[i] = true;
                        KinexFx.PopBurst(_fruit[i].position, new Color(1f, 0.9f, 0.3f));
                        _ctx.addCoin?.Invoke();
                        _ctx.trail.Detach(_fruit[i]);
                        _ctx.props.Despawn(_fruit[i]);
                        _fruit[i] = null;
                    }
                    else if (_clock - _arriveAt[i] > OverheadWindowSeconds)
                    {
                        _missed[i] = true;
                        _ctx.trail.Detach(_fruit[i]);
                        _ctx.props.Despawn(_fruit[i]);
                        _fruit[i] = null;
                    }
                }
            }

            int grabbedCount = 0;
            for (int i = 0; i < FruitCount; i++) if (_grabbed[i]) grabbedCount++;
            _score = BeatScoring.Tiptoe(grabbedCount);
        }
    }
}
