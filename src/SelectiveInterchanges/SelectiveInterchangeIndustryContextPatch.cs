using System;
using System.Collections.Generic;
using System.Reflection;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using UnityEngine;

namespace Toolshed.SelectiveInterchanges
{
	[HarmonyPatch(typeof(IndustryContext), "CreateCarDescriptorForOrder")]
	internal static class SelectiveInterchangeIndustryContextPatch
	{
		private static readonly FieldInfo ControllerField = AccessTools.Field(typeof(IndustryContext), "_controller");
		private static readonly FieldInfo IndustryComponentField = AccessTools.Field(typeof(IndustryContext), "_industryComponent");

		private static bool Prefix(IndustryContext __instance, Order order, IPrefabStore prefabStore, System.Random rnd, ref CarDescriptor __result)
		{
			try
			{
				TypedContainerItem<CarDefinition> definitionInfo;
				if (!SelectiveInterchangeCarModelRegistry.TrySelect(order.Tag, prefabStore, order.CarTypeFilter, order.Load, rnd, out definitionInfo))
				{
					return true;
				}

				__result = CreateCarDescriptor(__instance, order, definitionInfo, rnd);
				return false;
			}
			catch (Exception ex)
			{
				Main.Warn("[SelectiveInterchange] Specific car model selection failed for order tag '" + order.Tag + "': " + ex.Message + ". Falling back to Railroader's normal car selection.");
				return true;
			}
		}

		private static CarDescriptor CreateCarDescriptor(IndustryContext context, Order order, TypedContainerItem<CarDefinition> definitionInfo, System.Random rnd)
		{
			OpsController controller = ControllerField != null ? ControllerField.GetValue(context) as OpsController : null;
			IndustryComponent industryComponent = IndustryComponentField != null ? IndustryComponentField.GetValue(context) as IndustryComponent : null;
			if (controller == null || industryComponent == null)
			{
				throw new InvalidOperationException("Could not access IndustryContext order origin.");
			}

			List<LoadSlot> loadSlots = definitionInfo.Definition.LoadSlots;
			if (loadSlots == null || loadSlots.Count == 0)
			{
				throw new InvalidOperationException("Car prototype " + definitionInfo.Identifier + " has no slots.");
			}

			int tons = OrderWeightInTons(definitionInfo, order.Load);
			int paymentOnArrival = order.NoPayment ? 0 : controller.PaymentForMove(industryComponent, order.Destination, tons);
			int graceDays = controller.CalculateGraceDays(industryComponent, order.Destination);
			Waybill waybill = new Waybill(context.Now, industryComponent, order.Destination, paymentOnArrival, false, order.Tag, graceDays);
			CarLoadInfo? loadInfo = order.Load == null ? (CarLoadInfo?)null : new CarLoadInfo(order.Load.id, loadSlots[0].MaximumCapacity);
			var loadProperty = CarExtensions.KeyValueForLoadInfo(0, loadInfo);
			Dictionary<string, Value> properties = new Dictionary<string, Value>
			{
				{ "ops.waybill", waybill.PropertyValue },
				{ loadProperty.Item1, loadProperty.Item2 }
			};

			if (Car.OilFeature)
			{
				float oilLevel = Config.Shared.initialOiledDistribution.Evaluate((float)(rnd != null ? rnd.NextDouble() : new System.Random().NextDouble()));
				properties["oiled"] = Value.Float(oilLevel);
			}

			return new CarDescriptor(definitionInfo, new CarIdent(ReportingMarkForNewCar(rnd), null), null, null, false, properties);
		}

		private static int OrderWeightInTons(TypedContainerItem<CarDefinition> definitionInfo, Load load)
		{
			if (load == null)
			{
				return 0;
			}
			List<LoadSlot> loadSlots = definitionInfo.Definition.LoadSlots;
			if (loadSlots == null || loadSlots.Count == 0)
			{
				return 0;
			}
			LoadSlot loadSlot = loadSlots[0];
			if (!loadSlot.LoadRequirementsMatch(load))
			{
				return 0;
			}
			return Mathf.CeilToInt(load.Pounds(loadSlot.MaximumCapacity) / 2000f);
		}

		private static string ReportingMarkForNewCar(System.Random rnd)
		{
			string playerRoad = StateManager.Shared != null ? StateManager.Shared.RailroadMark : null;
			string[] foreignRoads = OpsController.ForeignRoads ?? Array.Empty<string>();
			List<string> candidates = new List<string>();
			for (int i = 0; i < foreignRoads.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(foreignRoads[i]) && !string.Equals(foreignRoads[i], playerRoad, StringComparison.OrdinalIgnoreCase))
				{
					candidates.Add(foreignRoads[i]);
				}
			}
			if (candidates.Count == 0)
			{
				return string.IsNullOrWhiteSpace(playerRoad) ? "RR" : playerRoad;
			}
			System.Random random = rnd ?? new System.Random();
			return candidates[random.Next(0, candidates.Count)];
		}
	}
}
