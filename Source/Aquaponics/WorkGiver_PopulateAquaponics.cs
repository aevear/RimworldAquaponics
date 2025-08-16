using Verse;
using RimWorld;
using Verse.AI;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Aquaponics
{
    public class WorkGiver_PopulateAquaponics : WorkGiver_Scanner
    {
        // Scan BASINS, not fish
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(AquaponicsDefOf.AquaponicsBasin);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            var list = pawn.Map?.listerThings?.ThingsOfDef(AquaponicsDefOf.AquaponicsBasin);
            if (list == null || list.Count == 0) return true;

            foreach (var b in list.Cast<Building_Aquaponics>())
            {
                if (b != null
                    && b.Spawned && !b.Destroyed
                    && b.IsTemperatureSuitable
                    && b.NeedsFish()
                    && b.Faction == pawn.Faction
                    && !b.IsForbidden(pawn)
                    && pawn.CanReach(b, PathEndMode.InteractionCell, Danger.Deadly)
                    && pawn.CanReserve(b, 1))
                {
                    return false; // at least one basin is doable
                }
            }
            return true;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var basin = t as Building_Aquaponics;
            if (basin == null) return false;

            if (!basin.Spawned || basin.Destroyed) return false;
            if (t.IsForbidden(pawn)) return false;

            // Check cooldown - this MUST match the logic in JobOnThing
            int basinId = basin.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;

            if (lastJobCreatedTick.ContainsKey(basinId) &&
                currentTick - lastJobCreatedTick[basinId] < JOB_CREATION_COOLDOWN)
            {
                return false; // Too soon to create another job for this basin
            }

            // Check if basin already has an active job assigned to it
            if (pawn.Map.reservationManager.IsReservedByAnyoneOf(basin, pawn.Faction)) return false;
            if (!pawn.CanReserve(basin, 1)) return false;

            if (!basin.IsTemperatureSuitable) return false;
            if (!basin.NeedsFish()) return false;

            // Find a compatible, reachable, reservable fish stack
            var fish = FindClosestCompatibleFish(pawn, basin);
            if (fish == null) return false;

            // Check if this specific fish is already reserved for aquaponics work
            if (pawn.Map.reservationManager.IsReservedByAnyoneOf(fish, pawn.Faction)) return false;
            if (!pawn.CanReserve(fish, 1)) return false;

            return true;
        }

        private static Dictionary<int, int> lastJobCreatedTick = new Dictionary<int, int>();
        private const int JOB_CREATION_COOLDOWN = 60; // 1 second cooldown

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var basin = t as Building_Aquaponics;
            if (basin == null) return null;

            // Prevent rapid job recreation for the same basin
            int basinId = basin.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;

            if (lastJobCreatedTick.ContainsKey(basinId) &&
                currentTick - lastJobCreatedTick[basinId] < JOB_CREATION_COOLDOWN)
            {
                return null; // Too soon to create another job for this basin
            }

            // Double-check basin state
            if (!basin.Spawned || basin.Destroyed) return null;
            if (!basin.IsTemperatureSuitable) return null;
            if (!basin.NeedsFish()) return null;

            var fish = FindClosestCompatibleFish(pawn, basin);
            if (fish == null) return null;

            int needed = Math.Max(0, basin.maxStoredFish - basin.storedFish);
            if (needed <= 0) return null;

            int carryCap = pawn.carryTracker.MaxStackSpaceEver(fish.def);
            int toTake = Math.Min(10, Math.Min(needed, Math.Min(fish.stackCount, carryCap)));
            if (toTake <= 0) return null;

            // Final reservation checks - this is critical for preventing duplicate jobs
            if (!pawn.CanReserve(basin, 1)) return null;
            if (!pawn.CanReserve(fish, 1, toTake)) return null;

            // Additional check: make sure no one else is already doing this exact job
            if (IsJobAlreadyQueued(pawn, basin, fish)) return null;

            // Record that we're creating a job for this basin
            lastJobCreatedTick[basinId] = currentTick;

            // A = basin, B = fish (matches your JobDriver)
            Job job = JobMaker.MakeJob(AquaponicsDefOf.PopulateAquaponics, basin, fish);
            job.count = toTake;

            Log.Message($"[Aquaponics] Creating job: {toTake} {fish.def.label} for basin {basinId}");
            return job;
        }

        private bool IsJobAlreadyQueued(Pawn pawn, Building_Aquaponics basin, Thing fish)
        {
            // Check if any pawn on this map already has this job queued
            foreach (Pawn otherPawn in pawn.Map.mapPawns.FreeColonists)
            {
                if (otherPawn == pawn) continue;

                foreach (QueuedJob queuedJob in otherPawn.jobs.jobQueue)
                {
                    if (queuedJob.job.def == AquaponicsDefOf.PopulateAquaponics &&
                        queuedJob.job.targetA.Thing == basin &&
                        queuedJob.job.targetB.Thing == fish)
                    {
                        return true;
                    }
                }

                // Also check current job
                if (otherPawn.jobs.curJob?.def == AquaponicsDefOf.PopulateAquaponics &&
                    otherPawn.jobs.curJob.targetA.Thing == basin &&
                    otherPawn.jobs.curJob.targetB.Thing == fish)
                {
                    return true;
                }
            }
            return false;
        }

        private Thing FindClosestCompatibleFish(Pawn pawn, Building_Aquaponics basin)
        {
            if (basin == null || basin.Map == null) return null;

            // Use a much simpler approach that definitely works in RimWorld 1.5
            Thing bestFish = null;
            float bestDistSq = float.MaxValue;

            // Manually search through all haulable things on the map
            foreach (Thing thing in basin.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                if (thing.IsForbidden(pawn)) continue;
                if (!basin.CanAcceptFish(thing.def)) continue;
                if (!pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                if (!pawn.CanReserve(thing, 1)) continue;
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false)) continue;

                // Additional check: make sure this fish isn't already being used for aquaponics by someone else
                if (pawn.Map.reservationManager.IsReservedByAnyoneOf(thing, pawn.Faction)) continue;

                float distSq = (thing.Position - basin.Position).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestFish = thing;
                }
            }

            return bestFish;
        }
    }
}