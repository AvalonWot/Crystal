using System.Drawing;
﻿using Server.MirDatabase;
using Server.MirEnvir;
using S = ServerPackets;


namespace Server.MirObjects.Monsters
{
    public class ZumaMonster : MonsterObject
    {
        public bool Stoned = true;
        public bool AvoidFireWall = true;

        protected override bool CanMove
        {
            get
            {
                return base.CanMove && !Stoned;
            }
        }
        protected override bool CanAttack
        {
            get
            {
                return base.CanAttack && !Stoned;
            }
        }


        protected internal ZumaMonster(MonsterInfo info) : base(info)
        {
        }

        public override int Pushed(MapObject pusher, MirDirection dir, int distance)
        {
            return Stoned ? 0 : base.Pushed(pusher, dir, distance);
        }

        public override void ApplyPoison(Poison p, MapObject Caster = null, bool NoResist = false, bool ignoreDefence = true)
        {
            if (Stoned) return;

            base.ApplyPoison(p, Caster, NoResist, ignoreDefence);
        }
        public override Buff AddBuff(BuffType type, MapObject owner, int duration, Stats stats, bool refreshStats = true, bool updateOnly = false, params int[] values)
        {
            if (Stoned) return null;

            return base.AddBuff(type, owner, duration, stats, refreshStats, updateOnly, values);
        }

        public override bool IsFriendlyTarget(HumanObject ally)
        {
            if (Stoned) return false;

            return base.IsFriendlyTarget(ally);
        }

        protected override void ProcessAI()
        {
            if (!Dead && Envir.Time > ActionTime)
            {
                bool stoned = !FindNearby(2);
                
                if (Stoned && !stoned)
                {
                    Wake();
                    WakeAll(14);
                }
            }

            base.ProcessAI();
        }

        public void Wake()
        {
            if (!Stoned) return;

            Stoned = false;
            Broadcast(new S.ObjectShow { ObjectID = ObjectID });
            ActionTime = Envir.Time + 1000;
        }

        public void WakeAll(int dist)
        {
            for (int y = CurrentLocation.Y - dist; y <= CurrentLocation.Y + dist; y++)
            {
                if (y < 0) continue;
                if (y >= CurrentMap.Height) break;

                for (int x = CurrentLocation.X - dist; x <= CurrentLocation.X + dist; x++)
                {
                    if (x < 0) continue;
                    if (x >= CurrentMap.Width) break;

                    using var cellQuery0 = CurrentMap.RentObjectsSnapshot(x, y);

                    if (!cellQuery0.Valid) continue;

                    for (int i = 0; i < cellQuery0.Count; i++)
                    {
                        ZumaMonster target = cellQuery0[i] as ZumaMonster;
                        if (target == null || !cellQuery0.IsCurrent(target) || !target.Stoned) continue;
                        target.Wake();
                        target.Target = Target;
                    }
                }
            }

        }
        public override bool IsAttackTarget(MonsterObject attacker)
        {
            return !Stoned && base.IsAttackTarget(attacker);
        }
        public override bool IsAttackTarget(HumanObject attacker)
        {
            return !Stoned && base.IsAttackTarget(attacker);
        }

        public override bool Walk(MirDirection dir)
        {
            if (!CanMove) return false;

            Point location = Functions.PointMove(CurrentLocation, dir, 1);

            if (!CurrentMap.ValidPoint(location)) return false;

            using var cellQuery1 = CurrentMap.RentObjectsSnapshot(location);


                for (int i = 0; i < cellQuery1.Count; i++)
                {
                    MapObject ob = cellQuery1[i];
                    if (!cellQuery1.IsCurrent(ob)) continue;
                    if (AvoidFireWall && ob.Race == ObjectType.Spell)
                        if (((SpellObject)ob).Spell == Spell.FireWall) return false;

                    if (!ob.Blocking) continue;

                    return false;
                }


            Direction = dir;
            RemoveObjects(dir, 1);
            CurrentMap.MoveObject(this, location);
            AddObjects(dir, 1);

            if (Hidden)
            {
                RemoveBuff(BuffType.Hiding);
            }

            CellTime = Envir.Time + 500;
            ActionTime = Envir.Time + 300;
            MoveTime = Envir.Time + MoveSpeed;

            if (MoveTime > AttackTime)
                AttackTime = MoveTime;

            InSafeZone = CurrentMap.GetSafeZone(CurrentLocation) != null;

            Broadcast(new S.ObjectWalk { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });

            using var cellQuery2 = CurrentMap.RentObjectsSnapshot(CurrentLocation);

            for (int i = 0; i < cellQuery2.Count; i++)
            {
                MapObject currentCellObject = cellQuery2[i];
                if (!cellQuery2.IsCurrent(currentCellObject)) continue;
                if (currentCellObject.Race != ObjectType.Spell) continue;
                SpellObject ob = (SpellObject)currentCellObject;

                ob.ProcessSpell(this);
                //break;
            }

            return true;
        }

        public override Packet GetInfo()
        {
            var packet = (S.ObjectMonster)base.GetInfo();
            packet.Extra = Stoned;
            return packet;
        }
    }
}
