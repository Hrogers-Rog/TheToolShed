using System;
using System.Collections.Generic;
using Game.State;
using HarmonyLib;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Physics;
using UnityEngine;

namespace Toolshed.OilWoodFiring
{
	internal static class OilConsumptionService
	{
		private static readonly Dictionary<string, float> LastConsumptionLogTimes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		private static readonly System.Reflection.MethodInfo PeriodicUpdateForMuMethod = AccessTools.Method(typeof(BaseLocomotive), "PeriodicUpdateForMu");

		private static readonly System.Reflection.MethodInfo InvokeHasFuelDidChangeMethod = AccessTools.Method(typeof(BaseLocomotive), "InvokeHasFuelDidChange");

		public static float ConvertCoalLbsToBunkerCGallons(float coalLbsPerSecond)
		{
			return coalLbsPerSecond * OilFuelConstants.BunkerCGallonsPerCoalPound;
		}

		public static float ConvertCoalLbsToWoodPounds(float coalLbsPerSecond)
		{
			return coalLbsPerSecond * OilFuelConstants.WoodPoundsPerCoalPound;
		}

		public static bool RunCustomFuelPeriodicUpdateSafely(SteamLocomotive locomotive, float dt)
		{
			try
			{
				RunCustomFuelPeriodicUpdate(locomotive, dt);
			}
			catch (Exception ex)
			{
				Main.Error("Custom steam fuel periodic update failed for " + ((locomotive != null) ? locomotive.id : "<null>") + "; skipping custom steam update for this tick. " + ex);
			}
			return false;
		}

		private static void RunCustomFuelPeriodicUpdate(SteamLocomotive locomotive, float dt)
		{
			InvokeBasePeriodicUpdate(locomotive);
			if (locomotive == null || locomotive.engine == null)
			{
				return;
			}
			Car fuelCar = OilFiringResolver.GetFuelCar(locomotive);
			if (fuelCar == null)
			{
				OilFiringResolver.LogFuelSlotMissingOnce(locomotive, "fuel car could not be resolved.");
				UpdateHasFuelState(locomotive, false);
				return;
			}
			int fuelSlotIndex;
			LoadSlot fuelSlot;
			string fallbackFuelLoadId;
			if (!OilFiringResolver.TryFindSteamFuelSlot(fuelCar, out fuelSlotIndex, out fuelSlot, out fallbackFuelLoadId))
			{
				OilFiringResolver.LogFuelSlotMissingOnce(fuelCar, "no steam fuel slot was found.");
				UpdateHasFuelState(locomotive, false);
				return;
			}
			int waterSlotIndex;
			LoadSlot waterSlot;
			if (!OilFiringResolver.TryFindWaterSlot(fuelCar, out waterSlotIndex, out waterSlot))
			{
				OilFiringResolver.LogFuelSlotMissingOnce(fuelCar, "no water slot was found.");
				UpdateHasFuelState(locomotive, false);
				return;
			}
			CarLoadInfo? loadInfo = fuelCar.GetLoadInfo(fuelSlotIndex);
			string currentFuelLoadId = OilFiringResolver.ResolveSteamFuelLoadId(loadInfo, fuelSlot, fallbackFuelLoadId);
			float num = (loadInfo != null) ? loadInfo.GetValueOrDefault().Quantity : 0f;
			NormalizeFuelLoadInfoIfNeeded(fuelCar, fuelSlotIndex, loadInfo, currentFuelLoadId, num);
			CarLoadInfo? loadInfo2 = fuelCar.GetLoadInfo(waterSlotIndex);
			float num2 = (loadInfo2 != null) ? loadInfo2.GetValueOrDefault().Quantity : 0f;
			bool flag = num > OilFuelConstants.LoadPresentThreshold && num2 > OilFuelConstants.LoadPresentThreshold;
			float num3 = ConvertCoalConsumptionToFuelQuantity(locomotive.engine.CoalConsumptionRate, currentFuelLoadId) * dt;
			float num4 = locomotive.engine.WaterConsumptionRate * dt;
			if (StateManager.IsHost && flag && (num3 > OilFuelConstants.LoadPresentThreshold || num4 > OilFuelConstants.LoadPresentThreshold))
			{
				num = Mathf.Max(0f, num - num3);
				num2 = Mathf.Max(0f, num2 - num4);
				fuelCar.SetLoadInfo(fuelSlotIndex, new CarLoadInfo?(new CarLoadInfo(currentFuelLoadId, num)));
				fuelCar.SetLoadInfo(waterSlotIndex, new CarLoadInfo?(new CarLoadInfo(OilFuelConstants.WaterLoadId, num2)));
				if (!string.Equals(currentFuelLoadId, OilFuelConstants.CoalLoadId, StringComparison.OrdinalIgnoreCase))
				{
					LogConsumptionIfNeeded(locomotive, currentFuelLoadId, num3, num, num2);
				}
			}
			UpdateHasFuelState(locomotive, num > OilFuelConstants.LoadPresentThreshold && num2 > OilFuelConstants.LoadPresentThreshold);
		}

		private static void NormalizeFuelLoadInfoIfNeeded(Car fuelCar, int fuelSlotIndex, CarLoadInfo? loadInfo, string expectedFuelLoadId, float quantity)
		{
			if (!StateManager.IsHost || fuelCar == null || loadInfo == null || string.IsNullOrEmpty(expectedFuelLoadId))
			{
				return;
			}
			string savedLoadId = loadInfo.Value.LoadId;
			if (string.Equals(savedLoadId, expectedFuelLoadId, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			fuelCar.SetLoadInfo(fuelSlotIndex, new CarLoadInfo?(new CarLoadInfo(expectedFuelLoadId, quantity)));
			Main.Log(string.Format("normalized saved steam fuel load on {0}: {1} -> {2}, quantity {3:F1}", fuelCar.DisplayName, savedLoadId, expectedFuelLoadId, quantity));
		}

		private static void UpdateHasFuelState(SteamLocomotive locomotive, bool hasFuel)
		{
			bool hasWaterAndCoal = locomotive.engine.HasWaterAndCoal;
			locomotive.engine.HasWaterAndCoal = hasFuel;
			if (hasWaterAndCoal == hasFuel)
			{
				return;
			}
			if (InvokeHasFuelDidChangeMethod != null)
			{
				InvokeHasFuelDidChangeMethod.Invoke(locomotive, null);
			}
			LocomotiveAirSystem component = locomotive.GetComponent<LocomotiveAirSystem>();
			if (component != null)
			{
				component.HasFuel = hasFuel;
			}
		}

		private static void InvokeBasePeriodicUpdate(BaseLocomotive locomotive)
		{
			if (PeriodicUpdateForMuMethod != null)
			{
				PeriodicUpdateForMuMethod.Invoke(locomotive, null);
			}
		}

		private static float ConvertCoalConsumptionToFuelQuantity(float coalLbsPerSecond, string fuelLoadId)
		{
			if (string.Equals(fuelLoadId, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase))
			{
				return ConvertCoalLbsToBunkerCGallons(coalLbsPerSecond);
			}
			if (OilFuelConstants.IsWoodFuelId(fuelLoadId))
			{
				return ConvertCoalLbsToWoodPounds(coalLbsPerSecond);
			}
			return coalLbsPerSecond;
		}

		private static void LogConsumptionIfNeeded(SteamLocomotive locomotive, string fuelLoadId, float fuelConsumed, float fuelRemaining, float waterRemaining)
		{
			if (locomotive == null || string.IsNullOrEmpty(locomotive.id))
			{
				return;
			}
			float unscaledTime = Time.unscaledTime;
			float num;
			if (LastConsumptionLogTimes.TryGetValue(locomotive.id, out num) && unscaledTime - num < 5f)
			{
				return;
			}
			LastConsumptionLogTimes[locomotive.id] = unscaledTime;
			Main.Log(string.Format("custom steam fuel consumption applied to {0}: drained {1:F3} {2}, remaining fuel {3:F1}, remaining water {4:F1}", locomotive.DisplayName, fuelConsumed, fuelLoadId, fuelRemaining, waterRemaining));
		}
	}
}
