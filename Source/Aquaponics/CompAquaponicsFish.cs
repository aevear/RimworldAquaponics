using Verse;
using RimWorld;
using Verse.AI;
using System;
using System.Collections.Generic;

namespace Aquaponics
{
    public class Building_Aquaponics : Building_PlantGrower
    {
        private int tickCounter;
        public int storedFish;
        public ThingDef selectedFishType;

        // Configurable properties
        public List<ThingDef> allowedFish = new List<ThingDef>
        {
            ThingDef.Named("Fish_Tilapia"),
            ThingDef.Named("Fish_Cod"),
            ThingDef.Named("Fish_Catfish")
        };
        public int productionInterval = 2500;
        public int fishPerCycle = 1;
        public int maxStoredFish = 120;
        public int minStoredFish = 10;
        public float autoHarvestThresholdPercent = 1.0f;
        public int breedingPopulationCount = 10;

        public ThingDef FishType
        {
            get => selectedFishType;
            set => selectedFishType = value;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // Set default fish type to tilapia if none selected
            if (selectedFishType == null && !respawningAfterLoad)
            {
                selectedFishType = ThingDef.Named("Fish_Tilapia");
            }
        }

        private int lastJobTick = -1;
        private const int JOB_COOLDOWN_TICKS = 60; // 1 second cooldown

        // Add this method to your Building_Aquaponics class:
        public bool CanAcceptNewJob()
        {
            return Find.TickManager.TicksGame - lastJobTick > JOB_COOLDOWN_TICKS;
        }

        public void OnJobStarted()
        {
            lastJobTick = Find.TickManager.TicksGame;
        }

        // Update your ExposeData method to include:
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
            Scribe_Values.Look(ref storedFish, "storedFish", 0);
            Scribe_Values.Look(ref lastJobTick, "lastJobTick", -1); // Add this line
            Scribe_Defs.Look(ref selectedFishType, "selectedFishType");
        }

        public override void TickRare()
        {
            base.TickRare();

            // Only increment tick counter and produce fish if temperature is suitable
            if (IsTemperatureSuitable)
            {
                tickCounter += 250;
                if (tickCounter >= productionInterval && storedFish >= minStoredFish)
                {
                    tickCounter = 0;
                    if (storedFish < maxStoredFish)
                        storedFish += fishPerCycle;
                }

                // Only auto-harvest if temperature is suitable
                if (storedFish >= maxStoredFish * autoHarvestThresholdPercent)
                    HarvestFish();
            }
            else
            {
                // Reset tick counter when temperature becomes unsuitable
                // This prevents "saved up" production when temperature returns to normal
                tickCounter = 0;
            }
        }

        public bool NeedsFish() => storedFish < minStoredFish && IsTemperatureSuitable && selectedFishType != null;
        public bool CanAcceptFish() => storedFish < maxStoredFish && IsTemperatureSuitable && selectedFishType != null;
        public bool CanAcceptFish(ThingDef fishDef) => CanAcceptFish() && (selectedFishType == null || selectedFishType == fishDef) && allowedFish.Contains(fishDef);

        public bool IsTemperatureSuitable
        {
            get
            {
                float temp = Position.GetTemperature(Map);
                return temp >= 10f && temp <= 42f;
            }
        }

        public bool TryAcceptThing(Thing thing)
        {
            if (thing == null || !allowedFish.Contains(thing.def)) return false;

            // Don't accept fish if temperature is unsuitable
            if (!IsTemperatureSuitable) return false;

            // If no fish type selected yet, accept the first compatible fish
            if (selectedFishType == null)
            {
                selectedFishType = thing.def;
                return CanAcceptFish();
            }

            // Only accept fish of the same type as already selected
            return selectedFishType == thing.def && CanAcceptFish();
        }

        public void AddFishFromHaul(int amount)
        {
            // Only add fish if temperature is suitable
            if (IsTemperatureSuitable)
            {
                storedFish = Math.Min(storedFish + amount, maxStoredFish);
            }
        }

        private void HarvestFish()
        {
            if (storedFish > breedingPopulationCount && selectedFishType != null && IsTemperatureSuitable)
            {
                int harvestAmount = storedFish - breedingPopulationCount;
                Thing fish = ThingMaker.MakeThing(selectedFishType);
                fish.stackCount = harvestAmount;

                // Use DropThing instead of TryPlaceThing for RimWorld 1.5 compatibility
                IntVec3 dropCell = InteractionCell;
                if (!dropCell.IsValid || dropCell.Impassable(Map))
                {
                    // Find a nearby valid cell if interaction cell is blocked
                    if (!GenDrop.TryDropSpawn(fish, InteractionCell, Map, ThingPlaceMode.Near, out fish))
                    {
                        // If that fails, try the building's position
                        fish.Position = Position;
                        fish.SpawnSetup(Map, false);
                    }
                }
                else
                {
                    fish.Position = dropCell;
                    fish.SpawnSetup(Map, false);
                }

                storedFish = breedingPopulationCount;
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();

            if (!IsTemperatureSuitable)
            {
                float temp = Position.GetTemperature(Map);
                string tempMessage = $"Temperature unsuitable for fish ({temp:F1}°C). Requires 10°C - 42°C.";

                // Add fish dying warning if there are fish present
                if (storedFish > 0)
                {
                    tempMessage += $"\nFish are dying! Population: {storedFish}";
                }

                return string.IsNullOrEmpty(baseString) ? tempMessage : baseString + "\n" + tempMessage;
            }

            string fishStatus;
            if (selectedFishType == null)
                fishStatus = "No fish type selected.";
            else if (storedFish < minStoredFish)
                fishStatus = $"No {selectedFishType.label} stored, need breeding stock.";
            else if (storedFish >= maxStoredFish)
                fishStatus = $"{selectedFishType.label.CapitalizeFirst()} population full ({storedFish}/{maxStoredFish})";
            else
                fishStatus = $"{selectedFishType.label.CapitalizeFirst()} population is growing";

            return string.IsNullOrEmpty(baseString) ? fishStatus : baseString + "\n" + fishStatus;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            // Fish selection gizmo (similar to plant selection)
            if (Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    action = delegate
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();

                        foreach (ThingDef fishDef in allowedFish)
                        {
                            if (!IsTemperatureSuitable)
                            {
                                // Create disabled option for unsuitable temperature
                                string disabledLabel = fishDef.LabelCap + " (Temperature unsuitable)";
                                options.Add(new FloatMenuOption(disabledLabel, null));
                            }
                            else
                            {
                                options.Add(new FloatMenuOption(fishDef.LabelCap, delegate
                                {
                                    if (storedFish > 0 && selectedFishType != fishDef)
                                    {
                                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                            "This will clear all existing fish from the basin. Continue?",
                                            delegate
                                            {
                                                storedFish = 0;
                                                selectedFishType = fishDef;
                                            }));
                                    }
                                    else
                                    {
                                        selectedFishType = fishDef;
                                    }
                                }));
                            }
                        }

                        Find.WindowStack.Add(new FloatMenu(options));
                    },
                    defaultLabel = "Select fish type",
                    defaultDesc = "Select which type of fish to raise in this basin.",
                    hotKey = KeyBindingDefOf.Misc1,
                    icon = selectedFishType?.uiIcon ?? BaseContent.BadTex
                };
            }
        }
    }
}