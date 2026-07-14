namespace Kinex.BattleGame
{
    /// <summary>Which procedural silhouette a monster uses — see MonsterMeshBuilder.</summary>
    public enum MonsterShape { RoundSlime, TallGhost, SpikyBoss }

    /// <summary>Plain data for one monster in a level's fight sequence.</summary>
    public readonly struct MonsterDef
    {
        public readonly string nameThai;
        public readonly int maxHp;
        public readonly MonsterShape shape;
        public readonly bool isBoss;
        public readonly float telegraphSeconds;

        public MonsterDef(string nameThai, int maxHp, MonsterShape shape, bool isBoss, float telegraphSeconds)
        {
            this.nameThai = nameThai;
            this.maxHp = maxHp;
            this.shape = shape;
            this.isBoss = isBoss;
            this.telegraphSeconds = telegraphSeconds;
        }
    }
}
