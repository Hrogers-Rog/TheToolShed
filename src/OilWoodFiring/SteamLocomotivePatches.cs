using HarmonyLib;
using Model;

namespace Toolshed.OilWoodFiring
{
	[HarmonyPatch(typeof(SteamLocomotive), "PeriodicUpdate")]
	internal static class SteamLocomotivePeriodicUpdatePatch
	{
		private static bool Prefix(SteamLocomotive __instance, float dt)
		{
			if (!Main.Enabled)
			{
				return true;
			}
			if (!OilFiringResolver.UsesCustomSteamFuel(__instance))
			{
				return true;
			}
			return OilConsumptionService.RunCustomFuelPeriodicUpdateSafely(__instance, dt);
		}
	}
}
