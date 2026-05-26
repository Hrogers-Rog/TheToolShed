using System;
using System.Collections.Generic;
using HarmonyLib;
using Model.Ops;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Railroader's stock IndustryUnloader panel assumes a non-importable unload-only commodity is consumed by the
	/// industry, so a zero-rate storage bin can show "Consumes ... @ 0 cars/day". Service facilities use the unloader
	/// purely as a delivery/storage point, so suppress that misleading row when the component is storage-only.
	/// </summary>
	[HarmonyPatch(typeof(IndustryUnloader), nameof(IndustryUnloader.PanelFields))]
	internal static class StorageOnlyIndustryUnloaderPanelPatch
	{
		private const float Epsilon = 0.001f;

		private static void Postfix(IndustryUnloader __instance, ref IEnumerable<IndustryComponent.PanelField> __result)
		{
			if (!ShouldHideConsumesRow(__instance))
			{
				return;
			}

			__result = WithoutConsumesRow(__result);
		}

		private static bool ShouldHideConsumesRow(IndustryUnloader unloader)
		{
			if (unloader == null || unloader.load == null)
			{
				return false;
			}
			if (unloader.orderLoads || unloader.storageConsumptionRate > Epsilon)
			{
				return false;
			}
			if (unloader.load.importable)
			{
				return false;
			}

			FormulaicIndustryComponent formulaic = unloader.Industry != null
				? unloader.Industry.GetComponent<FormulaicIndustryComponent>()
				: null;
			if (formulaic == null)
			{
				return true;
			}

			for (int i = 0; i < formulaic.inputTerms.Count; i++)
			{
				FormulaicIndustryComponent.Term term = formulaic.inputTerms[i];
				if (term != null && term.load == unloader.load && term.unitsPerDay > Epsilon)
				{
					return false;
				}
			}

			return true;
		}

		private static IEnumerable<IndustryComponent.PanelField> WithoutConsumesRow(IEnumerable<IndustryComponent.PanelField> fields)
		{
			foreach (IndustryComponent.PanelField field in fields)
			{
				if (!string.Equals(field.Label, "Consumes", StringComparison.OrdinalIgnoreCase))
				{
					yield return field;
				}
			}
		}
	}

	/// <summary>
	/// Service-facility storage is railroad-owned fuel inventory, not a paying industry delivery.
	/// Base-game pulpwood has a delivery payout, so a storage-only wood shed would otherwise pay the
	/// player for stocking their own engine-service fuel pile.
	/// </summary>
	[HarmonyPatch(typeof(IndustryUnloader), nameof(IndustryUnloader.DailyReceivables))]
	internal static class StorageOnlyIndustryUnloaderRevenuePatch
	{
		private const float Epsilon = 0.001f;

		private static bool Prefix(IndustryUnloader __instance, IIndustryContext ctx)
		{
			if (!ShouldSuppressRevenue(__instance))
			{
				return true;
			}

			if (ctx != null && __instance.load != null)
			{
				ctx.CounterClear("unloaded-total-" + __instance.load.id);
			}
			return false;
		}

		private static bool ShouldSuppressRevenue(IndustryUnloader unloader)
		{
			if (unloader == null || unloader.load == null || unloader.load.payPerQuantity <= 0f)
			{
				return false;
			}
			if (unloader.orderLoads || unloader.storageConsumptionRate > Epsilon)
			{
				return false;
			}

			Industry industry = unloader.Industry;
			if (UniversalServiceFacilityComponent.HasFiniteStorageFacilityFor(industry, unloader.load))
			{
				return true;
			}

			string identifier = unloader.Identifier ?? string.Empty;
			string displayName = unloader.DisplayName ?? string.Empty;
			return identifier.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0 &&
				displayName.IndexOf("loader", StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}
}
