using System;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using Model.Ops;
using RollingStock;
using UnityEngine;

namespace Toolshed.OilWoodFiring
{
	[HarmonyPatch(typeof(CarLoadTargetLoader), "LoadSlotFromCar")]
	internal static class OilStandCarLoadTargetLoaderLoadSlotFromCarPatch
	{
		private static void Postfix(CarLoadTargetLoader __instance, Car car, Vector3 point, ref int slotIndex, ref LoadSlot __result)
		{
			if (!Main.Enabled || __instance == null || car == null || !OilLoaderStandService.IsDualServiceLoader(__instance))
			{
				return;
			}

			int bunkerCSlotIndex;
			LoadSlot bunkerCSlot;
			string fallbackFuelLoadId;
			if (!OilFiringResolver.TryFindSteamFuelSlot(car, out bunkerCSlotIndex, out bunkerCSlot, out fallbackFuelLoadId) ||
				bunkerCSlot == null ||
				!string.Equals(bunkerCSlot.RequiredLoadIdentifier, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			if (!TryMatchLoadTarget(__instance, car, point, bunkerCSlotIndex))
			{
				return;
			}

			slotIndex = bunkerCSlotIndex;
			__result = bunkerCSlot;
		}

		private static bool TryMatchLoadTarget(CarLoadTargetLoader loader, Car car, Vector3 position, int desiredSlotIndex)
		{
			TrainController shared = TrainController.Shared;
			if (loader == null || car == null || shared == null || shared.graph == null)
			{
				return false;
			}

			Matrix4x4 matrix = car.GetTransformMatrix(shared.graph);
			CarLoadTarget[] targets = car.GetComponentsInChildren<CarLoadTarget>();
			for (int i = 0; i < targets.Length; i++)
			{
				CarLoadTarget target = targets[i];
				if (target == null || target.slotIndex != desiredSlotIndex)
				{
					continue;
				}

				Vector3 localPoint = car.transform.InverseTransformPoint(target.transform.position);
				Vector3 worldPoint = matrix.MultiplyPoint3x4(localPoint);
				if (FlatDistance(position, worldPoint) <= target.radius + loader.radius)
				{
					return true;
				}
			}

			return false;
		}

		private static float FlatDistance(Vector3 a, Vector3 b)
		{
			a.y = 0f;
			b.y = 0f;
			return Vector3.Distance(a, b);
		}
	}

	[HarmonyPatch(typeof(CarLoadTargetLoader), "Load")]
	internal static class OilStandCarLoadTargetLoaderLoadPatch
	{
		private static bool Prefix(CarLoadTargetLoader __instance, Car car, LoadSlot loadSlot, int slotIndex, float dt)
		{
			if (!Main.Enabled || __instance == null || car == null || loadSlot == null || !OilLoaderStandService.IsDualServiceLoader(__instance))
			{
				return true;
			}

			if (!string.Equals(loadSlot.RequiredLoadIdentifier, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			CarLoadInfo? loadInfo = car.GetLoadInfo(slotIndex);
			CarLoadInfo currentLoad = loadInfo ?? new CarLoadInfo(OilFuelConstants.BunkerCLoadId, 0f);
			float addedQuantity = Mathf.Clamp(OilFuelConstants.BunkerCLoadingRateGallonsPerSecond / dt, 0f, loadSlot.MaximumCapacity - currentLoad.Quantity);
			if (__instance.sourceIndustry != null)
			{
				Model.Ops.Definition.Load bunkerCLoad = OilFuelRegistry.GetOrCreateBunkerCLoad();
				addedQuantity = __instance.sourceIndustry.Storage.RemoveFromStorage(bunkerCLoad, addedQuantity, null);
			}

			car.SetLoadInfo(slotIndex, new CarLoadInfo?(new CarLoadInfo(OilFuelConstants.BunkerCLoadId, currentLoad.Quantity + addedQuantity)));
			return false;
		}
	}
}
