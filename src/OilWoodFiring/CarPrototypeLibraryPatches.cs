using HarmonyLib;
using Model;
using Model.Ops.Definition;

namespace Toolshed.OilWoodFiring
{
	[HarmonyPatch(typeof(CarPrototypeLibrary), "LoadForId")]
	internal static class CarPrototypeLibraryPatches
	{
		private static bool Prefix(CarPrototypeLibrary __instance, string loadId, ref Load __result)
		{
			if (!Main.Enabled ||
				(!string.Equals(loadId, OilFuelConstants.BunkerCLoadId, System.StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(loadId, OilFuelConstants.WoodLoadId, System.StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
			OilFuelRegistry.EnsureRegistered(__instance);
			if (string.Equals(loadId, OilFuelConstants.BunkerCLoadId, System.StringComparison.OrdinalIgnoreCase))
			{
				__result = OilFuelRegistry.FindBunkerCLoad(__instance) ?? OilFuelRegistry.GetOrCreateBunkerCLoad();
				return false;
			}
			__result = OilFuelRegistry.FindWoodLoad(__instance) ?? OilFuelRegistry.GetOrCreateWoodLoad();
			return false;
		}
	}
}
