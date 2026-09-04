using Server.MirDatabase;
using S = ServerPackets;

namespace Server.MirObjects.Monsters
{
    public class BoneFamiliar : MonsterObject
    {
        public bool Summoned;

        protected internal BoneFamiliar(MonsterInfo info) : base(info)
        {
            Direction = MirDirection.DownLeft;
        }
        
        public override void RefreshAll()
        {
            base.RefreshAll();
            Stats[Stat.MinDC] += (int)Math.Floor(master.Stats[Stat.MinSC] * (PetLevel * PetLevel * 0.005f));
            Stats[Stat.MaxDC] += (int)Math.Floor(master.Stats[Stat.MaxSC] * (PetLevel * PetLevel * 0.005f));
            Stats[Stat.Accuracy] += (int)Math.Floor((master.Stats[Stat.MinSC] + master.Stats[Stat.MaxSC]) / 5.0f);
            Stats[Stat.Agility] += (int)Math.Floor((master.Stats[Stat.MinSC] + master.Stats[Stat.MaxSC]) / 10.0f);
        }
        
        public override void Spawned()
        {
            base.Spawned();

            Summoned = true;
        }

        public override Packet GetInfo()
        {
            var packet = (S.ObjectMonster)base.GetInfo();
            packet.Extra = Summoned;
            return packet;
        }
    }
}
