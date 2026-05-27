using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Helpers;
using Model.Ops;
using Model.Ops.Definition;
using Track;
using UnityEngine;

namespace Toolshed.SelectiveInterchanges
{
	/// <summary>
	/// Configures an interchange as an explicit off-map supply-chain endpoint.
	/// Only configured loads get purchase loaders, and optional outbound routes can spawn
	/// loaded traffic at this interchange with waybills to another interchange.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class SelectiveInterchangeComponent : IndustryComponent
	{
		private static readonly FieldInfo IndustryCachedComponentsField = AccessTools.Field(typeof(Industry), "_cachedComponents");

		private static readonly System.Random Random = new System.Random();

		public string displayTitle = "Selective Interchange";

		public bool configurePurchases = true;

		public bool disableUnlistedPurchaseLoaders = true;

		public bool enableOutboundTraffic;

		public bool enableProductionAccounting;

		public SelectiveInterchangeInitialCounter[] initialCounters = Array.Empty<SelectiveInterchangeInitialCounter>();

		public bool requireEnabledInterchange = true;

		public bool excludeFromGenericInterchangeSelection = true;

		public SelectiveInterchangeLoadRule[] purchaseLoads = Array.Empty<SelectiveInterchangeLoadRule>();

		public SelectiveInterchangeInboundLoadRule[] inboundLoads = Array.Empty<SelectiveInterchangeInboundLoadRule>();

		public SelectiveInterchangeEmptyCarRequest[] emptyCarRequests = Array.Empty<SelectiveInterchangeEmptyCarRequest>();

		public SelectiveInterchangeOutboundRoute[] outboundRoutes = Array.Empty<SelectiveInterchangeOutboundRoute>();

		public bool debugLogging;

		private readonly Dictionary<string, Load> _loadCache = new Dictionary<string, Load>(StringComparer.OrdinalIgnoreCase);
		private bool _initialCountersApplied;

		public override string DisplayName
		{
			get
			{
				return string.IsNullOrWhiteSpace(displayTitle) ? base.DisplayName : displayTitle;
			}
		}

		private void OnEnable()
		{
			_initialCountersApplied = false;
			Configure();
		}

		public override void Service(IIndustryContext ctx)
		{
			if (enableProductionAccounting)
			{
				ApplyInitialCounters(ctx);
			}
		}

		public override void OrderCars(IIndustryContext ctx)
		{
			if (enableProductionAccounting)
			{
				ApplyInitialCounters(ctx);
				if (ShouldProcessOffMapInputsNow(ctx))
				{
					ProcessOffMapInputs(ctx);
				}
			}
			OrderInboundLoadedCars(ctx);
			OrderEmptyCars(ctx);

			if (!enableOutboundTraffic || outboundRoutes == null || outboundRoutes.Length == 0)
			{
				return;
			}

			// The ops car-counter uses a mock context; only mutate interchange orders during the real ordering pass.
			if (!(ctx is IndustryContext))
			{
				return;
			}

			Interchange source = ResolveOwnInterchange();
			if (!CanUseInterchange(source))
			{
				return;
			}

			for (int i = 0; i < outboundRoutes.Length; i++)
			{
				SelectiveInterchangeOutboundRoute route = outboundRoutes[i];
				if (route == null || !route.enabled)
				{
					continue;
				}

				Load load = ResolveLoad(route.loadId);
				IndustryComponent destination = ResolveDestination(route);
				if (load == null || destination == null || destination == source)
				{
					continue;
				}

				string tag = BuildOrderTag(route, load, destination);
				RegisterCarModelSelector(tag, route);
				int requestedCars = ResolveRequestedCars(route.minCarsPerService, route.maxCarsPerService, route.carsPerService);
				requestedCars = ApplyOutboundRealism(ctx, route, requestedCars);
				if (requestedCars <= 0)
				{
					continue;
				}

				int carsAlreadyOnOrder = ctx.NumberOfCarsOnOrderForTag(tag);
				int carsToOrder = Mathf.Max(0, requestedCars - carsAlreadyOnOrder);
				if (carsToOrder <= 0)
				{
					continue;
				}

				CarTypeFilter filter = BuildFilter(route.carTypeFilterQuery);
				source.AddOrder(new Order(filter, load, destination, carsToOrder, tag, route.noPayment));
				ConsumeOutboundInputs(ctx, route, carsToOrder);
				ScheduleNextOutboundWindow(ctx, route);
				LogDebug("ordered outbound " + carsToOrder + " car(s) load=" + load.id + ", destination=" + destination.Identifier + ", tag=" + tag);
			}
		}

		private bool ShouldProcessOffMapInputsNow(IIndustryContext ctx)
		{
			// Interchange tracks should not swallow cars on the normal industry tick.
			// Railroader calls OrderCars immediately before serving due interchanges, so
			// this gate keeps selective intake aligned with vanilla interchange timing.
			if (!(ctx is IndustryContext))
			{
				return false;
			}

			Interchange interchange = ResolveOwnInterchange();
			if (!CanUseInterchange(interchange))
			{
				return false;
			}

			Interchange.NextServiceStyle style;
			return !(interchange.GetNextServiceTime(ctx.Now, out style, false) > ctx.Now);
		}

		private void OrderInboundLoadedCars(IIndustryContext ctx)
		{
			if (inboundLoads == null || inboundLoads.Length == 0 || !Industry.ShouldOrderCars())
			{
				return;
			}

			for (int i = 0; i < inboundLoads.Length; i++)
			{
				SelectiveInterchangeInboundLoadRule rule = inboundLoads[i];
				if (rule == null || !rule.enabled || !rule.orderLoadedCars || rule.useVanillaIndustryUnloader)
				{
					continue;
				}

				Load load = ResolveLoad(rule.loadId);
				if (load == null)
				{
					continue;
				}

				string tag = BuildInboundOrderTag(rule, i);
				int carsAlreadyOnOrder = ctx.NumberOfCarsOnOrderForTag(tag);
				int carsToOrder = InboundCarsNeeded(ctx, rule, carsAlreadyOnOrder);
				if (carsToOrder <= 0)
				{
					continue;
				}

				CarTypeFilter filter = BuildFilter(rule.carTypeFilterQuery);
				int ordered = 0;
				float quantity;
				while (ordered < carsToOrder && ctx.OrderLoad(filter, load, tag, false, out quantity))
				{
					ordered++;
				}
				if (ordered > 0)
				{
					LogDebug("ordered inbound " + ordered + " car(s) load=" + load.id + ", tag=" + tag);
				}
			}
		}

		private int InboundCarsNeeded(IIndustryContext ctx, SelectiveInterchangeInboundLoadRule rule, int carsAlreadyOnOrder)
		{
			float creditsPerCar = rule.supplyCreditsPerCar > 0f ? rule.supplyCreditsPerCar : 1f;
			if (rule.maxSupplyCredits > 0f)
			{
				string key = string.IsNullOrWhiteSpace(rule.supplyCounterKey) ? rule.loadId : rule.supplyCounterKey;
				float current = GetCounter(ctx, key);
				int remainingCapacity = Mathf.FloorToInt(Mathf.Max(0f, rule.maxSupplyCredits - current) / creditsPerCar);
				return Mathf.Max(0, remainingCapacity - carsAlreadyOnOrder);
			}

			return carsAlreadyOnOrder > 0 ? 0 : 1;
		}

		private void ApplyInitialCounters(IIndustryContext ctx)
		{
			if (_initialCountersApplied || initialCounters == null)
			{
				return;
			}

			for (int i = 0; i < initialCounters.Length; i++)
			{
				SelectiveInterchangeInitialCounter counter = initialCounters[i];
				if (counter == null || string.IsNullOrWhiteSpace(counter.key) || counter.value <= 0f)
				{
					continue;
				}
				string initializedKey = BuildCounterKey("initialized:" + counter.key);
				if (ctx.CounterIncrement(initializedKey, 0f) > 0f)
				{
					continue;
				}
				SetCounter(ctx, counter.key, Mathf.Min(counter.value, counter.maxValue > 0f ? counter.maxValue : counter.value));
				ctx.CounterIncrement(initializedKey, 1f);
				LogDebug("initialized counter " + counter.key + "=" + counter.value.ToString("0.###"));
			}
			_initialCountersApplied = true;
		}

		private void ProcessOffMapInputs(IIndustryContext ctx)
		{
			List<IOpsCar> cars = new List<IOpsCar>(ctx.CarsAtPosition());
			for (int i = 0; i < cars.Count; i++)
			{
				IOpsCar car = cars[i];
				if (car == null)
				{
					continue;
				}
				if (TryReceiveEmptyCar(ctx, car))
				{
					continue;
				}
				TryReceiveInboundLoad(ctx, car);
			}
		}

		private bool TryReceiveEmptyCar(IIndustryContext ctx, IOpsCar car)
		{
			if (emptyCarRequests == null || emptyCarRequests.Length == 0 || !CarIsEmpty(car))
			{
				return false;
			}
			for (int i = 0; i < emptyCarRequests.Length; i++)
			{
				SelectiveInterchangeEmptyCarRequest request = emptyCarRequests[i];
				if (request == null || !request.enabled || !BuildFilter(request.carTypeFilterQuery).Matches(car.CarType))
				{
					continue;
				}
				if (request.countOnlyTaggedCars && !CarHasWaybillTag(car, BuildEmptyOrderTag(request, i)))
				{
					continue;
				}

				string key = string.IsNullOrWhiteSpace(request.equipmentCounterKey) ? "equipment" : request.equipmentCounterKey;
				float credit = request.equipmentCreditsPerCar > 0f ? request.equipmentCreditsPerCar : 1f;
				AddCounter(ctx, key, credit, request.maxEquipmentCredits);
				ctx.MoveToBardo(car);
				LogDebug("received empty " + car.DisplayName + " as off-map equipment credit " + credit.ToString("0.###") + " for " + key);
				return true;
			}
			return false;
		}

		private bool TryReceiveInboundLoad(IIndustryContext ctx, IOpsCar car)
		{
			if (inboundLoads == null || inboundLoads.Length == 0)
			{
				return false;
			}
			for (int i = 0; i < inboundLoads.Length; i++)
			{
				SelectiveInterchangeInboundLoadRule rule = inboundLoads[i];
				if (rule == null || !rule.enabled || !BuildFilter(rule.carTypeFilterQuery).Matches(car.CarType))
				{
					continue;
				}
				Load load = ResolveLoad(rule.loadId);
				if (load == null)
				{
					continue;
				}
				var quantity = car.QuantityOfLoad(load);
				if (quantity.Item1 <= 0f)
				{
					continue;
				}
				if (rule.countOnlyTaggedCars && !CarHasWaybillTag(car, BuildInboundOrderTag(rule, i)))
				{
					continue;
				}

				float unloaded = car.Unload(load, quantity.Item1);
				if (unloaded <= 0f)
				{
					continue;
				}
				string key = string.IsNullOrWhiteSpace(rule.supplyCounterKey) ? load.id : rule.supplyCounterKey;
				float credit = rule.supplyCreditsPerCar > 0f ? rule.supplyCreditsPerCar : 1f;
				AddCounter(ctx, key, credit, rule.maxSupplyCredits);
				if (rule.moveToBardoAfterUnload)
				{
					ctx.MoveToBardo(car);
				}
				else if (rule.orderAwayEmpties)
				{
					ctx.OrderAwayEmpty(car, BuildInboundOrderTag(rule, i), false);
				}
				LogDebug("received inbound " + load.id + " from " + car.DisplayName + " as supply credit " + credit.ToString("0.###") + " for " + key);
				return true;
			}
			return false;
		}

		private void OrderEmptyCars(IIndustryContext ctx)
		{
			if (emptyCarRequests == null || emptyCarRequests.Length == 0)
			{
				return;
			}

			Interchange source = ResolveOwnInterchange();
			if (!CanUseInterchange(source))
			{
				return;
			}

			for (int i = 0; i < emptyCarRequests.Length; i++)
			{
				SelectiveInterchangeEmptyCarRequest request = emptyCarRequests[i];
				if (request == null || !request.enabled)
				{
					continue;
				}

				CarTypeFilter filter = BuildFilter(request.carTypeFilterQuery);
				string tag = BuildEmptyOrderTag(request, i);
				RegisterCarModelSelector(tag, request);
				int requestedCars = ResolveRequestedCars(request.minCarsPerService, request.maxCarsPerService, request.carsPerService);
				requestedCars = ApplyEmptyRequestCapacity(ctx, request, requestedCars);
				if (requestedCars <= 0)
				{
					continue;
				}
				int carsAlreadyOnOrder = ctx.NumberOfCarsOnOrderForTag(tag);
				int carsToOrder = Mathf.Max(0, requestedCars - carsAlreadyOnOrder);
				if (carsToOrder <= 0)
				{
					continue;
				}
				Interchange origin = ResolveEmptyOrigin(request);
				if (HasExplicitEmptyOrigin(request))
				{
					if (!(ctx is IndustryContext))
					{
						continue;
					}
					if (origin == null)
					{
						LogDebug("skipped empty car order carTypes=" + request.carTypeFilterQuery + ", tag=" + tag + " because no enabled origin interchange resolved.");
						continue;
					}
					origin.AddOrder(new Order(filter, null, this, carsToOrder, tag, request.noPayment));
					if (carsToOrder > 0)
					{
						LogDebug("ordered " + carsToOrder + " empty car(s) from " + origin.Identifier + " to " + Identifier + " carTypes=" + request.carTypeFilterQuery + ", tag=" + tag);
					}
					continue;
				}
				for (int j = 0; j < carsToOrder; j++)
				{
					ctx.OrderEmpty(filter, tag, request.noPayment);
				}
				if (carsToOrder > 0)
				{
					LogDebug("ordered " + carsToOrder + " empty car(s) carTypes=" + request.carTypeFilterQuery + ", tag=" + tag);
				}
			}
		}

		private bool HasExplicitEmptyOrigin(SelectiveInterchangeEmptyCarRequest request)
		{
			if (request == null)
			{
				return false;
			}
			return !string.IsNullOrWhiteSpace(request.originPositionId)
				|| !string.IsNullOrWhiteSpace(request.originIndustryId)
				|| !string.IsNullOrWhiteSpace(request.originComponentId)
				|| !string.IsNullOrWhiteSpace(request.sourcePositionId)
				|| !string.IsNullOrWhiteSpace(request.sourceIndustryId)
				|| !string.IsNullOrWhiteSpace(request.sourceComponentId)
				|| HasAny(request.originPositionIds)
				|| HasAny(request.originIndustryIds)
				|| HasAny(request.originComponentIds)
				|| HasAny(request.sourcePositionIds)
				|| HasAny(request.sourceIndustryIds)
				|| HasAny(request.sourceComponentIds)
				|| (request.originWeights != null && request.originWeights.Length > 0)
				|| (request.sourceWeights != null && request.sourceWeights.Length > 0);
		}

		private static bool HasAny(string[] values)
		{
			if (values == null)
			{
				return false;
			}
			for (int i = 0; i < values.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(values[i]))
				{
					return true;
				}
			}
			return false;
		}

		private Interchange ResolveEmptyOrigin(SelectiveInterchangeEmptyCarRequest request)
		{
			Interchange origin = ResolveEmptyOrigin(request, false);
			if (origin != null || (!request.allowInactiveOriginFallback && !request.allowDisabledOriginFallback))
			{
				return origin;
			}
			return ResolveEmptyOrigin(request, true);
		}

		private Interchange ResolveEmptyOrigin(SelectiveInterchangeEmptyCarRequest request, bool allowInactive)
		{
			Interchange weightedOrigin = ResolveWeightedOrigin(request, allowInactive);
			if (CanUseEmptyOrigin(weightedOrigin, request, allowInactive))
			{
				return weightedOrigin;
			}

			foreach (string positionId in SelectiveInterchangeRuntime.CandidateStrings(request.originPositionId, request.originPositionIds))
			{
				Interchange origin = ResolveOriginPosition(positionId);
				if (CanUseEmptyOrigin(origin, request, allowInactive))
				{
					return origin;
				}
			}
			foreach (string positionId in SelectiveInterchangeRuntime.CandidateStrings(request.sourcePositionId, request.sourcePositionIds))
			{
				Interchange origin = ResolveOriginPosition(positionId);
				if (CanUseEmptyOrigin(origin, request, allowInactive))
				{
					return origin;
				}
			}

			foreach (string industryId in SelectiveInterchangeRuntime.CandidateStrings(request.originIndustryId, request.originIndustryIds))
			{
				Interchange origin = ResolveOriginIndustry(industryId);
				if (CanUseEmptyOrigin(origin, request, allowInactive))
				{
					return origin;
				}
			}
			foreach (string industryId in SelectiveInterchangeRuntime.CandidateStrings(request.sourceIndustryId, request.sourceIndustryIds))
			{
				Interchange origin = ResolveOriginIndustry(industryId);
				if (CanUseEmptyOrigin(origin, request, allowInactive))
				{
					return origin;
				}
			}

			foreach (string componentId in SelectiveInterchangeRuntime.CandidateStrings(request.originComponentId, request.originComponentIds))
			{
				Interchange origin = SelectiveInterchangeRuntime.ResolveInterchange(componentId);
				if (CanUseEmptyOrigin(origin, request, allowInactive))
				{
					return origin;
				}
			}
			foreach (string componentId in SelectiveInterchangeRuntime.CandidateStrings(request.sourceComponentId, request.sourceComponentIds))
			{
				Interchange origin = SelectiveInterchangeRuntime.ResolveInterchange(componentId);
				if (CanUseEmptyOrigin(origin, request, allowInactive))
				{
					return origin;
				}
			}

			return null;
		}

		private Interchange ResolveWeightedOrigin(SelectiveInterchangeEmptyCarRequest request, bool allowInactive)
		{
			SelectiveInterchangeWeightedOrigin[] weights = request.originWeights != null && request.originWeights.Length > 0
				? request.originWeights
				: request.sourceWeights;
			if (weights == null || weights.Length == 0)
			{
				return null;
			}

			List<Tuple<Interchange, int>> candidates = new List<Tuple<Interchange, int>>();
			int totalWeight = 0;
			for (int i = 0; i < weights.Length; i++)
			{
				SelectiveInterchangeWeightedOrigin item = weights[i];
				if (item == null || item.weight <= 0)
				{
					continue;
				}
				Interchange origin = ResolveOriginItem(item);
				if (!CanUseEmptyOrigin(origin, request, allowInactive))
				{
					continue;
				}
				totalWeight += item.weight;
				candidates.Add(new Tuple<Interchange, int>(origin, totalWeight));
			}
			if (candidates.Count == 0 || totalWeight <= 0)
			{
				return null;
			}
			int roll;
			lock (Random)
			{
				roll = Random.Next(1, totalWeight + 1);
			}
			for (int i = 0; i < candidates.Count; i++)
			{
				if (roll <= candidates[i].Item2)
				{
					return candidates[i].Item1;
				}
			}
			return candidates[candidates.Count - 1].Item1;
		}

		private Interchange ResolveOriginItem(SelectiveInterchangeWeightedOrigin item)
		{
			if (!string.IsNullOrWhiteSpace(item.originPositionId))
			{
				return ResolveOriginPosition(item.originPositionId);
			}
			if (!string.IsNullOrWhiteSpace(item.sourcePositionId))
			{
				return ResolveOriginPosition(item.sourcePositionId);
			}
			if (!string.IsNullOrWhiteSpace(item.originIndustryId))
			{
				return ResolveOriginIndustry(item.originIndustryId);
			}
			if (!string.IsNullOrWhiteSpace(item.sourceIndustryId))
			{
				return ResolveOriginIndustry(item.sourceIndustryId);
			}
			if (!string.IsNullOrWhiteSpace(item.originComponentId))
			{
				return SelectiveInterchangeRuntime.ResolveInterchange(item.originComponentId);
			}
			if (!string.IsNullOrWhiteSpace(item.sourceComponentId))
			{
				return SelectiveInterchangeRuntime.ResolveInterchange(item.sourceComponentId);
			}
			return null;
		}

		private static Interchange ResolveOriginPosition(string positionId)
		{
			IndustryComponent component = SelectiveInterchangeRuntime.ResolveIndustryComponent(positionId);
			if (component == null)
			{
				return null;
			}
			Interchange interchange = component as Interchange;
			if (interchange != null)
			{
				return interchange;
			}
			Industry industry = component.Industry;
			return industry != null ? industry.GetComponentInChildren<Interchange>(true) : null;
		}

		private static Interchange ResolveOriginIndustry(string industryId)
		{
			Industry industry = SelectiveInterchangeRuntime.ResolveIndustry(industryId);
			return industry != null ? industry.GetComponentInChildren<Interchange>(true) : null;
		}

		private bool CanUseEmptyOrigin(Interchange origin, SelectiveInterchangeEmptyCarRequest request, bool allowInactive)
		{
			if (origin == null || (!allowInactive && requireEnabledInterchange && !CanUseInterchange(origin)))
			{
				return false;
			}
			if (!request.allowOriginAtThisInterchange && origin.Industry == Industry)
			{
				return false;
			}
			return true;
		}

		private int ApplyEmptyRequestCapacity(IIndustryContext ctx, SelectiveInterchangeEmptyCarRequest request, int requestedCars)
		{
			if (!enableProductionAccounting || requestedCars <= 0 || request.maxEquipmentCredits <= 0f)
			{
				return requestedCars;
			}
			string key = string.IsNullOrWhiteSpace(request.equipmentCounterKey) ? "equipment" : request.equipmentCounterKey;
			float current = GetCounter(ctx, key);
			float credit = request.equipmentCreditsPerCar > 0f ? request.equipmentCreditsPerCar : 1f;
			int capacityCars = Mathf.FloorToInt(Mathf.Max(0f, request.maxEquipmentCredits - current) / credit);
			return Mathf.Min(requestedCars, capacityCars);
		}

		private int ApplyOutboundRealism(IIndustryContext ctx, SelectiveInterchangeOutboundRoute route, int requestedCars)
		{
			if (!enableProductionAccounting || requestedCars <= 0)
			{
				return requestedCars;
			}
			if (!IsRouteReady(ctx, route))
			{
				return 0;
			}
			int limited = requestedCars;
			if (route.equipmentCreditsPerCar > 0f)
			{
				float available = ctx.CounterIncrement(BuildCounterKey(route.equipmentCounterKey), 0f);
				limited = Mathf.Min(limited, Mathf.FloorToInt(available / route.equipmentCreditsPerCar));
			}
			if (route.supplyCreditsPerCar > 0f)
			{
				float available = ctx.CounterIncrement(BuildCounterKey(route.supplyCounterKey), 0f);
				limited = Mathf.Min(limited, Mathf.FloorToInt(available / route.supplyCreditsPerCar));
			}
			if (route.slowDayChance > 0f && RandomChance(route.slowDayChance))
			{
				float multiplier = Mathf.Max(0f, route.slowDayMultiplier);
				limited = Mathf.FloorToInt(limited * multiplier);
			}
			return Mathf.Max(0, limited);
		}

		private bool IsRouteReady(IIndustryContext ctx, SelectiveInterchangeOutboundRoute route)
		{
			if (route.weekdaysOnly && ctx.Now.Day % 7 >= 5)
			{
				return false;
			}
			if (route.productionDays != null && route.productionDays.Length > 0)
			{
				bool allowed = false;
				int day = ctx.Now.Day % 7;
				for (int i = 0; i < route.productionDays.Length; i++)
				{
					if (route.productionDays[i] == day)
					{
						allowed = true;
						break;
					}
				}
				if (!allowed)
				{
					return false;
				}
			}
			Game.GameDateTime readyAt = ctx.GetDateTime(BuildReadyAtKey(route), Game.GameDateTime.Zero);
			return readyAt <= Game.GameDateTime.Zero || ctx.Now >= readyAt;
		}

		private void ConsumeOutboundInputs(IIndustryContext ctx, SelectiveInterchangeOutboundRoute route, int carsOrdered)
		{
			if (!enableProductionAccounting || carsOrdered <= 0)
			{
				return;
			}
			ConsumeCounter(ctx, route.equipmentCounterKey, route.equipmentCreditsPerCar * carsOrdered);
			ConsumeCounter(ctx, route.supplyCounterKey, route.supplyCreditsPerCar * carsOrdered);
		}

		private void ConsumeCounter(IIndustryContext ctx, string counterKey, float amount)
		{
			if (string.IsNullOrWhiteSpace(counterKey) || amount <= 0f)
			{
				return;
			}
			SetCounter(ctx, counterKey, Mathf.Max(0f, GetCounter(ctx, counterKey) - amount));
		}

		private void AddCounter(IIndustryContext ctx, string counterKey, float amount, float maxValue)
		{
			if (string.IsNullOrWhiteSpace(counterKey) || amount <= 0f)
			{
				return;
			}
			float next = GetCounter(ctx, counterKey) + amount;
			if (maxValue > 0f)
			{
				next = Mathf.Min(next, maxValue);
			}
			SetCounter(ctx, counterKey, next);
		}

		private float GetCounter(IIndustryContext ctx, string counterKey)
		{
			return string.IsNullOrWhiteSpace(counterKey) ? 0f : ctx.CounterIncrement(BuildCounterKey(counterKey), 0f);
		}

		private void SetCounter(IIndustryContext ctx, string counterKey, float value)
		{
			if (string.IsNullOrWhiteSpace(counterKey))
			{
				return;
			}
			string key = BuildCounterKey(counterKey);
			ctx.CounterClear(key);
			if (value > 0f)
			{
				ctx.CounterIncrement(key, value);
			}
		}

		private void ScheduleNextOutboundWindow(IIndustryContext ctx, SelectiveInterchangeOutboundRoute route)
		{
			float delay = ResolveRandomFloat(route.minReturnDelayDays, route.maxReturnDelayDays);
			if (delay <= 0f)
			{
				return;
			}
			ctx.SetDateTime(BuildReadyAtKey(route), ctx.Now.AddingDays(delay));
		}

		[ContextMenu("Configure Selective Interchange")]
		public void Configure()
		{
			if (!Main.Enabled)
			{
				return;
			}

			Interchange interchange = ResolveOwnInterchange();
			if (interchange == null)
			{
				LogWarning("no Interchange component found under the same industry.");
				return;
			}

			if (configurePurchases)
			{
				ConfigurePurchaseLoaders(interchange);
			}
			ConfigureInboundUnloaders();

			RefreshIndustryComponents(Industry);
		}

		private void ConfigureInboundUnloaders()
		{
			if (inboundLoads == null)
			{
				return;
			}

			for (int i = 0; i < inboundLoads.Length; i++)
			{
				SelectiveInterchangeInboundLoadRule rule = inboundLoads[i];
				if (rule == null || !rule.enabled)
				{
					continue;
				}

				Load load = ResolveLoad(rule.loadId);
				if (load == null)
				{
					LogWarning("unknown inbound load id '" + rule.loadId + "'.");
					continue;
				}

				if (!rule.useVanillaIndustryUnloader)
				{
					DisableInboundUnloader(rule, load);
					LogDebug("configured scheduled inbound load=" + load.id + ", carTypes=" + rule.carTypeFilterQuery);
					continue;
				}

				IndustryUnloader unloader = FindOrCreateInboundUnloader(rule, load);
				if (unloader == null)
				{
					continue;
				}

				unloader.enabled = true;
				unloader.load = load;
				unloader.carTypeFilter = BuildFilter(rule.carTypeFilterQuery);
				unloader.trackSpans = ResolveEffectiveTrackSpans(rule.trackSpanIds);
				unloader.sharedStorage = true;
				unloader.carUnloadRate = rule.carTransferRate > 0f ? rule.carTransferRate : 200000f;
				unloader.storageConsumptionRate = rule.storageConsumptionRate;
				unloader.maxStorage = rule.maxStorage > 0f ? rule.maxStorage : unloader.carUnloadRate;
				unloader.orderLoads = rule.orderLoadedCars;
				unloader.orderAwayEmpties = rule.orderAwayEmpties;
				unloader.name = string.IsNullOrWhiteSpace(rule.displayName)
					? DisplayName + " - " + load.description + " Inbound"
					: rule.displayName;
				LogDebug("configured inbound load=" + load.id + ", carTypes=" + unloader.carTypeFilter + ", spans=" + unloader.trackSpans.Length);
			}
		}

		private void DisableInboundUnloader(SelectiveInterchangeInboundLoadRule rule, Load load)
		{
			string subId = string.IsNullOrWhiteSpace(rule.componentId)
				? "toolshed-selective-in-" + SafeIdentifier(load.id)
				: rule.componentId;

			IndustryUnloader[] unloaders = Industry.GetComponentsInChildren<IndustryUnloader>(true);
			for (int i = 0; i < unloaders.Length; i++)
			{
				IndustryUnloader unloader = unloaders[i];
				if (unloader == null)
				{
					continue;
				}

				bool matchesComponent = string.Equals(unloader.subIdentifier, subId, StringComparison.OrdinalIgnoreCase);
				bool matchesGeneratedLoad = string.IsNullOrWhiteSpace(rule.componentId)
					&& unloader.load != null
					&& string.Equals(unloader.load.id, load.id, StringComparison.OrdinalIgnoreCase);
				if (matchesComponent || matchesGeneratedLoad)
				{
					unloader.enabled = false;
					LogDebug("disabled vanilla inbound unloader " + unloader.Identifier + " so the selective interchange schedule owns intake.");
				}
			}
		}

		private void ConfigurePurchaseLoaders(Interchange interchange)
		{
			if (purchaseLoads == null)
			{
				return;
			}

			HashSet<string> allowedComponentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> allowedLoadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < purchaseLoads.Length; i++)
			{
				SelectiveInterchangeLoadRule rule = purchaseLoads[i];
				if (rule == null || !rule.enabled)
				{
					continue;
				}

				Load load = ResolveLoad(rule.loadId);
				if (load == null)
				{
					LogWarning("unknown purchase load id '" + rule.loadId + "'.");
					continue;
				}
				allowedLoadIds.Add(load.id);

				InterchangedIndustryLoader loader = FindOrCreatePurchaseLoader(rule, load);
				if (loader == null)
				{
					continue;
				}

				allowedComponentIds.Add(loader.subIdentifier);
				loader.enabled = true;
				loader.load = load;
				loader.carTypeFilter = BuildFilter(rule.carTypeFilterQuery);
				loader.trackSpans = ResolveEffectiveTrackSpans(rule.trackSpanIds);
				loader.sharedStorage = true;
				loader.name = string.IsNullOrWhiteSpace(rule.displayName)
					? DisplayName + " - " + load.description
					: rule.displayName;
				LogDebug("configured purchase load=" + load.id + ", carTypes=" + loader.carTypeFilter + ", spans=" + loader.trackSpans.Length);
			}

			if (disableUnlistedPurchaseLoaders)
			{
				DisableUnlistedPurchaseLoaders(allowedComponentIds, allowedLoadIds);
			}
		}

		private InterchangedIndustryLoader FindOrCreatePurchaseLoader(SelectiveInterchangeLoadRule rule, Load load)
		{
			string subId = string.IsNullOrWhiteSpace(rule.componentId)
				? "toolshed-selective-buy-" + SafeIdentifier(load.id)
				: rule.componentId;

			InterchangedIndustryLoader[] loaders = Industry.GetComponentsInChildren<InterchangedIndustryLoader>(true);
			for (int i = 0; i < loaders.Length; i++)
			{
				InterchangedIndustryLoader loader = loaders[i];
				if (loader == null)
				{
					continue;
				}
				if (string.Equals(loader.subIdentifier, subId, StringComparison.OrdinalIgnoreCase))
				{
					return loader;
				}
			}

			if (string.IsNullOrWhiteSpace(rule.componentId))
			{
				for (int i = 0; i < loaders.Length; i++)
				{
					InterchangedIndustryLoader loader = loaders[i];
					if (loader != null && loader.load != null && string.Equals(loader.load.id, load.id, StringComparison.OrdinalIgnoreCase))
					{
						return loader;
					}
				}
			}

			if (!rule.createIfMissing)
			{
				LogWarning("purchase loader '" + subId + "' was not found. Enable createIfMissing or author the component in FUSE.");
				return null;
			}

			GameObject child = new GameObject(string.IsNullOrWhiteSpace(rule.displayName) ? "Selective " + load.description : rule.displayName);
			child.SetActive(false);
			child.transform.SetParent(Industry.transform, false);
			InterchangedIndustryLoader created = child.AddComponent<InterchangedIndustryLoader>();
			created.subIdentifier = subId;
			child.SetActive(true);
			return created;
		}

		private IndustryUnloader FindOrCreateInboundUnloader(SelectiveInterchangeInboundLoadRule rule, Load load)
		{
			string subId = string.IsNullOrWhiteSpace(rule.componentId)
				? "toolshed-selective-in-" + SafeIdentifier(load.id)
				: rule.componentId;

			IndustryUnloader[] unloaders = Industry.GetComponentsInChildren<IndustryUnloader>(true);
			for (int i = 0; i < unloaders.Length; i++)
			{
				IndustryUnloader unloader = unloaders[i];
				if (unloader != null && string.Equals(unloader.subIdentifier, subId, StringComparison.OrdinalIgnoreCase))
				{
					return unloader;
				}
			}

			if (!rule.createIfMissing)
			{
				LogWarning("inbound unloader '" + subId + "' was not found. Enable createIfMissing or author the component in FUSE.");
				return null;
			}

			GameObject child = new GameObject(string.IsNullOrWhiteSpace(rule.displayName) ? "Selective inbound " + load.description : rule.displayName);
			child.SetActive(false);
			child.transform.SetParent(Industry.transform, false);
			IndustryUnloader created = child.AddComponent<IndustryUnloader>();
			created.subIdentifier = subId;
			child.SetActive(true);
			return created;
		}

		private void DisableUnlistedPurchaseLoaders(HashSet<string> allowedComponentIds, HashSet<string> allowedLoadIds)
		{
			InterchangedIndustryLoader[] loaders = Industry.GetComponentsInChildren<InterchangedIndustryLoader>(true);
			for (int i = 0; i < loaders.Length; i++)
			{
				InterchangedIndustryLoader loader = loaders[i];
				if (loader == null)
				{
					continue;
				}

				bool allowedByComponent = allowedComponentIds.Contains(loader.subIdentifier);
				bool allowedByLoad = loader.load != null && allowedLoadIds.Contains(loader.load.id);
				bool shouldDisable = !allowedByComponent && !allowedByLoad;
				if (loader.enabled == shouldDisable)
				{
					loader.enabled = !shouldDisable;
					LogDebug((shouldDisable ? "disabled" : "enabled") + " purchase loader " + loader.Identifier);
				}
			}
		}

		private TrackSpan[] ResolveEffectiveTrackSpans(string[] overrideSpanIds)
		{
			TrackSpan[] explicitSpans = SelectiveInterchangeRuntime.ResolveTrackSpans(overrideSpanIds);
			if (explicitSpans.Length > 0)
			{
				return explicitSpans;
			}
			if (trackSpans != null && trackSpans.Length > 0)
			{
				return trackSpans;
			}
			Interchange interchange = ResolveOwnInterchange();
			return interchange != null && interchange.trackSpans != null ? interchange.trackSpans : Array.Empty<TrackSpan>();
		}

		private Interchange ResolveOwnInterchange()
		{
			return Industry != null ? Industry.GetComponentInChildren<Interchange>(true) : null;
		}

		private static bool CanUseInterchange(Interchange interchange)
		{
			if (interchange == null || interchange.ProgressionDisabled || interchange.Industry.ProgressionDisabled)
			{
				return false;
			}
			return !interchange.Disabled;
		}

		private IndustryComponent ResolveDestination(SelectiveInterchangeOutboundRoute route)
		{
			IndustryComponent destination = ResolveDestination(route, false);
			if (destination != null || (!route.allowInactiveDestinationFallback && !route.allowDisabledDestinationFallback))
			{
				return destination;
			}
			return ResolveDestination(route, true);
		}

		private IndustryComponent ResolveDestination(SelectiveInterchangeOutboundRoute route, bool allowInactive)
		{
			IndustryComponent weightedDestination = ResolveWeightedDestination(route, allowInactive);
			if (weightedDestination != null)
			{
				return weightedDestination;
			}

			IndustryComponent industryChild = ResolveDestinationIndustryComponent(route);
			if (industryChild != null)
			{
				return industryChild;
			}

			foreach (string positionId in SelectiveInterchangeRuntime.CandidateStrings(route.destinationPositionId, route.destinationPositionIds))
			{
				IndustryComponent component = SelectiveInterchangeRuntime.ResolveIndustryComponent(positionId);
				if (component == null)
				{
					continue;
				}
				Interchange interchange = component as Interchange;
				if (interchange == null || allowInactive || !requireEnabledInterchange || CanUseInterchange(interchange))
				{
					return component;
				}
			}

			foreach (string industryId in SelectiveInterchangeRuntime.CandidateStrings(route.destinationIndustryId, route.destinationIndustryIds))
			{
				Industry industry = SelectiveInterchangeRuntime.ResolveIndustry(industryId);
				Interchange interchange = industry != null ? industry.GetComponentInChildren<Interchange>(true) : null;
				if (interchange != null && (allowInactive || !requireEnabledInterchange || CanUseInterchange(interchange)))
				{
					return interchange;
				}
			}

			foreach (string componentId in SelectiveInterchangeRuntime.CandidateStrings(route.destinationComponentId, route.destinationComponentIds))
			{
				Interchange interchange = SelectiveInterchangeRuntime.ResolveInterchange(componentId);
				if (interchange != null && (allowInactive || !requireEnabledInterchange || CanUseInterchange(interchange)))
				{
					return interchange;
				}
			}

			return null;
		}

		private IndustryComponent ResolveWeightedDestination(SelectiveInterchangeOutboundRoute route, bool allowInactive)
		{
			if (route.destinationWeights == null || route.destinationWeights.Length == 0)
			{
				return null;
			}

			List<Tuple<IndustryComponent, int>> candidates = new List<Tuple<IndustryComponent, int>>();
			int totalWeight = 0;
			for (int i = 0; i < route.destinationWeights.Length; i++)
			{
				SelectiveInterchangeWeightedDestination item = route.destinationWeights[i];
				if (item == null || item.weight <= 0)
				{
					continue;
				}
				IndustryComponent destination = ResolveDestinationItem(item, allowInactive);
				if (destination == null)
				{
					continue;
				}
				totalWeight += item.weight;
				candidates.Add(new Tuple<IndustryComponent, int>(destination, totalWeight));
			}
			if (candidates.Count == 0 || totalWeight <= 0)
			{
				return null;
			}
			int roll;
			lock (Random)
			{
				roll = Random.Next(1, totalWeight + 1);
			}
			for (int i = 0; i < candidates.Count; i++)
			{
				if (roll <= candidates[i].Item2)
				{
					return candidates[i].Item1;
				}
			}
			return candidates[candidates.Count - 1].Item1;
		}

		private IndustryComponent ResolveDestinationItem(SelectiveInterchangeWeightedDestination item, bool allowInactive)
		{
			if (!string.IsNullOrWhiteSpace(item.destinationIndustryId) && !string.IsNullOrWhiteSpace(item.destinationComponentId))
			{
				Industry industry = SelectiveInterchangeRuntime.ResolveIndustry(item.destinationIndustryId);
				if (industry != null)
				{
					IndustryComponent component = SelectiveInterchangeRuntime.ResolveIndustryComponent(industry.identifier + "." + item.destinationComponentId);
					if (component != null)
					{
						return component;
					}
				}
			}
			if (!string.IsNullOrWhiteSpace(item.destinationPositionId))
			{
				IndustryComponent component = SelectiveInterchangeRuntime.ResolveIndustryComponent(item.destinationPositionId);
				if (component != null)
				{
					return component;
				}
			}
			if (!string.IsNullOrWhiteSpace(item.destinationIndustryId))
			{
				Industry industry = SelectiveInterchangeRuntime.ResolveIndustry(item.destinationIndustryId);
				Interchange interchange = industry != null ? industry.GetComponentInChildren<Interchange>(true) : null;
				if (interchange != null && (allowInactive || !requireEnabledInterchange || CanUseInterchange(interchange)))
				{
					return interchange;
				}
			}
			return null;
		}

		private IndustryComponent ResolveDestinationIndustryComponent(SelectiveInterchangeOutboundRoute route)
		{
			foreach (string industryId in SelectiveInterchangeRuntime.CandidateStrings(route.destinationIndustryId, route.destinationIndustryIds))
			{
				Industry industry = SelectiveInterchangeRuntime.ResolveIndustry(industryId);
				if (industry == null)
				{
					continue;
				}

				foreach (string componentId in SelectiveInterchangeRuntime.CandidateStrings(route.destinationComponentId, route.destinationComponentIds))
				{
					IndustryComponent component = SelectiveInterchangeRuntime.ResolveIndustryComponent(industry.identifier + "." + componentId);
					if (component != null)
					{
						return component;
					}
				}
			}

			return null;
		}

		private Load ResolveLoad(string loadId)
		{
			if (string.IsNullOrWhiteSpace(loadId))
			{
				return null;
			}
			Load cached;
			if (_loadCache.TryGetValue(loadId, out cached))
			{
				return cached;
			}
			Load resolved = SelectiveInterchangeRuntime.ResolveLoad(loadId);
			if (resolved != null)
			{
				_loadCache[loadId] = resolved;
			}
			return resolved;
		}

		private static CarTypeFilter BuildFilter(string query)
		{
			return new CarTypeFilter(string.IsNullOrWhiteSpace(query) ? "*" : query);
		}

		private static int ResolveRequestedCars(int minCars, int maxCars, int fallbackCars)
		{
			if (minCars > 0 || maxCars > 0)
			{
				int min = Mathf.Max(0, minCars);
				int max = Mathf.Max(min, maxCars > 0 ? maxCars : min);
				lock (Random)
				{
					return Random.Next(min, max + 1);
				}
			}
			return fallbackCars > 0 ? fallbackCars : 1;
		}

		private static float ResolveRandomFloat(float min, float max)
		{
			if (min <= 0f && max <= 0f)
			{
				return 0f;
			}
			float low = Mathf.Max(0f, min);
			float high = Mathf.Max(low, max > 0f ? max : low);
			lock (Random)
			{
				return low + (float)Random.NextDouble() * (high - low);
			}
		}

		private static bool RandomChance(float chance)
		{
			if (chance <= 0f)
			{
				return false;
			}
			if (chance >= 1f)
			{
				return true;
			}
			lock (Random)
			{
				return Random.NextDouble() < chance;
			}
		}

		private bool CarIsEmpty(IOpsCar car)
		{
			if (car.Waybill.HasValue && car.Waybill.Value.Destination.Identifier == Identifier)
			{
				return true;
			}
			for (int i = 0; i < purchaseLoads.Length; i++)
			{
				Load load = purchaseLoads[i] != null ? ResolveLoad(purchaseLoads[i].loadId) : null;
				if (load != null && !car.IsEmptyOrContains(load))
				{
					return false;
				}
			}
			for (int i = 0; i < inboundLoads.Length; i++)
			{
				Load load = inboundLoads[i] != null ? ResolveLoad(inboundLoads[i].loadId) : null;
				if (load != null && car.QuantityOfLoad(load).Item1 > 0f)
				{
					return false;
				}
			}
			return true;
		}

		private static bool CarHasWaybillTag(IOpsCar car, string tag)
		{
			return car.Waybill.HasValue && string.Equals(car.Waybill.Value.Tag, tag, StringComparison.OrdinalIgnoreCase);
		}

		private string BuildCounterKey(string key)
		{
			return "toolshed-selective:" + subIdentifier + ":" + key;
		}

		private string BuildReadyAtKey(SelectiveInterchangeOutboundRoute route)
		{
			return BuildCounterKey("ready-at:" + (!string.IsNullOrWhiteSpace(route.orderTag) ? route.orderTag : route.loadId));
		}

		private string BuildOrderTag(SelectiveInterchangeOutboundRoute route, Load load, IndustryComponent destination)
		{
			if (!string.IsNullOrWhiteSpace(route.orderTag))
			{
				return route.orderTag;
			}
			return "toolshed-selective:" + subIdentifier + ":" + load.id + ":" + destination.Identifier;
		}

		private string BuildEmptyOrderTag(SelectiveInterchangeEmptyCarRequest request, int index)
		{
			if (!string.IsNullOrWhiteSpace(request.orderTag))
			{
				return request.orderTag;
			}
			return "toolshed-selective-empty:" + subIdentifier + ":" + index + ":" + request.carTypeFilterQuery;
		}

		private string BuildInboundOrderTag(SelectiveInterchangeInboundLoadRule rule, int index)
		{
			if (!string.IsNullOrWhiteSpace(rule.orderTag))
			{
				return rule.orderTag;
			}
			return "toolshed-selective-inbound:" + subIdentifier + ":" + index + ":" + rule.loadId;
		}

		private static void RegisterCarModelSelector(string tag, SelectiveInterchangeEmptyCarRequest request)
		{
			if (request == null)
			{
				return;
			}
			SelectiveInterchangeCarModelRegistry.Register(tag, BuildCarModelIdentifiers(
				request.carModelIdentifier,
				request.carModelIdentifiers,
				request.modelIdentifier,
				request.modelIdentifiers,
				request.carPrototypeIdentifier,
				request.carPrototypeIdentifiers,
				request.carPrototypeId,
				request.carPrototypeIds,
				request.carDefinitionIdentifier,
				request.carDefinitionIdentifiers), request.carModelWeights);
		}

		private static void RegisterCarModelSelector(string tag, SelectiveInterchangeOutboundRoute route)
		{
			if (route == null)
			{
				return;
			}
			SelectiveInterchangeCarModelRegistry.Register(tag, BuildCarModelIdentifiers(
				route.carModelIdentifier,
				route.carModelIdentifiers,
				route.modelIdentifier,
				route.modelIdentifiers,
				route.carPrototypeIdentifier,
				route.carPrototypeIdentifiers,
				route.carPrototypeId,
				route.carPrototypeIds,
				route.carDefinitionIdentifier,
				route.carDefinitionIdentifiers), route.carModelWeights);
		}

		private static string[] BuildCarModelIdentifiers(
			string carModelIdentifier,
			string[] carModelIdentifiers,
			string modelIdentifier,
			string[] modelIdentifiers,
			string carPrototypeIdentifier,
			string[] carPrototypeIdentifiers,
			string carPrototypeId,
			string[] carPrototypeIds,
			string carDefinitionIdentifier,
			string[] carDefinitionIdentifiers)
		{
			List<string> values = new List<string>();
			AddCandidate(values, carModelIdentifier);
			AddCandidates(values, carModelIdentifiers);
			AddCandidate(values, modelIdentifier);
			AddCandidates(values, modelIdentifiers);
			AddCandidate(values, carPrototypeIdentifier);
			AddCandidates(values, carPrototypeIdentifiers);
			AddCandidate(values, carPrototypeId);
			AddCandidates(values, carPrototypeIds);
			AddCandidate(values, carDefinitionIdentifier);
			AddCandidates(values, carDefinitionIdentifiers);
			return values.ToArray();
		}

		private static void AddCandidates(List<string> values, string[] candidates)
		{
			if (candidates == null)
			{
				return;
			}
			for (int i = 0; i < candidates.Length; i++)
			{
				AddCandidate(values, candidates[i]);
			}
		}

		private static void AddCandidate(List<string> values, string candidate)
		{
			if (!string.IsNullOrWhiteSpace(candidate))
			{
				values.Add(candidate);
			}
		}

		private static string SafeIdentifier(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "load";
			}
			char[] chars = value.ToCharArray();
			for (int i = 0; i < chars.Length; i++)
			{
				if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
				{
					chars[i] = '-';
				}
			}
			return new string(chars);
		}

		private static void RefreshIndustryComponents(Industry industry)
		{
			if (industry == null || IndustryCachedComponentsField == null)
			{
				return;
			}
			IndustryCachedComponentsField.SetValue(industry, null);
		}

		private void LogWarning(string message)
		{
			Main.Warn("[SelectiveInterchange] " + name + ": " + message);
		}

		private void LogDebug(string message)
		{
			if (debugLogging)
			{
				Main.Log("[SelectiveInterchange] " + name + ": " + message);
			}
		}
	}

	[Serializable]
	public sealed class SelectiveInterchangeLoadRule
	{
		public bool enabled = true;
		public string loadId;
		public string componentId;
		public string displayName;
		public string carTypeFilterQuery = "*";
		public string[] trackSpanIds;
		public bool createIfMissing = true;
	}

	[Serializable]
	public sealed class SelectiveInterchangeInboundLoadRule
	{
		public bool enabled = true;
		public string loadId;
		public string componentId;
		public string displayName;
		public string carTypeFilterQuery = "*";
		public string[] trackSpanIds;
		public bool createIfMissing = true;
		public bool orderLoadedCars = true;
		public bool orderAwayEmpties = true;
		public string orderTag;
		public bool countOnlyTaggedCars;
		public string supplyCounterKey;
		public float supplyCreditsPerCar = 1f;
		public float maxSupplyCredits;
		public bool moveToBardoAfterUnload;
		public bool useVanillaIndustryUnloader;
		public float carTransferRate;
		public float storageConsumptionRate = 50000f;
		public float maxStorage;
	}

	[Serializable]
	public sealed class SelectiveInterchangeEmptyCarRequest
	{
		public bool enabled = true;
		public string carTypeFilterQuery = "*";
		public int carsPerService = 1;
		public int minCarsPerService;
		public int maxCarsPerService;
		public string originPositionId;
		public string[] originPositionIds;
		public string originIndustryId;
		public string[] originIndustryIds;
		public string originComponentId;
		public string[] originComponentIds;
		public SelectiveInterchangeWeightedOrigin[] originWeights;
		public string sourcePositionId;
		public string[] sourcePositionIds;
		public string sourceIndustryId;
		public string[] sourceIndustryIds;
		public string sourceComponentId;
		public string[] sourceComponentIds;
		public SelectiveInterchangeWeightedOrigin[] sourceWeights;
		public bool allowInactiveOriginFallback;
		public bool allowDisabledOriginFallback;
		public bool allowOriginAtThisInterchange;
		public string carModelIdentifier;
		public string[] carModelIdentifiers;
		public string modelIdentifier;
		public string[] modelIdentifiers;
		public string carPrototypeIdentifier;
		public string[] carPrototypeIdentifiers;
		public string carPrototypeId;
		public string[] carPrototypeIds;
		public string carDefinitionIdentifier;
		public string[] carDefinitionIdentifiers;
		public SelectiveInterchangeWeightedCarModel[] carModelWeights;
		public string orderTag;
		public bool countOnlyTaggedCars = true;
		public string equipmentCounterKey = "equipment";
		public float equipmentCreditsPerCar = 1f;
		public float maxEquipmentCredits;
		public bool noPayment;
	}

	[Serializable]
	public sealed class SelectiveInterchangeOutboundRoute
	{
		public bool enabled = true;
		public string loadId;
		public string destinationPositionId;
		public string[] destinationPositionIds;
		public string destinationIndustryId;
		public string[] destinationIndustryIds;
		public string destinationComponentId;
		public string[] destinationComponentIds;
		public string carTypeFilterQuery = "*";
		public int carsPerService = 1;
		public int minCarsPerService;
		public int maxCarsPerService;
		public string equipmentCounterKey;
		public float equipmentCreditsPerCar;
		public string supplyCounterKey;
		public float supplyCreditsPerCar;
		public float minReturnDelayDays;
		public float maxReturnDelayDays;
		public bool weekdaysOnly;
		public int[] productionDays;
		public float slowDayChance;
		public float slowDayMultiplier = 0.5f;
		public SelectiveInterchangeWeightedDestination[] destinationWeights;
		public bool allowInactiveDestinationFallback;
		public bool allowDisabledDestinationFallback;
		public string carModelIdentifier;
		public string[] carModelIdentifiers;
		public string modelIdentifier;
		public string[] modelIdentifiers;
		public string carPrototypeIdentifier;
		public string[] carPrototypeIdentifiers;
		public string carPrototypeId;
		public string[] carPrototypeIds;
		public string carDefinitionIdentifier;
		public string[] carDefinitionIdentifiers;
		public SelectiveInterchangeWeightedCarModel[] carModelWeights;
		public string orderTag;
		public bool noPayment;
	}

	[Serializable]
	public sealed class SelectiveInterchangeWeightedCarModel
	{
		public string carModelIdentifier;
		public string modelIdentifier;
		public string carPrototypeIdentifier;
		public string carPrototypeId;
		public string carDefinitionIdentifier;
		public int weight = 1;
	}

	[Serializable]
	public sealed class SelectiveInterchangeWeightedOrigin
	{
		public string originPositionId;
		public string originIndustryId;
		public string originComponentId;
		public string sourcePositionId;
		public string sourceIndustryId;
		public string sourceComponentId;
		public int weight = 1;
	}

	[Serializable]
	public sealed class SelectiveInterchangeWeightedDestination
	{
		public string destinationPositionId;
		public string destinationIndustryId;
		public string destinationComponentId;
		public int weight = 1;
	}

	[Serializable]
	public sealed class SelectiveInterchangeInitialCounter
	{
		public string key;
		public float value;
		public float maxValue;
	}
}
