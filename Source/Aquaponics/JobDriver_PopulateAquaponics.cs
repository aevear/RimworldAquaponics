using Verse;
using RimWorld;
using Verse.AI;
using System.Collections.Generic;
using System;

namespace Aquaponics
{
    public class JobDriver_PopulateAquaponics : JobDriver
    {
        private const TargetIndex BasinIndex = TargetIndex.A;
        private const TargetIndex FishIndex = TargetIndex.B;

        private Building_Aquaponics Basin => job.GetTarget(BasinIndex).Thing as Building_Aquaponics;
        private Thing Fish => job.GetTarget(FishIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Validate targets before making reservations
            if (Basin == null || Fish == null) return false;
            if (!Basin.Spawned || Basin.Destroyed) return false;
            if (!Fish.Spawned || Fish.Destroyed) return false;
            
            // Check temperature and fish needs before reserving
            if (!Basin.IsTemperatureSuitable || !Basin.NeedsFish()) return false;
            
            // Make reservations
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                return false;
            if (!pawn.Reserve(job.targetB, job, 1, job.count, null, errorOnFailed))
                return false;
            return true;
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            
            // Thorough validation when starting
            if (Basin == null || Fish == null)
            {
                Log.Warning("[Aquaponics] Job starting with null targets");
                EndJobWith(JobCondition.Errored);
                return;
            }
            
            if (!Basin.Spawned || Basin.Destroyed || !Fish.Spawned || Fish.Destroyed)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            
            if (!Basin.NeedsFish() || !Basin.IsTemperatureSuitable)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            
            if (!Basin.CanAcceptFish(Fish.def))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Comprehensive fail conditions with additional safety checks
            this.FailOnDespawnedNullOrForbidden(BasinIndex);
            this.FailOnDespawnedNullOrForbidden(FishIndex);
            this.FailOnBurningImmobile(BasinIndex);
            this.FailOnDestroyedOrNull(BasinIndex);
            this.FailOnDestroyedOrNull(FishIndex);
            
            // More robust condition checking
            this.FailOn(() => {
                if (Basin == null || Fish == null) return true;
                if (!Basin.Spawned || Basin.Destroyed) return true;
                if (!Fish.Spawned || Fish.Destroyed) return true;
                if (!Basin.NeedsFish()) return true;
                if (!Basin.IsTemperatureSuitable) return true;
                if (!Basin.CanAcceptFish(Fish.def)) return true;
                return false;
            });
            
            this.FailOnForbidden(FishIndex);

            // Go to the fish with additional safety checks
            yield return Toils_Goto.GotoThing(FishIndex, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(FishIndex)
                .FailOnSomeonePhysicallyInteracting(FishIndex)
                .FailOn(() => Basin == null || !Basin.IsTemperatureSuitable || !Basin.NeedsFish());

            // Pickup toil with better error handling
            yield return new Toil
            {
                initAction = () =>
                {
                    Thing fish = Fish;
                    Building_Aquaponics basin = Basin;
                    
                    if (fish == null || !fish.Spawned || fish.Destroyed)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    if (basin == null || !basin.Spawned || basin.Destroyed)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // Final validation before pickup
                    if (!basin.IsTemperatureSuitable || !basin.NeedsFish() || !basin.CanAcceptFish(fish.def))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // Calculate fish to take
                    int fishToTake = Math.Min(job.count, fish.stackCount);
                    if (fishToTake <= 0)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    
                    Thing takenFish = fish.SplitOff(fishToTake);
                    if (takenFish != null)
                    {
                        if (!pawn.carryTracker.TryStartCarry(takenFish))
                        {
                            // If we can't carry it, drop it and fail gracefully
                            takenFish.Position = fish.Position;
                            takenFish.SpawnSetup(fish.Map, false);
                            EndJobWith(JobCondition.Incompletable);
                        }
                    }
                    else
                    {
                        EndJobWith(JobCondition.Incompletable);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            }
            .FailOnDespawnedNullOrForbidden(FishIndex)
            .FailOn(() => Basin == null || !Basin.IsTemperatureSuitable || !Basin.NeedsFish());

            // Go to the basin
            yield return Toils_Goto.GotoThing(BasinIndex, PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(BasinIndex)
                .FailOn(() => Basin == null || !Basin.IsTemperatureSuitable || !Basin.NeedsFish());

            // Wait and add fish to basin
            yield return Toils_General.Wait(300, BasinIndex)
                .WithProgressBarToilDelay(BasinIndex)
                .FailOn(() => Basin == null || !Basin.IsTemperatureSuitable || !Basin.NeedsFish());

            // Final toil: add fish to basin
            yield return new Toil
            {
                initAction = () =>
                {
                    Building_Aquaponics basin = Basin;
                    Thing carriedFish = pawn.carryTracker.CarriedThing;

                    if (basin == null || !basin.Spawned || basin.Destroyed)
                    {
                        // Drop carried fish if basin is gone
                        if (carriedFish != null)
                        {
                            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
                        }
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // Final temperature and capacity check
                    if (!basin.IsTemperatureSuitable)
                    {
                        // Temperature became unsuitable, drop the fish nearby
                        if (carriedFish != null)
                        {
                            pawn.carryTracker.TryDropCarriedThing(basin.InteractionCell, ThingPlaceMode.Near, out Thing _);
                        }
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    if (carriedFish != null)
                    {
                        int fishCount = carriedFish.stackCount;
                        if (basin.CanAcceptFish(carriedFish.def) && basin.NeedsFish())
                        {
                            basin.AddFishFromHaul(fishCount);
                            pawn.carryTracker.DestroyCarriedThing();
                        }
                        else
                        {
                            // Basin can't accept fish, drop nearby
                            pawn.carryTracker.TryDropCarriedThing(basin.InteractionCell, ThingPlaceMode.Near, out Thing _);
                        }
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}