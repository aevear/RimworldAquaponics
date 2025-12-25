using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace AquaponicsMod
{
    public class Building_AquaponicsBay : Building_PlantGrower
    {
        private int fishCount = 0;
        private int tickCounter = 0;
        private const int TicksPerFishGrowth = 5000; // Adjust growth rate as needed
        private const int MaxFish = 26;
        private const int HarvestThreshold = 24;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fishCount, "fishCount", 0);
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
        }

        public override void Tick()
        {
            base.Tick();

            if (fishCount > 0 && fishCount < MaxFish)
            {
                tickCounter++;
                if (tickCounter >= TicksPerFishGrowth)
                {
                    fishCount++;
                    tickCounter = 0;
                    if (fishCount > MaxFish)
                    {
                        fishCount = MaxFish;
                    }
                }
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(base.GetInspectString());
            
            if (fishCount <= 0)
            {
                sb.AppendLine();
                sb.Append("Aquaponics Bay is empty. Add fish to start the cycle.");
            }

            if (fishCount > 0)
            {
                sb.AppendLine();
                sb.Append($"Fish are breeding...");
            }
                        
            if (fishCount >= HarvestThreshold)
            {
                sb.AppendLine();
                sb.Append("Ready to harvest fish!");
            }
            
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            if (fishCount == 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Add Fish",
                    defaultDesc = "Add a fish to start the aquaponics cycle.",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Hunt", true),
                    action = delegate
                    {
                        fishCount = 1;
                        tickCounter = 0;
                    }
                };
            }

            if (fishCount >= HarvestThreshold)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Harvest Fish",
                    defaultDesc = "Harvest the fish, leaving one to continue breeding.",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Hunt", true),
                    action = delegate
                    {
                        HarvestFish();
                    }
                };
            }

            // Debug Gizmos (only show in dev mode)
            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Add 5 Fish",
                    defaultDesc = "Debug: Instantly add 5 fish to the bay.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", true),
                    action = delegate
                    {
                        fishCount = Mathf.Min(fishCount + 5, MaxFish);
                        Messages.Message($"Added 5 fish. Current count: {fishCount}", MessageTypeDefOf.NeutralEvent);
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Set to Harvest",
                    defaultDesc = "Debug: Set fish count to harvest threshold.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", true),
                    action = delegate
                    {
                        fishCount = HarvestThreshold;
                        Messages.Message($"Fish count set to {HarvestThreshold} (harvest ready)", MessageTypeDefOf.PositiveEvent);
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Reset Bay",
                    defaultDesc = "Debug: Reset fish count to zero.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", true),
                    action = delegate
                    {
                        fishCount = 0;
                        tickCounter = 0;
                        Messages.Message("Aquaponics bay reset.", MessageTypeDefOf.NeutralEvent);
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Max Fish",
                    defaultDesc = "Debug: Fill bay to maximum capacity.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", true),
                    action = delegate
                    {
                        fishCount = MaxFish;
                        Messages.Message($"Fish count set to maximum: {MaxFish}", MessageTypeDefOf.PositiveEvent);
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Speed Growth",
                    defaultDesc = "Debug: Advance growth by 90%.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", true),
                    action = delegate
                    {
                        if (fishCount > 0 && fishCount < MaxFish)
                        {
                            tickCounter = (int)(TicksPerFishGrowth * 0.9f);
                            Messages.Message($"Growth accelerated to 90%", MessageTypeDefOf.NeutralEvent);
                        }
                        else
                        {
                            Messages.Message("No fish growing currently.", MessageTypeDefOf.RejectInput);
                        }
                    }
                };
            }
        }

        public void HarvestFish()
        {
            if (fishCount >= HarvestThreshold)
            {
                int harvestAmount = fishCount - 1;
                
                // Spawn Tilapia (Fish_Tilapia from Odyssey)
                ThingDef fishDef = ThingDef.Named("Fish_Tilapia");
                if (fishDef != null)
                {
                    Thing fish = ThingMaker.MakeThing(fishDef);
                    fish.stackCount = harvestAmount;
                    
                    // Use GenSpawn instead of GenPlace for RimWorld 1.6
                    GenSpawn.Spawn(fish, Position, Map);
                    
                    Messages.Message($"Harvested {harvestAmount} tilapia from aquaponics bay.", 
                        new LookTargets(Position, Map), MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Log.Error("AquaponicsMod: Could not find Fish_Tilapia ThingDef. Make sure RimWorld Odyssey (1.6) is loaded.");
                }
                
                fishCount = 1;
                tickCounter = 0;
                
                // Optional: Add experience for grower skill
                Pawn pawn = Map.mapPawns.FreeColonistsSpawned.RandomElement();
                if (pawn != null)
                {
                    pawn.skills.Learn(SkillDefOf.Plants, 50f);
                }
            }
        }

        public int FishCount => fishCount;
        
        public bool CanHarvestFish => fishCount >= HarvestThreshold;
    }

    // Work giver for adding fish
    public class WorkGiver_AddFish : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("AquaponicsBay"));

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_AquaponicsBay bay = t as Building_AquaponicsBay;
            if (bay == null || bay.FishCount > 0)
            {
                return false;
            }

            if (t.IsForbidden(pawn) || !pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
            {
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("AddFishToAquaponics"), t);
        }
    }

    // Work giver for harvesting fish
    public class WorkGiver_HarvestFish : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("AquaponicsBay"));

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_AquaponicsBay bay = t as Building_AquaponicsBay;
            if (bay == null || !bay.CanHarvestFish)
            {
                return false;
            }

            if (t.IsForbidden(pawn) || !pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
            {
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HarvestFishFromAquaponics"), t);
        }
    }

    // Job driver for adding fish
    public class JobDriver_AddFish : JobDriver
    {
        private const int AddFishWorkTicks = 180; // About 3 seconds (60 ticks = 1 second)

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            
            // Wait while working
            Toil waitToil = Toils_General.Wait(AddFishWorkTicks);
            waitToil.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            waitToil.defaultCompleteMode = ToilCompleteMode.Delay;
            yield return waitToil;
            
            // Actually add the fish
            Toil addFish = new Toil();
            addFish.initAction = delegate
            {
                Building_AquaponicsBay bay = job.targetA.Thing as Building_AquaponicsBay;
                if (bay != null && bay.FishCount == 0)
                {
                    bay.GetType().GetField("fishCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(bay, 1);
                    bay.GetType().GetField("tickCounter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(bay, 0);
                }
            };
            addFish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return addFish;
        }
    }

    // Job driver for harvesting fish
    public class JobDriver_HarvestFish : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            
            Toil harvestFish = new Toil();
            harvestFish.initAction = delegate
            {
                Building_AquaponicsBay bay = job.targetA.Thing as Building_AquaponicsBay;
                if (bay != null && bay.CanHarvestFish)
                {
                    bay.HarvestFish();
                }
            };
            harvestFish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return harvestFish;
        }
    }
}