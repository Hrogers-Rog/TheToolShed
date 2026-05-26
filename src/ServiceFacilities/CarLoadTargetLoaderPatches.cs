using HarmonyLib;
using Model;
using Model.Definition.Data;
using RollingStock;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	[HarmonyPatch(typeof(CarLoadTargetLoader), "Load")]
	internal static class CarLoadTargetLoaderUniversalServiceDebugPatch
	{
		private static bool Prefix(CarLoadTargetLoader __instance, Car car, int slotIndex, out float __state)
		{
			__state = UniversalServiceFacilityComponent.QuantityInSlot(car, slotIndex);
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				return facility.CanVanillaLoaderTransfer();
			}
			return true;
		}

		private static void Postfix(CarLoadTargetLoader __instance, Car car, LoadSlot loadSlot, int slotIndex, float __state)
		{
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				facility.LogTransfer(__instance, car, loadSlot, slotIndex, __state);
			}
		}
	}

	[HarmonyPatch(typeof(CarLoadTargetLoader), "LoadSlotFromCar")]
	internal static class CarLoadTargetLoaderUniversalServiceMatchPatch
	{
		private static bool Prefix(CarLoadTargetLoader __instance, Car car, Vector3 point)
		{
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				facility.LogLoadTargetScan(__instance, car, point);
			}
			return true;
		}

		private static void Postfix(CarLoadTargetLoader __instance, Car car, ref int slotIndex, LoadSlot __result)
		{
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				facility.LogLoadTargetResult(__instance, car, __result, slotIndex);
			}
		}
	}
}
