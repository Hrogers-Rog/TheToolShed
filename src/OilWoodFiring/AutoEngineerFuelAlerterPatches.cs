using System;
using HarmonyLib;
using Model.AI;

namespace Toolshed.OilWoodFiring
{
	[HarmonyPatch(typeof(AutoEngineerFuelAlerter), "LoadCategoryForLoadId")]
	internal static class AutoEngineerFuelAlerterPatches
	{
		private static readonly Type LoadCategoryType = AccessTools.Inner(typeof(AutoEngineerFuelAlerter), "LoadCategory");

		private static bool _loggedAlertCategory;

		private static bool Prefix(string loadId, ref object __result)
		{
			if (!Main.Enabled ||
				(!string.Equals(loadId, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(loadId, OilFuelConstants.WoodLoadId, StringComparison.OrdinalIgnoreCase)) ||
				LoadCategoryType == null)
			{
				return true;
			}
			__result = Enum.ToObject(LoadCategoryType, 0);
			if (!_loggedAlertCategory)
			{
				_loggedAlertCategory = true;
				Main.Log("custom fuel alert category used.");
			}
			return false;
		}
	}
}
