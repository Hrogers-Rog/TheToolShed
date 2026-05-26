using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model.Definition.Data;

namespace Toolshed.OilWoodFiring
{
	[HarmonyPatch(typeof(CarDefinitionExtensions), "DisplayOrderLoadSlots")]
	internal static class CarDefinitionExtensionsPatches
	{
		private static bool Prefix(CarDefinition definition, ref IEnumerable<ValueTuple<LoadSlot, int>> __result)
		{
			if (!Main.Enabled || definition == null || definition.LoadSlots == null || !OilFiringResolver.HasCustomSteamFuelSlot(definition))
			{
				return true;
			}
			__result = definition.LoadSlots.Select((LoadSlot slot, int index) => new ValueTuple<LoadSlot, int>(slot, index)).OrderBy(delegate(ValueTuple<LoadSlot, int> pair)
			{
				string requiredLoadIdentifier = pair.Item1.RequiredLoadIdentifier;
				if (OilFuelConstants.IsSteamFuelId(requiredLoadIdentifier))
				{
					return -2;
				}
				if (string.Equals(requiredLoadIdentifier, OilFuelConstants.WaterLoadId, StringComparison.OrdinalIgnoreCase))
				{
					return -1;
				}
				return pair.Item2;
			});
			return false;
		}
	}
}
