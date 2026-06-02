using System;
using System.Collections.Generic;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using RollingStock;
using Track;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	[HarmonyPatch(typeof(CarLoadTargetLoader), "Load")]
	internal static class CarLoadTargetLoaderUniversalServiceDebugPatch
	{
		private static bool Prefix(CarLoadTargetLoader __instance, Car car, int slotIndex, out float __state)
		{
			__state = 0f;
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				if (!facility.CanLoadSlotSafely(car, slotIndex))
				{
					return false;
				}
				__state = UniversalServiceFacilityComponent.QuantityInSlot(car, slotIndex);
				return facility.CanVanillaLoaderTransfer();
			}
			__state = UniversalServiceFacilityComponent.QuantityInSlot(car, slotIndex);
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
		private static bool Prefix(CarLoadTargetLoader __instance, Car car, Vector3 point, ref int slotIndex, ref LoadSlot __result)
		{
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				facility.LogVanillaLoadSlotProbe(__instance, car, point);
				try
				{
					__result = facility.ResolveLoadTargetSafely(__instance, car, point, out slotIndex);
					return false;
				}
				catch (System.Exception ex)
				{
					Main.Warn("[ServiceFacility][Loader] custom load-target scan failed; falling back to vanilla scan: " +
						ex.GetType().Name + " - " + ex.Message);
				}
			}
			return true;
		}

		[HarmonyPriority(Priority.Last)]
		private static void Postfix(CarLoadTargetLoader __instance, Car car, Vector3 point, ref int slotIndex, ref LoadSlot __result)
		{
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				try
				{
					if (__result != null && !facility.AllowVanillaLoadTarget(__instance, car, point, __result, slotIndex))
					{
						__result = null;
						slotIndex = -1;
					}
					facility.LogLoadTargetResult(__instance, car, __result, slotIndex);
				}
				catch (System.Exception ex)
				{
					__result = null;
					slotIndex = -1;
					Main.Warn("[ServiceFacility][Loader] custom load-target result check failed: " +
						ex.GetType().Name + " - " + ex.Message);
				}
			}
		}
	}

	[HarmonyPatch(typeof(CarLoadTargetLoader), "SetLoading")]
	internal static class CarLoadTargetLoaderUniversalServiceSetLoadingProbePatch
	{
		private static void Prefix(CarLoadTargetLoader __instance, bool loading)
		{
			UniversalServiceFacilityComponent facility;
			if (UniversalServiceFacilityComponent.TryGetForLoader(__instance, out facility))
			{
				facility.LogVanillaSetLoading(__instance, loading);
			}
		}
	}

	[HarmonyPatch(typeof(TrainController), "CheckForCarsAtPoint")]
	internal static class TrainControllerUniversalServiceCarScanProbePatch
	{
		private static void Prefix(Vector3 point, float radius, HashSet<Car> foundCars, Location? sameRouteRequirement, out bool __state)
		{
			__state = false;
			string description;
			if (UniversalServiceFacilityComponent.TryBuildCarScanProbe(point, radius, foundCars, "begin", out description))
			{
				__state = true;
				Main.Log("[ServiceFacility][LoaderProbe] CheckForCarsAtPoint begin " + description +
					", sameRoute=" + (sameRouteRequirement.HasValue ? sameRouteRequirement.Value.segment != null ? sameRouteRequirement.Value.segment.id : "<null-segment>" : "<none>"));
			}
		}

		private static void Postfix(Vector3 point, float radius, HashSet<Car> foundCars, bool __state)
		{
			if (!__state)
			{
				return;
			}

			string description;
			if (UniversalServiceFacilityComponent.TryBuildCarScanProbe(point, radius, foundCars, "end", out description))
			{
				Main.Log("[ServiceFacility][LoaderProbe] CheckForCarsAtPoint end " + description);
			}
		}

		private static Exception Finalizer(Exception __exception, Vector3 point, float radius, HashSet<Car> foundCars, bool __state)
		{
			if (__exception == null)
			{
				return null;
			}

			string description = null;
			if (__state || UniversalServiceFacilityComponent.TryBuildCarScanProbe(point, radius, foundCars, "exception", out description))
			{
				Main.Warn("[ServiceFacility][LoaderProbe] CheckForCarsAtPoint exception " +
					__exception.GetType().Name + " - " + __exception.Message +
					(description != null ? " :: " + description : ""));
			}
			return __exception;
		}
	}
}
