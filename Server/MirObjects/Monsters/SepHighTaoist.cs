using System.Drawing;
using System.Linq;
using Server.MirDatabase;
using Server.MirEnvir;
using S = ServerPackets;

namespace Server.MirObjects.Monsters
{
    public class SepHighTaoist : MonsterObject
    {
        public long FearTime, DecreaseMPTime, SummonTime;
        public byte AttackRange = 5;
        public bool Summoned;

        protected internal SepHighTaoist(MonsterInfo info)
            : base(info)
        {
        }

        protected override bool InAttackRange()
        {
            return CurrentMap == Target.CurrentMap && Functions.InRange(CurrentLocation, Target.CurrentLocation, Info.ViewRange);
        }

        protected override void Attack()
        {
            if (!Target.IsAttackTarget(this))
            {
                Target = null;
                return;
            }

            ShockTime = 0;

            ActionTime = Envir.Time + 300;
            AttackTime = Envir.Time + AttackSpeed;

            int damage = GetAttackPower(Stats[Stat.MinDC], Stats[Stat.MaxDC]);
            if (damage == 0) return;

            Direction = Functions.DirectionFromPoint(CurrentLocation, Target.CurrentLocation);
            DelayedAction action;
            int delay = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation) * 50 + 500; //50 MS per Step

            if (!Target.PoisonList.Any(x => x.PType == PoisonType.Green) && !Target.PoisonList.Any(x => x.PType == PoisonType.Red))
            {
                Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Spell = Spell.Poisoning, TargetID = Target.ObjectID, Target = Target.CurrentLocation, Cast = true, Level = 3 });
                int power = GetAttackPower(Stats[Stat.MinSC], Stats[Stat.MaxSC]);
                Target.ApplyPoison(new Poison
                {
                    Duration = power + ((Envir.Random.Next(0, 3) + 1) * 7),
                    Owner = this,
                    PType = PoisonType.Green,
                    TickSpeed = 2000,
                    Value = power / 15 + 4
                }, this);
                return;
            }

            else if (Target.PoisonList.Any(x => x.PType == PoisonType.Green) && !Target.PoisonList.Any(x => x.PType == PoisonType.Red))
            {
                Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Spell = Spell.Poisoning, TargetID = Target.ObjectID, Target = Target.CurrentLocation, Cast = true, Level = 3 });
                int power = GetAttackPower(Stats[Stat.MinSC], Stats[Stat.MaxSC]);
                Target.ApplyPoison(new Poison
                {
                    Duration = power + ((Envir.Random.Next(0, 3) + 1) * 7),
                    Owner = this,
                    PType = PoisonType.Red,
                    TickSpeed = 2000,
                }, this);
                return;
            }

            else if (!Target.PoisonList.Any(x => x.PType == PoisonType.Green) && Target.PoisonList.Any(x => x.PType == PoisonType.Red))
            {
                Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Spell = Spell.Poisoning, TargetID = Target.ObjectID, Target = Target.CurrentLocation, Cast = true, Level = 3 });
                int power = GetAttackPower(Stats[Stat.MinSC], Stats[Stat.MaxSC]);
                Target.ApplyPoison(new Poison
                {
                    Duration = power + ((Envir.Random.Next(0, 3) + 1) * 7),
                    Owner = this,
                    PType = PoisonType.Green,
                    TickSpeed = 2000,
                }, this);
                return;
            }


            if (!Target.Buffs.Any(e => e.Type == BuffType.Curse) && Envir.Random.Next(8) == 0)
            {
                Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Spell = Spell.Curse, TargetID = Target.ObjectID, Target = Target.CurrentLocation, Cast = true, Level = 3 });
                PoisonTarget(Target, 1, 5, PoisonType.Slow, 1000);
                return;
            }

            if (PercentHealth <= 90 && Envir.Random.Next(8) == 0)
            {
                Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Spell = Spell.MassHealing, TargetID = ObjectID, Target = CurrentLocation, Cast = true, Level = 3 });
                TriangleAttack(damage, 2, 1, 800);
                return;
            }


            if (Pets.Count < 1)
            {
                MonsterObject monster;

                MonsterInfo info = Envir.GetMonsterInfo(Settings.ShinsuName);
                if (info == null) return;

                monster = MonsterObject.GetMonster(info);
                monster.PetLevel = 3;
                monster.Master = this;
                monster.MaxPetLevel = 7;
                monster.Direction = Direction;
                monster.ActionTime = Envir.Time + 1000;
                monster.Target = Target;
                Pets.Add(monster);

                Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Spell = Spell.SummonShinsu, TargetID = Target.ObjectID, Target = Target.CurrentLocation, Cast = true, Level = 3 });

                action = new DelayedAction(DelayedType.Spawn, Envir.Time + 1000, monster, Front);

                CurrentMap.ActionList.Add(action);
                return;
            }

            Broadcast(new S.ObjectMagic { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Spell = Spell.SoulFireBall, TargetID = Target.ObjectID, Target = Target.CurrentLocation, Cast = true, Level = 3 });

            HalfmoonAttack(damage);

            if (Target.Dead)
                FindTarget();
        }

        protected override void ProcessTarget()
        {
            if (Target == null || !CanAttack) return;

            if (!InAttackRange())
            {
                if (CurrentLocation == Target.CurrentLocation)
                {
                    MirDirection direction = (MirDirection)Envir.Random.Next(8);
                    int rotation = Envir.Random.Next(2) == 0 ? 1 : -1;

                    for (int d = 0; d < 8; d++)
                    {
                        if (Walk(direction)) break;

                        direction = Functions.ShiftDirection(direction, rotation);
                    }
                }
                else
                    MoveTo(Target.CurrentLocation);
            }

            if (!CanAttack) return;

            if (InAttackRange() && Envir.Time < FearTime)
            {
                Attack();
                return;
            }

            FearTime = Envir.Time + 5000;

            if (Envir.Time < ShockTime)
            {
                Target = null;
                return;
            }

            int dist = Functions.MaxDistance(CurrentLocation, Target.CurrentLocation);

            if (dist < AttackRange)
            {
                MirDirection dir = Functions.DirectionFromPoint(Target.CurrentLocation, CurrentLocation);

                if (Walk(dir)) return;

                switch (Envir.Random.Next(2)) //No favour
                {
                    case 0:
                        for (int i = 0; i < 7; i++)
                        {
                            dir = Functions.NextDir(dir);

                            if (Walk(dir))
                                return;
                        }
                        break;
                    default:
                        for (int i = 0; i < 7; i++)
                        {
                            dir = Functions.PreviousDir(dir);

                            if (Walk(dir))
                                return;
                        }
                        break;
                }
            }
        }



        public bool Walk(MirDirection dir, bool br = false)
        {
            if (!CanMove) return false;

            var temploc = Functions.PointMove(CurrentLocation, dir, 1);

            if (!CurrentMap.ValidPoint(temploc)) return false;

            using var cellQuery0 = CurrentMap.RentObjectsSnapshot(temploc);


                for (int i = 0; i < cellQuery0.Count; i++)
                {
                    MapObject ob = cellQuery0[i];
                    if (!cellQuery0.IsCurrent(ob)) continue;
                    if (!ob.Blocking) continue;
                    return false;
                }



            Point location = Functions.PointMove(CurrentLocation, dir, 2);

            if (!CurrentMap.ValidPoint(location)) return false;

            using var cellQuery1 = CurrentMap.RentObjectsSnapshot(location);

            bool isBreak = br;




                for (int i = 0; i < cellQuery1.Count; i++)
                {
                    MapObject ob = cellQuery1[i];
                    if (!cellQuery1.IsCurrent(ob)) continue;
                    if (!ob.Blocking) continue;
                    isBreak = true;
                    break;
                }

            if (isBreak)
            {
                location = Functions.PointMove(CurrentLocation, dir, 1);

                if (!CurrentMap.ValidPoint(location)) return false;

                using var cellQuery2 = CurrentMap.RentObjectsSnapshot(location);


                    for (int i = 0; i < cellQuery2.Count; i++)
                    {
                        MapObject ob = cellQuery2[i];
                        if (!cellQuery2.IsCurrent(ob)) continue;
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
                Hidden = false;

                for (int i = 0; i < Buffs.Count; i++)
                {
                    if (Buffs[i].Type != BuffType.Hiding) continue;

                    Buffs[i].ExpireTime = 0;
                    break;
                }
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


            using var cellQuery3 = CurrentMap.RentObjectsSnapshot(CurrentLocation);

            for (int i = 0; i < cellQuery3.Count; i++)
            {
                MapObject currentCellObject = cellQuery3[i];
                if (!cellQuery3.IsCurrent(currentCellObject)) continue;
                if (currentCellObject.Race != ObjectType.Spell) continue;
                SpellObject ob = (SpellObject)currentCellObject;

                ob.ProcessSpell(this);
                //break;
            }

            return true;
        }


        public override void Die()
        {
            if (Dead) return;

            HP = 0;
            Dead = true;

            //DeadTime = Envir.Time + DeadDelay;
            DeadTime = 0;

            Broadcast(new S.ObjectDied { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation, Type = (byte)(Master != null ? 1 : 0) });

            if (EXPOwner != null && Master == null && EXPOwner.Race == ObjectType.Player) EXPOwner.WinExp(Experience, Level);

            if (Respawn != null)
                Respawn.Count--;

            if (Master == null)
                Drop();

            Master = null;

            PoisonList.Clear();
            Envir.MonsterCount--;

            if (CurrentMap != null)
                CurrentMap.MonsterCount--;
        }

        public override Packet GetInfo()
        {
            PlayerObject master = null;
            short weapon = -1;
            short armour = 0;
            byte wing = 0;

            if (Master != null && Master is PlayerObject) master = (PlayerObject)Master;
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
                Class = MirClass.Taoist,
                Gender = master != null ? master.Gender : Envir.Random.Next(1, 2) == 1 ? MirGender.Male : MirGender.Female,
                Location = CurrentLocation,
                Direction = Direction,
                Hair = master != null ? master.Hair : (byte)Envir.Random.Next(0, 5),
                Weapon = 53,
                Armour = 33,
                Light = master != null ? master.Light : Light,
                Poison = CurrentPoison,
                Dead = Dead,
                Hidden = Hidden,
                Effect = SpellEffect.None,
                WingEffect = wing,
                Extra = Summoned,
                TransformType = -1,
            };
        }
    }
}

