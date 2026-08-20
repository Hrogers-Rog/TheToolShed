using System;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using Model.Ops;
using RollingStock;
using UnityEngine;

namespace Toolshed.OilWoodFiring
{
	internal static class OilStandLoaderGate
	{
		/// <summary>
		/// Oil-fired steam tenders need the fuel-slot matching in this file to be fueled.
		/// The water standpipe earns it by being the registered dual-service loader; any
		/// other loader (e.g. a diesel/bunker-c service stand) earns it by dispensing bunker-c.
		/// </summary>
		internal static bool ShouldServeBunkerC(CarLoadTargetLoader loader)
		{
			if (loader == null)
			{
				return false;
			}
			if (OilLoaderStandService.IsDualServiceLoader(loader))
			{
				return true;
			}
			return loader.load != null &&
				string.Equals(loader.load.id, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase);
		}
	}

	[HarmonyPatch(typeof(CarLoadTargetLoader), "LoadSlotFromCar")]
	internal static class OilStandCarLoadTargetLoaderLoadSlotFromCarPatch
	{
		private static void Postfix(CarLoadTargetLoader __instance, Car car, Vector3 point, ref int slotIndex, ref LoadSlot __result)
		{
			if (!Main.Enabled || __instance == null || car == null || !OilStandLoaderGate.ShouldServeBunkerC(__instance))
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
			if (!Main.Enabled || __instance == null || car == null || loadSlot == null || !OilStandLoaderGate.ShouldServeBunkerC(__instance))
			{
				return true;
			}

			if (!string.Equals(loadSlot.RequiredLoadIdentifier, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			CarLoadInfo? loadInfo = car.GetLoadInfo(slotIndex);
			CarLoadInfo currentLoad = loadInfo ?? new CarLoadInfo(OilFuelConstants.BunkerCLoadId, 0f);
			// Rate is expressed per real second. Dividing by frame delta made a
			// nominal 60 gal/s stand transfer thousands of gallons every physics
			// tick and could empty its source storage almost instantly.
			float addedQuantity = Mathf.Clamp(
				OilFuelConstants.BunkerCLoadingRateGallonsPerSecond * Mathf.Max(dt, 0f),
				0f,
				loadSlot.MaximumCapacity - currentLoad.Quantity);
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
