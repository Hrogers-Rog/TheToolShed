using System;
using System.Collections.Generic;
using HarmonyLib;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using Track;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Debug-only tracing for the vanilla IndustryUnloader path used by service facilities.
	/// It keeps unloading behavior vanilla, but tells us exactly which vanilla gate is rejecting a supply car.
	/// </summary>
	[HarmonyPatch(typeof(IndustryUnloader), nameof(IndustryUnloader.Service))]
	internal static class ServiceFacilityIndustryUnloaderDiagnosticsPatch
	{
		private const float LogIntervalSeconds = 5f;

		private static readonly Dictionary<string, float> NextLogByUnloader = new Dictionary<string, float>();

		private static void Prefix(IndustryUnloader __instance, IIndustryContext ctx, out float __state)
		{
			__state = float.NaN;
			RefreshStaleWaybillDestinations(__instance, ctx);
			if (!ShouldTrace(__instance, ctx, out UniversalServiceFacilityComponent facility))
			{
				return;
			}

			__state = ctx.QuantityInStorage(__instance.load);
			string key = DebugKey(__instance);
			float nextLog;
			if (NextLogByUnloader.TryGetValue(key, out nextLog) && Time.unscaledTime < nextLog)
			{
				return;
			}
			NextLogByUnloader[key] = Time.unscaledTime + LogIntervalSeconds;

			LogSnapshot("begin", __instance, ctx, facility, __state);
		}

		private static void Postfix(IndustryUnloader __instance, IIndustryContext ctx, float __state)
		{
			if (float.IsNaN(__state) || !ShouldTrace(__instance, ctx, out UniversalServiceFacilityComponent facility))
			{
				return;
			}

			float after = ctx.QuantityInStorage(__instance.load);
			if (Mathf.Abs(after - __state) >= __instance.load.ZeroThreshold)
			{
				Main.Log("[ServiceFacility][UnloaderProbe] unloaded into " + __instance.Identifier +
					" load=" + __instance.load.id +
					", storage " + FormatQuantity(__state) + " -> " + FormatQuantity(after) +
					", delta=" + FormatQuantity(after - __state) +
					", facility=" + facility.name);
			}
		}

		private static bool ShouldTrace(IndustryUnloader unloader, IIndustryContext ctx, out UniversalServiceFacilityComponent facility)
		{
			facility = null;
			return unloader != null &&
				ctx != null &&
				unloader.load != null &&
				UniversalServiceFacilityComponent.TryGetDebugFacilityForUnloader(unloader, out facility);
		}

		private static void RefreshStaleWaybillDestinations(IndustryUnloader unloader, IIndustryContext ctx)
		{
			if (unloader == null || ctx == null || unloader.load == null ||
				!UniversalServiceFacilityComponent.TryGetFacilityForUnloader(unloader, out UniversalServiceFacilityComponent facility))
			{
				return;
			}

			try
			{
				foreach (IOpsCar car in ctx.CarsAtPosition())
				{
					if (car == null)
					{
						continue;
					}
					Waybill? waybill = car.Waybill;
					if (waybill == null)
					{
						continue;
					}
					Waybill value = waybill.Value;
					if (value.Destination.Equals(unloader) ||
						!string.Equals(value.Destination.Identifier, unloader.Identifier, StringComparison.Ordinal))
					{
						continue;
					}

					value.Destination = unloader;
					car.SetWaybill(new Waybill?(value), unloader, "Toolshed service facility destination refresh");
					if (facility.debugLogging)
					{
						Main.Log("[ServiceFacility][UnloaderProbe] refreshed stale waybill destination for " +
							car.DisplayName +
							" to " + unloader.DisplayName + "/" + unloader.Identifier +
							" load=" + unloader.load.id);
					}
				}
			}
			catch (Exception ex)
			{
				if (facility.debugLogging)
				{
					Main.Warn("[ServiceFacility][UnloaderProbe] stale waybill refresh failed for " +
						unloader.Identifier + ": " + ex.GetType().Name + " - " + ex.Message);
				}
			}
		}

		private static void LogSnapshot(string phase, IndustryUnloader unloader, IIndustryContext ctx, UniversalServiceFacilityComponent facility, float storage)
		{
			List<IOpsCar> cars = new List<IOpsCar>();
			try
			{
				foreach (IOpsCar car in ctx.CarsAtPosition())
				{
					cars.Add(car);
				}
			}
			catch (Exception ex)
			{
				Main.Warn("[ServiceFacility][UnloaderProbe] failed to enumerate cars for " + unloader.Identifier +
					": " + ex.GetType().Name + " - " + ex.Message);
			}

			float maxStorage = unloader.maxStorage * SafeContractMultiplier(unloader);
			float freeStorage = Mathf.Max(0f, maxStorage - storage);
			Main.Log("[ServiceFacility][UnloaderProbe] " + phase +
				" id=" + unloader.Identifier +
				", name=" + unloader.DisplayName +
				", load=" + unloader.load.id +
				", storage=" + FormatQuantity(storage) + "/" + FormatQuantity(maxStorage) +
				", free=" + FormatQuantity(freeStorage) +
				", unloadRate=" + FormatQuantity(unloader.carUnloadRate) +
				", carTypeFilter=" + FilterText(unloader.carTypeFilter) +
				", spans=" + SpanText(unloader.trackSpans) +
				", ctxCars=" + cars.Count +
				", facility=" + facility.name);

			for (int i = 0; i < cars.Count; i++)
			{
				LogCar(unloader, cars[i]);
			}
		}

		private static void LogCar(IndustryUnloader unloader, IOpsCar car)
		{
			if (car == null)
			{
				return;
			}

			bool typeMatches = unloader.carTypeFilter != null && unloader.carTypeFilter.Matches(car.CarType);
			bool loadCompatible = car.IsEmptyOrContains(unloader.load);
			ValueTuple<float, float> quantity = car.QuantityOfLoad(unloader.load);
			Waybill? waybill = car.Waybill;
			bool hasWaybill = waybill != null;
			bool destinationEquals = hasWaybill && waybill.Value.Destination.Equals(unloader);
			string destination = hasWaybill
				? waybill.Value.Destination.DisplayName + "/" + waybill.Value.Destination.Identifier
				: "<none>";
			Car modelCar = TrainController.Shared != null ? TrainController.Shared.CarForId(car.Id) : null;
			string velocity = modelCar != null ? modelCar.velocity.ToString("0.###") : "<unknown>";

			Main.Log("[ServiceFacility][UnloaderProbe] car=" + car.DisplayName +
				", id=" + car.Id +
				", type=" + car.CarType +
				", typeMatches=" + typeMatches +
				", loadCompatible=" + loadCompatible +
				", qty=" + FormatQuantity(quantity.Item1) + "/" + FormatQuantity(quantity.Item2) +
				", waybill=" + hasWaybill +
				", destEquals=" + destinationEquals +
				", dest=" + destination +
				", velocity=" + velocity +
				", owned=" + car.IsOwnedByPlayer);
		}

		private static float SafeContractMultiplier(IndustryUnloader unloader)
		{
			try
			{
				return unloader.Industry != null ? unloader.Industry.GetContractMultiplier() : 1f;
			}
			catch
			{
				return 1f;
			}
		}

		private static string SpanText(TrackSpan[] spans)
		{
			if (spans == null || spans.Length == 0)
			{
				return "<none>";
			}

			string[] pieces = new string[spans.Length];
			for (int i = 0; i < spans.Length; i++)
			{
				TrackSpan span = spans[i];
				pieces[i] = span != null
					? span.id + ":" + (span.IsValid ? "valid" : "invalid")
					: "<null>";
			}
			return string.Join(",", pieces);
		}

		private static string FilterText(CarTypeFilter filter)
		{
			return filter != null ? filter.ToString() : "<null>";
		}

		private static string DebugKey(IndustryUnloader unloader)
		{
			return unloader.Identifier ?? unloader.DisplayName ?? unloader.GetInstanceID().ToString();
		}

		private static string FormatQuantity(float value)
		{
			return value.ToString("0.###");
		}
	}
}
