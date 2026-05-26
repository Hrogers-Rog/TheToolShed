using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;

namespace Toolshed.OilWoodFiring
{
	internal static class OilFiringResolver
	{
		private static readonly MethodInfo FuelCarMethod = AccessTools.Method(typeof(SteamLocomotive), "FuelCar");

		private static readonly HashSet<string> LoggedFuelEquipment = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private static readonly HashSet<string> LoggedFuelSlotMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private static readonly HashSet<string> LoggedFuelCarReflectionFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public static bool IsOilFiredSteamLocomotive(SteamLocomotive locomotive)
		{
			return UsesCustomSteamFuel(locomotive);
		}

		public static bool UsesCustomSteamFuel(SteamLocomotive locomotive)
		{
			if (locomotive == null)
			{
				return false;
			}
			string fuelLoadId;
			if (TryGetCustomSteamFuelLoadId(locomotive, true, out fuelLoadId))
			{
				return true;
			}
			Car fuelCar = GetFuelCar(locomotive);
			return fuelCar != null && TryGetCustomSteamFuelLoadId(fuelCar, true, out fuelLoadId);
		}

		public static bool IsOilFiredEquipment(Car car)
		{
			string fuelLoadId;
			return TryGetCustomSteamFuelLoadId(car, false, out fuelLoadId) &&
				string.Equals(fuelLoadId, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsWoodFiredEquipment(Car car)
		{
			string fuelLoadId;
			return TryGetCustomSteamFuelLoadId(car, false, out fuelLoadId) &&
				OilFuelConstants.IsWoodFuelId(fuelLoadId);
		}

		public static bool TryGetCustomSteamFuelLoadId(Car car, bool log, out string fuelLoadId)
		{
			fuelLoadId = null;
			if (!CanUseCustomSteamFuel(car))
			{
				return false;
			}
			if (TryGetDirectCustomFuelMarker(car, log, out fuelLoadId))
			{
				return true;
			}
			if (car.Definition.Archetype == CarArchetype.Tender && TryGetAttachedCustomSteamLocomotive(car, out SteamLocomotive steamLocomotive, out fuelLoadId))
			{
				if (log)
				{
					LogFuelEquipmentOnce(car, fuelLoadId, "attached to " + fuelLoadId + "-fired steam locomotive " + DescribeCar(steamLocomotive));
				}
				return true;
			}
			return false;
		}

		public static string GetSteamFuelLoadId(Car car)
		{
			string fuelLoadId;
			return TryGetCustomSteamFuelLoadId(car, true, out fuelLoadId) ? fuelLoadId : OilFuelConstants.CoalLoadId;
		}

		public static bool TryFindSteamFuelSlot(Car car, out int slotIndex, out LoadSlot slot, out string fallbackFuelLoadId)
		{
			slotIndex = -1;
			slot = null;
			fallbackFuelLoadId = OilFuelConstants.CoalLoadId;
			if (car == null || car.Definition == null || car.Definition.LoadSlots == null)
			{
				return false;
			}
			string steamFuelLoadId = GetSteamFuelLoadId(car);
			if (!string.Equals(steamFuelLoadId, OilFuelConstants.CoalLoadId, StringComparison.OrdinalIgnoreCase))
			{
				if (TryFindSlot(car, steamFuelLoadId, out slotIndex, out slot))
				{
					fallbackFuelLoadId = steamFuelLoadId;
					return true;
				}
				LogFuelSlotMissingOnce(car, "equipment is marked " + steamFuelLoadId + "-fired but no " + steamFuelLoadId + " slot exists; using coal fallback.");
			}
			return TryFindSlot(car, OilFuelConstants.CoalLoadId, out slotIndex, out slot);
		}

		public static string ResolveSteamFuelLoadId(CarLoadInfo? loadInfo, LoadSlot slot, string fallbackFuelLoadId)
		{
			// Definition-backed custom fuel slots should win over stale save data.
			// Existing cars that were saved before the load type changed can still
			// carry "coal" in a slot that is now explicitly pulpwood or bunker-c.
			if (slot != null && OilFuelConstants.IsCustomSteamFuelId(slot.RequiredLoadIdentifier))
			{
				return slot.RequiredLoadIdentifier;
			}
			if (loadInfo != null && OilFuelConstants.IsSteamFuelId(loadInfo.Value.LoadId))
			{
				return loadInfo.Value.LoadId;
			}
			if (slot != null && OilFuelConstants.IsSteamFuelId(slot.RequiredLoadIdentifier))
			{
				return slot.RequiredLoadIdentifier;
			}
			return fallbackFuelLoadId;
		}

		public static bool TryFindWaterSlot(Car car, out int slotIndex, out LoadSlot slot)
		{
			return TryFindSlot(car, OilFuelConstants.WaterLoadId, out slotIndex, out slot);
		}

		public static bool HasBunkerCFuelSlot(CarDefinition definition)
		{
			return definition != null && definition.LoadSlots != null && definition.LoadSlots.Exists((LoadSlot loadSlot) => string.Equals(loadSlot.RequiredLoadIdentifier, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase));
		}

		public static bool HasWoodFuelSlot(CarDefinition definition)
		{
			return definition != null && definition.LoadSlots != null && definition.LoadSlots.Exists((LoadSlot loadSlot) => OilFuelConstants.IsWoodFuelId(loadSlot.RequiredLoadIdentifier));
		}

		public static bool HasCustomSteamFuelSlot(CarDefinition definition)
		{
			return HasBunkerCFuelSlot(definition) || HasWoodFuelSlot(definition);
		}

		public static Car GetFuelCar(SteamLocomotive locomotive)
		{
			if (locomotive == null)
			{
				return null;
			}
			if (FuelCarMethod != null)
			{
				try
				{
					return FuelCarMethod.Invoke(locomotive, null) as Car;
				}
				catch (Exception ex)
				{
					if (!string.IsNullOrEmpty(locomotive.id) && LoggedFuelCarReflectionFailures.Add(locomotive.id))
					{
						Main.Warn("FuelCar reflection failed for " + DescribeCar(locomotive) + ": " + ex.Message);
					}
				}
			}
			if (!locomotive.hasTender)
			{
				return locomotive;
			}
			Car car;
			if (locomotive.TryGetAdjacentCar(Car.LogicalEnd.A, out car) && car != null && car.Definition.Archetype == CarArchetype.Tender)
			{
				return car;
			}
			if (locomotive.TryGetAdjacentCar(Car.LogicalEnd.B, out car) && car != null && car.Definition.Archetype == CarArchetype.Tender)
			{
				return car;
			}
			return null;
		}

		public static void LogFuelSlotMissingOnce(Car car, string reason)
		{
			if (car == null || string.IsNullOrEmpty(car.id))
			{
				return;
			}
			if (LoggedFuelSlotMissing.Add(car.id))
			{
				Main.Warn("steam fuel slot found/missing for " + DescribeCar(car) + ": " + reason);
			}
		}

		private static bool CanUseCustomSteamFuel(Car car)
		{
			return car != null && CanUseCustomSteamFuel(car.Definition);
		}

		private static bool CanUseCustomSteamFuel(CarDefinition definition)
		{
			if (definition == null)
			{
				return false;
			}
			CarArchetype archetype = definition.Archetype;
			return archetype == CarArchetype.LocomotiveSteam || archetype == CarArchetype.Tender;
		}

		private static bool TryGetDirectCustomFuelMarker(Car car, bool log, out string fuelLoadId)
		{
			fuelLoadId = null;
			if (!CanUseCustomSteamFuel(car))
			{
				return false;
			}
			if (HasBunkerCFuelSlot(car.Definition))
			{
				fuelLoadId = OilFuelConstants.BunkerCLoadId;
				if (log)
				{
					LogFuelEquipmentOnce(car, fuelLoadId, "definition uses bunker-c");
				}
				return true;
			}
			if (HasWoodFuelSlot(car.Definition))
			{
				fuelLoadId = OilFuelConstants.WoodLoadId;
				if (log)
				{
					LogFuelEquipmentOnce(car, fuelLoadId, "definition uses pulpwood");
				}
				return true;
			}
			return false;
		}

		private static bool TryGetAttachedCustomSteamLocomotive(Car tender, out SteamLocomotive locomotive, out string fuelLoadId)
		{
			locomotive = null;
			fuelLoadId = null;
			if (tender == null)
			{
				return false;
			}
			Car car;
			if (tender.TryGetAdjacentCar(Car.LogicalEnd.A, out car) && IsConfiguredOrDefinitionCustomSteamLocomotive(car, out locomotive, out fuelLoadId))
			{
				return true;
			}
			if (tender.TryGetAdjacentCar(Car.LogicalEnd.B, out car) && IsConfiguredOrDefinitionCustomSteamLocomotive(car, out locomotive, out fuelLoadId))
			{
				return true;
			}
			return false;
		}

		private static bool IsConfiguredOrDefinitionCustomSteamLocomotive(Car car, out SteamLocomotive locomotive, out string fuelLoadId)
		{
			locomotive = car as SteamLocomotive;
			fuelLoadId = null;
			return locomotive != null && TryGetDirectCustomFuelMarker(locomotive, false, out fuelLoadId);
		}

		private static int FindSlotIndexForRequiredLoad(List<LoadSlot> loadSlots, string loadId)
		{
			if (loadSlots == null)
			{
				return -1;
			}
			return loadSlots.FindIndex((LoadSlot slot) => string.Equals(slot.RequiredLoadIdentifier, loadId, StringComparison.OrdinalIgnoreCase));
		}

		private static int FindSlotIndexForActualLoad(Car car, string loadId)
		{
			if (car == null || car.Definition == null || car.Definition.LoadSlots == null)
			{
				return -1;
			}
			for (int i = 0; i < car.Definition.LoadSlots.Count; i++)
			{
				CarLoadInfo? loadInfo = car.GetLoadInfo(i);
				if (loadInfo != null && string.Equals(loadInfo.Value.LoadId, loadId, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}
			return -1;
		}

		private static bool TryFindSlot(Car car, string loadId, out int slotIndex, out LoadSlot slot)
		{
			slotIndex = -1;
			slot = null;
			if (car == null || car.Definition == null || car.Definition.LoadSlots == null)
			{
				return false;
			}
			slotIndex = FindSlotIndexForActualLoad(car, loadId);
			if (slotIndex < 0)
			{
				slotIndex = FindSlotIndexForRequiredLoad(car.Definition.LoadSlots, loadId);
			}
			if (slotIndex < 0)
			{
				return false;
			}
			slot = car.Definition.LoadSlots[slotIndex];
			return true;
		}

		private static string GetDefinitionId(Car car)
		{
			TypedContainerItem<CarDefinition> definitionInfo = car.DefinitionInfo;
			return (definitionInfo != null) ? definitionInfo.Identifier : null;
		}

		private static void LogFuelEquipmentOnce(Car car, string fuelLoadId, string reason)
		{
			if (car == null || string.IsNullOrEmpty(car.id))
			{
				return;
			}
			if (LoggedFuelEquipment.Add(car.id))
			{
				Main.Log("equipment identified as " + fuelLoadId + "-fired: " + DescribeCar(car) + " (" + reason + ")");
			}
		}

		private static string DescribeCar(Car car)
		{
			if (car == null)
			{
				return "<null>";
			}
			return car.DisplayName + " [id=" + car.id + ", def=" + GetDefinitionId(car) + "]";
		}
	}
}
