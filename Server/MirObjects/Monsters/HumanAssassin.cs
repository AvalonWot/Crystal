using System.Drawing;
﻿using Server.MirDatabase;
using Server.MirEnvir;
using S = ServerPackets;

namespace Server.MirObjects.Monsters
{
    public class HumanAssassin : MonsterObject
    {
        public byte AttackRange = 1;
        public int AttackDamage = 0;
        public bool Summoned;
        public long ExplosionTime;

        protected internal HumanAssassin(MonsterInfo info)
            : base(info)
        {
            ExplosionTime = Envir.Time + 1000 * 10;
            Summoned = true;
        }

        protected override void RefreshBase()
        {
            if (Master != null)
            {
                Stats.Clear();
                Stats.Add(Master.Stats);

                Stats[Stat.HPRatePercent] = 0;
                Stats[Stat.MPRatePercent] = 0;
                Stats[Stat.MaxACRatePercent] = 0;
                Stats[Stat.MaxMACRatePercent] = 0;
                Stats[Stat.MaxDCRatePercent] = 0;
                Stats[Stat.MaxMCRatePercent] = 0;
                Stats[Stat.MaxSCRatePercent] = 0;
                Stats[Stat.AttackSpeedRatePercent] = 0;

                Stats[Stat.HP] = 1500;

                MoveSpeed = 100;
                AttackSpeed = Master.AttackSpeed;
            }
        }

        protected override void RefreshAllCore()
        {
            RefreshBase();

            Stats[Stat.HP] += PetLevel * 20;
            Stats[Stat.MinAC] += PetLevel * 2;
            Stats[Stat.MaxAC] += PetLevel * 2;
            Stats[Stat.MinMAC] += PetLevel * 2;
            Stats[Stat.MaxMAC] += PetLevel * 2;
            Stats[Stat.MinDC] += PetLevel;
            Stats[Stat.MaxDC] += PetLevel;

            if (MoveSpeed < 100) MoveSpeed = 100;
            if (AttackSpeed < 100) AttackSpeed = 100;

            RefreshBuffs();
        }

        public override bool Walk(MirDirection dir)
        {
            if (!CanMove) return false;

            Point location = Functions.PointMove(CurrentLocation, dir, 2);

            if (!CurrentMap.ValidPoint(location)) return false;

            using var cellQuery0 = CurrentMap.RentObjectsSnapshot(location);

            bool isBreak = false;


                for (int i = 0; i < cellQuery0.Count; i++)
                {
                    MapObject ob = cellQuery0[i];
                    if (!cellQuery0.IsCurrent(ob)) continue;
                    if (!ob.Blocking) continue;
                    isBreak = true;
                    break;
                }

            if (isBreak)
            {
                location = Functions.PointMove(CurrentLocation, dir, 1);

                if (!CurrentMap.ValidPoint(location)) return false;

                using var cellQuery1 = CurrentMap.RentObjectsSnapshot(location);


                    for (int i = 0; i < cellQuery1.Count; i++)
                    {
                        MapObject ob = cellQuery1[i];
                        if (!cellQuery1.IsCurrent(ob)) continue;
                        if (!ob.Blocking) continue;
                        return false;
                    }
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

            if (isBreak)
                Broadcast(new S.ObjectWalk { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });
            else
                Broadcast(new S.ObjectRun { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });


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

        protected override bool InAttackRange()
        {
            return CurrentMap == Target.CurrentMap && Functions.InRange(CurrentLocation, Target.CurrentLocation, AttackRange);
        }

        protected override void ProcessAI()
        {
            if (Dead) return;

            ProcessSearch();
            ProcessTarget();

            if (Master != null && Master is PlayerObject)
            {
                if (Envir.Time > ExplosionTime) Die();
            }
        }

        protected override void ProcessSearch()
        {
            if (Envir.Time < SearchTime) return;

            SearchTime = Envir.Time + SearchDelay;

            //Stacking or Infront of master - Move
            bool stacking = false;

            using var cellQuery3 = CurrentMap.RentObjectsSnapshot(CurrentLocation);


                for (int i = 0; i < cellQuery3.Count; i++)
                {
                    MapObject ob = cellQuery3[i];
                    if (!cellQuery3.IsCurrent(ob)) continue;
                    if (ob == this || !ob.Blocking) continue;
                    stacking = true;
                    break;
                }

            if (CanMove && stacking)
            {
                //Walk Randomly
                if (!Walk(Direction))
                {
                    MirDirection dir = Direction;

                    switch (Envir.Random.Next(3)) // favour Clockwise
                    {
                        case 0:
                            for (int i = 0; i < 7; i++)
                            {
                                dir = Functions.NextDir(dir);

                                if (Walk(dir))
                                    break;
                            }
                            break;
                        default:
                            for (int i = 0; i < 7; i++)
                            {
                                dir = Functions.PreviousDir(dir);

                                if (Walk(dir))
                                    break;
                            }
                            break;
                    }
                }
            }

            if (Target == null || Envir.Random.Next(3) == 0)
                FindTarget();
        }

        protected override void ProcessTarget()
        {
            if (Target == null || !CanAttack) return;

            if (InAttackRange())
            {
                Attack();
                return;
            }

            if (Envir.Time < ShockTime)
            {
                Target = null;
                return;
            }

            int dist = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);

            if (dist >= AttackRange)
                MoveTo(Target.CurrentLocation);
        }

        protected override void Attack()
        {
            if (AttackDamage >= 500) 
            {   
                Die();
                return;
            }

            ShockTime = 0;

            if (!Target.IsAttackTarget(this))
            {
                Target = null;
                return;
            }


            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            Broadcast(new S.ObjectAttack { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });


            ActionTime = Envir.Time + 300;
            AttackTime = Envir.Time + AttackSpeed;

            int damage = GetAttackPower(Stats[Stat.MinDC], Stats[Stat.MaxDC]);
            AttackDamage += damage;

            if (damage == 0) return;

            DelayedAction action = new DelayedAction(DelayedType.Damage, Envir.Time + 300, Target, damage, DefenceType.ACAgility);
            ActionList.Add(action);
        }

        public override void Spawned()
        {
            base.Spawned();

            Summoned = false;
        }

        public override void Die()
        {
            if (Dead) return;

            ExplosionDie();

            HP = 0;
            Dead = true;

            //DeadTime = Envir.Time + DeadDelay;
            DeadTime = 0;

            Broadcast(new S.ObjectDied { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Type = (byte)2 });

            if (EXPOwner != null && EXPOwner.Node != null && Master == null && EXPOwner.Race == ObjectType.Player) EXPOwner.WinExp(Experience);

            if (Respawn != null)
                Respawn.Count--;

            Master = null;

            PoisonList.Clear();
            Envir.MonsterCount--;

            if (CurrentMap != null)
                CurrentMap.MonsterCount--;
        }

        private void ExplosionDie()
        {
            int criticalDamage = Envir.Random.Next(0, 100) <= Stats[Stat.Accuracy] ? Stats[Stat.MaxDC] * 2 : Stats[Stat.MinDC] * 2;
            int damage = (Stats[Stat.MinDC] / 5 + 4 * (Level / 20)) * criticalDamage / 20 + Stats[Stat.MaxDC];

            for (int i = 0; i < 16; i++)
            {
                MirDirection dir = (MirDirection)(i % 8);
                Point hitPoint = Functions.PointMove(CurrentLocation, dir, (i / 8 + 1));

                if (!CurrentMap.ValidPoint(hitPoint)) continue;

                using var cellQuery4 = CurrentMap.RentObjectsSnapshot(hitPoint);
                for (int j = 0; j < cellQuery4.Count; j++)
                {
                    MapObject target = cellQuery4[j];
                    if (!cellQuery4.IsCurrent(target)) continue;
                    switch (target.Race)
                    {
                        case ObjectType.Monster:
                        case ObjectType.Player:
                            //Only targets
                            if (Master is HumanObject humanOb && target.IsAttackTarget(humanOb))
                            {
                                target.Attacked(humanOb, damage, DefenceType.AC, false);
                            }
                            break;
                    }
                }
            }
        }

        public override Packet GetInfo()
        {
            PlayerObject master = null;
            short weapon = -1;
            short armour = 0;
            byte wing = 0;
            if (Master != null && Master is PlayerObject) 
                master = (PlayerObject)Master;

            if (master != null)
            {
                weapon = master.Looks_Weapon;
                armour = master.Looks_Armour;
                wing = master.Looks_Wings;
            }

            return new S.ObjectPlayer
            {
                ObjectID = ObjectID,
                Name = master != null ? master.Name : Name,
                NameColour = NameColour,
                Class = master != null ? master.Class : MirClass.Assassin,
                Gender = master != null ? master.Gender : MirGender.Male,
                Location = CurrentLocation,
                Direction = Direction,
                Hair = master != null ? master.Hair : (byte)0,
                Weapon = weapon,
                Armour = armour,
                Light = master != null ? master.Light : Light,
                Poison = CurrentPoison,
                Dead = Dead,
                Hidden = Hidden,
                Effect = SpellEffect.None,
                WingEffect = wing,
                Extra = false,
                TransformType = -1
            };
        }
    }
}
