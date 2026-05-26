using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model.Definition.Components;
using Model.Definition.Data;

namespace Toolshed.OilWoodFiring
{
	[HarmonyPatch]
	internal static class DefinitionCheckerPatches
	{
		private static readonly Type DefinitionCheckerType = AccessTools.TypeByName("Model.Database.DefinitionChecker");

		private static readonly MethodInfo ErrorMethod = (DefinitionCheckerType != null) ? AccessTools.Method(DefinitionCheckerType, "Error") : null;

		private static readonly HashSet<string> LoggedBypassDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private static MethodBase TargetMethod()
		{
			if (DefinitionCheckerType == null)
			{
				return null;
			}
			return AccessTools.Method(DefinitionCheckerType, "CheckHasFuelSlots", new Type[]
			{
				typeof(CarDefinition)
			});
		}

		private static bool Prefix(object __instance, CarDefinition definition)
		{
			if (!Main.Enabled || !OilFiringResolver.HasCustomSteamFuelSlot(definition))
			{
				return true;
			}
			string text = GetObjectIdentifier(__instance) ?? "<unknown>";
			if (LoggedBypassDefinitions.Add(text))
			{
				Main.Log("validation bypass applied for " + text + " using custom steam fuel.");
			}
			List<LoadSlot> loadSlots = definition.LoadSlots ?? new List<LoadSlot>();
			if (loadSlots.Count != 2)
			{
				Error(__instance, string.Format("Tender should have 2 slots, found {0}", loadSlots.Count));
				return false;
			}
			int num = loadSlots.FindIndex((LoadSlot slot) => OilFuelConstants.IsSteamFuelId(slot.RequiredLoadIdentifier));
			int num2 = loadSlots.FindIndex((LoadSlot slot) => string.Equals(slot.RequiredLoadIdentifier, OilFuelConstants.WaterLoadId, StringComparison.OrdinalIgnoreCase));
			Assert(__instance, num >= 0, "Must have coal, bunker-c, or pulpwood slot.");
			Assert(__instance, num2 >= 0, "Must have one water.");
			if (num >= 0 && string.Equals(loadSlots[num].RequiredLoadIdentifier, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase))
			{
				Assert(__instance, loadSlots[num].LoadUnits == LoadUnits.Gallons, "Slot requires bunker-c but units are not Gallons.");
			}
			if (num >= 0 && OilFuelConstants.IsWoodFuelId(loadSlots[num].RequiredLoadIdentifier))
			{
				Assert(__instance, loadSlots[num].LoadUnits == LoadUnits.Pounds, "Slot requires pulpwood but units are not Pounds.");
			}
			List<LoadTargetComponent> list = ((definition.Components != null) ? definition.Components.OfType<LoadTargetComponent>().ToList<LoadTargetComponent>() : new List<LoadTargetComponent>());
			if (num >= 0)
			{
				Assert(__instance, list.Count((LoadTargetComponent target) => target.SlotIndex == num) == 1, "Expected one LoadTarget for steam fuel");
			}
			if (num2 >= 0)
			{
				Assert(__instance, list.Count((LoadTargetComponent target) => target.SlotIndex == num2) == 1, "Expected one LoadTarget for water");
			}
			return false;
		}

		private static void Assert(object instance, bool condition, string message)
		{
			if (!condition)
			{
				Error(instance, message);
			}
		}

		private static void Error(object instance, string message)
		{
			if (ErrorMethod != null)
			{
				ErrorMethod.Invoke(instance, new object[]
				{
					message
				});
			}
		}

		private static string GetObjectIdentifier(object instance)
		{
			FieldInfo fieldInfo = (instance != null) ? AccessTools.Field(instance.GetType(), "_objectIdentifier") : null;
			return (fieldInfo != null) ? (fieldInfo.GetValue(instance) as string) : null;
		}
	}
}
