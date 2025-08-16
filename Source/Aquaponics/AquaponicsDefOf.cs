using RimWorld;
using Verse;

namespace Aquaponics
{
    [DefOf]
    public static class AquaponicsDefOf
    {
        public static ThingDef AquaponicsBasin;
        public static ThingDef Fish_Tilapia;
        public static JobDef PopulateAquaponics;

        static AquaponicsDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AquaponicsDefOf));
        }
    }
}
