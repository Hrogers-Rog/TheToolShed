using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Helpers;
using KeyValue.Runtime;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using RollingStock;
using Track;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Inspector-facing wrapper that turns vanilla Railroader loader pieces into a reusable service facility.
	/// This class deliberately configures CarLoadTargetLoader, CarLoaderSequencer, IndustryUnloader,
	/// and InterchangedIndustryLoader instead of replacing them with fuel-specific loader code.
	/// </summary>
	[DisallowMultipleComponent]
	public class UniversalServiceFacilityComponent : MonoBehaviour
	{
		private const float DefaultPurchaseDelayDays = 0.9583333f;

		// Used by the Harmony debug hook so transfer logging stays opt-in per configured facility.
		private static readonly Dictionary<CarLoadTargetLoader, UniversalServiceFacilityComponent> FacilityByLoader = new Dictionary<CarLoadTargetLoader, UniversalServiceFacilityComponent>();

		private static readonly HashSet<UniversalServiceFacilityComponent> ActiveFacilities = new HashSet<UniversalServiceFacilityComponent>();

		// Runtime-added industry components need the vanilla cached component list cleared before ops ticks see them.
		private static readonly FieldInfo IndustryCachedComponentsField = AccessTools.Field(typeof(Industry), "_cachedComponents");

		[Header("Service Load")]
		public string serviceLoadId = ServiceLoadIds.Water;

		public bool infiniteSupply = true;

		[Tooltip("Facility storage capacity in the load's native units. Used by configured industry receiving components.")]
		public float facilityCapacity = 8000f;

		[Tooltip("Inspector/debug mirror of live linked industry storage. To seed storage, use SeedLinkedIndustryStorage from the context menu.")]
		public float currentStorage;

		[Tooltip("Units per real second transferred by CarLoadTargetLoader.")]
		public float loadingRate = ServiceLoadIds.DefaultLiquidLoadingRate;

		public float serviceRadius = 0.2f;

		public float maximumSpeedMph = 5f;

		public TrackSpan serviceTrackSpan;

		public Industry linkedIndustry;

		public bool requirePlayerOwnedCars = true;

		[Header("Interchange")]
		public bool canPurchaseThroughInterchange;

		[Tooltip("Vanilla InterchangedIndustryLoader currently hard-codes roughly one day; this value is retained for future compatibility.")]
		public float purchaseDelayDays = DefaultPurchaseDelayDays;

		[Tooltip("Car type query used if this component configures receiving or interchange industry components.")]
		public string carTypeFilterQuery = "*";

		[Header("Animation Keys")]
		public string canLoadBoolKey = "canLoad";

		public string isLoadingBoolKey = "isLoading";

		public string requestLoadingBoolKey = "request";

		public string prepareLoadBoolKey = "prepareLoad";

		public string animateLoadBoolKey = "animateLoad";

		[Tooltip("When enabled, loading is allowed only while the configured service-condition key has the expected value. Use this for spouts/chutes that must be in position before transfer.")]
		public bool requireServiceCondition;

		public string serviceConditionBoolKey = "request";

		public bool serviceConditionExpectedValue = true;

		[Header("Component References")]
		public CarLoadTargetLoader serviceLoader;

		public CarLoaderSequencer serviceSequencer;

		public KeyValueObject keyValueObject;

		public IndustryUnloader receivingUnloader;

		public InterchangedIndustryLoader interchangedIndustryLoader;

		[Header("Options")]
		public bool configureReceivingUnloader;

		public bool configureInterchangeLoader;

		public bool createMissingIndustryComponents;

		public bool debugLogging;

		[Header("Service Target Search")]
		[Tooltip("Extends vanilla loader behavior for custom service scenery by searching nearby cars for matching tender load targets.")]
		public bool enableExtendedTenderSearch;

		[Tooltip("Game-space radius used to discover nearby cars when vanilla point loading misses a tender behind the locomotive.")]
		public float extendedSearchRadius = 12f;

		[Tooltip("Maximum flat distance from the service point to the matching CarLoadTarget.")]
		public float extendedLoadTargetRadius = 8f;

		private Load _load;

		private string _lastConfiguredSummary;

		private float _nextConfigureRetryTime;

		private readonly Dictionary<int, float> _nextTargetScanLogByCar = new Dictionary<int, float>();

		private readonly HashSet<Car> _extendedSearchCars = new HashSet<Car>();

		private float _nextExtendedLoadTime;

		private float _nextExtendedSearchDebugTime;

		private float _nextExtendedNoMatchLogTime;

		private float _lastVanillaTransferTime = -100f;

		private void OnEnable()
		{
			ActiveFacilities.Add(this);
			Configure();
		}

		private void OnDisable()
		{
			ActiveFacilities.Remove(this);
			if (serviceLoader != null)
			{
				FacilityByLoader.Remove(serviceLoader);
			}
		}

		private void Update()
		{
			// Asset-pack objects can enable before CarPrototypeLibrary is populated; retry lightly until the load resolves.
			if (!Main.Enabled)
			{
				return;
			}
			if (serviceLoader == null || serviceLoader.load == null)
			{
				if (Time.unscaledTime >= _nextConfigureRetryTime)
				{
					_nextConfigureRetryTime = Time.unscaledTime + 2f;
					Configure();
				}
			}
			RunExtendedTenderSearch();
		}

		private void OnValidate()
		{
			if (facilityCapacity < 0f)
			{
				facilityCapacity = 0f;
			}
			if (currentStorage < 0f)
			{
				currentStorage = 0f;
			}
			if (loadingRate < 0f)
			{
				loadingRate = 0f;
			}
			serviceRadius = Mathf.Clamp(serviceRadius, 0.1f, 1f);
			if (maximumSpeedMph < 0f)
			{
				maximumSpeedMph = 0f;
			}
			if (purchaseDelayDays <= 0f)
			{
				purchaseDelayDays = DefaultPurchaseDelayDays;
			}
		}

		[ContextMenu("Configure Service Facility")]
		public void Configure()
		{
			if (!Main.Enabled)
			{
				return;
			}
			_load = ResolveLoad();
			if (_load == null)
			{
				LogWarning("unknown load id '" + serviceLoadId + "'; service facility is not configured.");
				return;
			}

			ResolveCoreComponents();
			if (serviceLoader == null)
			{
				LogWarning("no CarLoadTargetLoader could be found or created.");
				return;
			}

			ConfigureCarLoadTargetLoader();
			ConfigureSequencer();
			ConfigureIndustryComponents();
			UpdateCurrentStorageMirror();
			RegisterLoader();
			LogConfigurationIfChanged();
		}

		[ContextMenu("Seed Linked Industry Storage")]
		public void SeedLinkedIndustryStorage()
		{
			_load = ResolveLoad();
			Industry source = ResolveLinkedIndustry();
			if (_load == null || source == null)
			{
				LogWarning("cannot seed storage without both a load and linked industry.");
				return;
			}
			source.Storage.SetStorage(_load, Mathf.Clamp(currentStorage, 0f, facilityCapacity), null);
			UpdateCurrentStorageMirror();
			LogDebug("seeded linked storage: load=" + _load.id + ", storage=" + currentStorage.ToString("0.###") + ", capacity=" + facilityCapacity.ToString("0.###"));
		}

		internal static bool TryGetForLoader(CarLoadTargetLoader loader, out UniversalServiceFacilityComponent facility)
		{
			facility = null;
			if (loader == null)
			{
				return false;
			}
			if (FacilityByLoader.TryGetValue(loader, out facility) && facility != null)
			{
				return true;
			}
			facility = loader.GetComponentInParent<UniversalServiceFacilityComponent>();
			if (facility == null)
			{
				return false;
			}
			facility.RegisterLoader();
			return true;
		}

		internal static float QuantityInSlot(Car car, int slotIndex)
		{
			if (car == null || slotIndex < 0)
			{
				return 0f;
			}
			CarLoadInfo? loadInfo = car.GetLoadInfo(slotIndex);
			return loadInfo != null ? loadInfo.GetValueOrDefault().Quantity : 0f;
		}

		internal static bool HasFiniteStorageFacilityFor(Industry industry, Load load)
		{
			if (industry == null || load == null)
			{
				return false;
			}
			foreach (UniversalServiceFacilityComponent facility in ActiveFacilities)
			{
				if (facility == null || facility.infiniteSupply)
				{
					continue;
				}
				Load facilityLoad = facility._load ?? facility.ResolveLoad();
				if (facilityLoad == null || !string.Equals(facilityLoad.id, load.id, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				Industry linked = facility.ResolveLinkedIndustry();
				if (linked == industry)
				{
					return true;
				}
			}
			return false;
		}

		internal void LogTransfer(CarLoadTargetLoader loader, Car car, LoadSlot loadSlot, int slotIndex, float beforeQuantity)
		{
			if (!debugLogging || loader == null || car == null || loadSlot == null)
			{
				return;
			}
			float afterQuantity = QuantityInSlot(car, slotIndex);
			float added = afterQuantity - beforeQuantity;
			if (Mathf.Abs(added) >= ServiceLoadIds.TransferLogThreshold)
			{
				_lastVanillaTransferTime = Time.unscaledTime;
			}
			if (Mathf.Abs(added) < ServiceLoadIds.TransferLogThreshold)
			{
				return;
			}
			UpdateCurrentStorageMirror();
			string loadId = (_load != null) ? _load.id : serviceLoadId;
			Main.Log("[ServiceFacility][Loader] transfer load=" + loadId +
				", target=" + car.DisplayName +
				", slot=" + slotIndex +
				", added=" + added.ToString("0.###") +
				", carQuantity=" + afterQuantity.ToString("0.###") +
				", storage=" + currentStorage.ToString("0.###") +
				", capacity=" + facilityCapacity.ToString("0.###"));
		}

		internal bool CanVanillaLoaderTransfer()
		{
			if (IsServiceConditionMet())
			{
				return true;
			}
			if (debugLogging)
			{
				LogDebug("[ServiceFacility][Loader] transfer blocked because service condition is not met: key=" +
					serviceConditionBoolKey + ", expected=" + serviceConditionExpectedValue);
			}
			return false;
		}

		internal void LogLoadTargetScan(CarLoadTargetLoader loader, Car car, Vector3 point)
		{
			if (!debugLogging || loader == null || car == null)
			{
				return;
			}

			int key = car.GetInstanceID();
			float nextLogTime;
			if (_nextTargetScanLogByCar.TryGetValue(key, out nextLogTime) && Time.unscaledTime < nextLogTime)
			{
				return;
			}
			_nextTargetScanLogByCar[key] = Time.unscaledTime + 5f;

			Load loadToMatch = loader.load;
			CarLoadTarget[] targets = car.GetComponentsInChildren<CarLoadTarget>();
			string message = "[ServiceFacility][Loader] target scan load=" + (loadToMatch != null ? loadToMatch.id : "<null>") +
				", car=" + car.DisplayName +
				", targets=" + targets.Length +
				", loaderRadius=" + loader.radius.ToString("0.###") +
				", point=" + FormatVector(point);

			if (targets.Length == 0)
			{
				LogDebug(message + ", result=no CarLoadTarget components");
				return;
			}

			Matrix4x4 transformMatrix = car.GetTransformMatrix(TrainController.Shared.graph);
			List<string> targetDescriptions = new List<string>();
			for (int i = 0; i < targets.Length; i++)
			{
				CarLoadTarget target = targets[i];
				if (target == null)
				{
					continue;
				}

				bool validSlot = target.slotIndex >= 0 && target.slotIndex < car.Definition.LoadSlots.Count;
				string required = "<bad-slot>";
				float capacity = 0f;
				float quantity = 0f;
				if (validSlot)
				{
					LoadSlot slot = car.Definition.LoadSlots[target.slotIndex];
					required = slot.RequiredLoadIdentifier;
					capacity = slot.MaximumCapacity;
					quantity = QuantityInSlot(car, target.slotIndex);
				}

				Vector3 localPoint = car.transform.InverseTransformPoint(target.transform.position);
				Vector3 targetPoint = transformMatrix.MultiplyPoint3x4(localPoint);
				float flatDistance = FlatDistance(point, targetPoint);
				float allowedDistance = target.radius + loader.radius;
				bool loadMatches = validSlot && loadToMatch != null && string.Equals(required, loadToMatch.id, StringComparison.OrdinalIgnoreCase);
				bool distanceMatches = flatDistance <= allowedDistance;
				targetDescriptions.Add(target.name +
					"[slot=" + target.slotIndex +
					", req=" + required +
					", qty=" + quantity.ToString("0.###") + "/" + capacity.ToString("0.###") +
					", targetRadius=" + target.radius.ToString("0.###") +
					", flatDistance=" + flatDistance.ToString("0.###") + "/" + allowedDistance.ToString("0.###") +
					", loadMatch=" + loadMatches +
					", distanceMatch=" + distanceMatches + "]");
			}

			LogDebug(message + ", targets: " + string.Join(" | ", targetDescriptions.ToArray()));
		}

		internal void LogLoadTargetResult(CarLoadTargetLoader loader, Car car, LoadSlot loadSlot, int slotIndex)
		{
			if (!debugLogging || loader == null || car == null)
			{
				return;
			}
			if (loadSlot == null)
			{
				return;
			}

			LogDebug("[ServiceFacility][Loader] target matched load=" + (loader.load != null ? loader.load.id : "<null>") +
				", car=" + car.DisplayName +
				", slot=" + slotIndex +
				", required=" + loadSlot.RequiredLoadIdentifier +
				", quantity=" + QuantityInSlot(car, slotIndex).ToString("0.###") +
				", capacity=" + loadSlot.MaximumCapacity.ToString("0.###"));
		}

		private void RunExtendedTenderSearch()
		{
			if (!enableExtendedTenderSearch || serviceLoader == null || keyValueObject == null || _load == null)
			{
				return;
			}
			if (!IsServiceConditionMet())
			{
				SetExtendedLoading(false);
				return;
			}
			if (!keyValueObject[canLoadBoolKey].BoolValue)
			{
				SetExtendedLoading(false);
				return;
			}
			if (Time.unscaledTime < _nextExtendedLoadTime)
			{
				return;
			}
			_nextExtendedLoadTime = Time.unscaledTime + 1f;
			if (Time.unscaledTime - _lastVanillaTransferTime < 1.25f)
			{
				return;
			}

			Industry source = infiniteSupply ? null : ResolveLinkedIndustry();
			if (source != null && source.Storage.QuantityInStorage(_load, null) <= _load.ZeroThreshold)
			{
				SetExtendedLoading(false);
				return;
			}

			TrainController controller = TrainController.Shared;
			if (controller == null || controller.graph == null)
			{
				return;
			}

			Vector3 point = WorldTransformer.WorldToGame(serviceLoader.transform.position);
			_extendedSearchCars.Clear();
			controller.CheckForCarsAtPoint(point, Mathf.Max(extendedSearchRadius, serviceLoader.radius + 2f), _extendedSearchCars, null);
			ExtendedLoadCandidate best = FindBestExtendedLoadCandidate(point, source);
			if (!best.IsValid)
			{
				SetExtendedLoading(false);
				LogExtendedSearchNoMatch(point);
				return;
			}

			float before = QuantityInSlot(best.Car, best.SlotIndex);
			float remainingCapacity = best.LoadSlot.MaximumCapacity - before;
			if (remainingCapacity <= _load.ZeroThreshold)
			{
				SetExtendedLoading(false);
				return;
			}

			float amount = Mathf.Min(Mathf.Max(loadingRate, 0f), remainingCapacity);
			if (source != null)
			{
				amount = source.Storage.RemoveFromStorage(_load, amount, null);
			}
			if (amount <= _load.ZeroThreshold)
			{
				SetExtendedLoading(false);
				return;
			}

			CarLoadInfo current = best.Car.GetLoadInfo(best.SlotIndex) ?? new CarLoadInfo(_load.id, 0f);
			best.Car.SetLoadInfo(best.SlotIndex, new CarLoadInfo?(new CarLoadInfo(_load.id, current.Quantity + amount)));
			SetExtendedLoading(true);
			UpdateCurrentStorageMirror();
			LogDebug("[ServiceFacility][Loader] extended transfer load=" + _load.id +
				", car=" + best.Car.DisplayName +
				", slot=" + best.SlotIndex +
				", added=" + amount.ToString("0.###") +
				", carQuantity=" + (current.Quantity + amount).ToString("0.###") +
				", distance=" + best.Distance.ToString("0.###") +
				", storage=" + currentStorage.ToString("0.###") +
				", capacity=" + facilityCapacity.ToString("0.###"));
		}

		private ExtendedLoadCandidate FindBestExtendedLoadCandidate(Vector3 point, Industry source)
		{
			ExtendedLoadCandidate best = new ExtendedLoadCandidate();
			float maxDistance = Mathf.Max(extendedLoadTargetRadius, serviceLoader.radius);
			bool emitDetails = debugLogging && Time.unscaledTime >= _nextExtendedSearchDebugTime;
			List<string> details = emitDetails ? new List<string>() : null;
			foreach (Car car in _extendedSearchCars)
			{
				if (car == null)
				{
					continue;
				}
				string carName = car.DisplayName;
				float speedMph = Mathf.Abs(car.velocity) * 2.23694f;
				if (speedMph > maximumSpeedMph)
				{
					if (details != null)
					{
						details.Add(carName + "[skip=speed " + speedMph.ToString("0.###") + " mph]");
					}
					continue;
				}
				if (requirePlayerOwnedCars && !car.IsOwnedByPlayer)
				{
					if (details != null)
					{
						details.Add(carName + "[skip=not-player-owned]");
					}
					continue;
				}

				Matrix4x4 transformMatrix = car.GetTransformMatrix(TrainController.Shared.graph);
				CarLoadTarget[] targets = car.GetComponentsInChildren<CarLoadTarget>();
				List<string> targetDetails = details != null ? new List<string>() : null;
				if (targetDetails != null && targets.Length == 0)
				{
					targetDetails.Add("targets=0");
				}
				for (int i = 0; i < targets.Length; i++)
				{
					CarLoadTarget target = targets[i];
					if (target == null)
					{
						continue;
					}

					Vector3 localPoint = car.transform.InverseTransformPoint(target.transform.position);
					Vector3 targetPoint = transformMatrix.MultiplyPoint3x4(localPoint);
					float distance = FlatDistance(point, targetPoint);
					float allowedDistance = Mathf.Max(maxDistance, target.radius + serviceLoader.radius);
					bool validSlot = target.slotIndex >= 0 && target.slotIndex < car.Definition.LoadSlots.Count;
					string requiredLoad = "<bad-slot>";
					float quantity = 0f;
					float capacity = 0f;
					bool loadMatches = false;
					bool hasRoom = false;
					LoadSlot slot = null;
					if (validSlot)
					{
						slot = car.Definition.LoadSlots[target.slotIndex];
						requiredLoad = slot.RequiredLoadIdentifier;
						quantity = QuantityInSlot(car, target.slotIndex);
						capacity = slot.MaximumCapacity;
						loadMatches = string.Equals(requiredLoad, _load.id, StringComparison.OrdinalIgnoreCase);
						hasRoom = slot.MaximumCapacity <= 0f || quantity / slot.MaximumCapacity <= 0.999f;
					}
					bool distanceMatches = distance <= allowedDistance;

					if (targetDetails != null)
					{
						targetDetails.Add(target.name +
							"[slot=" + target.slotIndex +
							", req=" + requiredLoad +
							", qty=" + quantity.ToString("0.###") + "/" + capacity.ToString("0.###") +
							", distance=" + distance.ToString("0.###") + "/" + allowedDistance.ToString("0.###") +
							", loadMatch=" + loadMatches +
							", distanceMatch=" + distanceMatches +
							", hasRoom=" + hasRoom + "]");
					}

					if (!validSlot || !loadMatches || !hasRoom || !distanceMatches)
					{
						continue;
					}
					if (!best.IsValid || distance < best.Distance)
					{
							best = new ExtendedLoadCandidate(car, slot, target.slotIndex, distance);
						}
					}
				if (details != null)
				{
					details.Add(carName + "[" + string.Join(" | ", targetDetails.ToArray()) + "]");
				}
			}
			if (details != null)
			{
				_nextExtendedSearchDebugTime = Time.unscaledTime + 5f;
				LogDebug("[ServiceFacility][Loader] extended candidates load=" + _load.id +
					", cars=" + _extendedSearchCars.Count +
					", point=" + FormatVector(point) +
					", searchRadius=" + extendedSearchRadius.ToString("0.###") +
					", targetRadius=" + extendedLoadTargetRadius.ToString("0.###") +
					": " + (details.Count > 0 ? string.Join(" || ", details.ToArray()) : "(none)"));
			}
			return best;
		}

		private void LogExtendedSearchNoMatch(Vector3 point)
		{
			if (!debugLogging || Time.unscaledTime < _nextExtendedNoMatchLogTime)
			{
				return;
			}
			_nextExtendedNoMatchLogTime = Time.unscaledTime + 5f;
			LogDebug("[ServiceFacility][Loader] extended search found no matching target load=" + _load.id +
				", cars=" + _extendedSearchCars.Count +
				", point=" + FormatVector(point) +
				", searchRadius=" + extendedSearchRadius.ToString("0.###") +
				", targetRadius=" + extendedLoadTargetRadius.ToString("0.###"));
		}

		private void SetExtendedLoading(bool loading)
		{
			if (keyValueObject == null || string.IsNullOrEmpty(isLoadingBoolKey))
			{
				return;
			}
			keyValueObject[isLoadingBoolKey] = Value.Bool(loading);
		}

		private bool IsServiceConditionMet()
		{
			if (!requireServiceCondition)
			{
				return true;
			}
			if (keyValueObject == null || string.IsNullOrEmpty(serviceConditionBoolKey))
			{
				return false;
			}
			return keyValueObject[serviceConditionBoolKey].BoolValue == serviceConditionExpectedValue;
		}

		private Load ResolveLoad()
		{
			if (string.IsNullOrEmpty(serviceLoadId))
			{
				return null;
			}
			CarPrototypeLibrary library = CarPrototypeLibrary.instance;
			return library != null ? library.LoadForId(serviceLoadId) : null;
		}

		private void ResolveCoreComponents()
		{
			// A prefab may provide fully wired vanilla components, but simple scenery assets can rely on this wrapper
			// to add the minimum missing pieces at runtime.
			if (keyValueObject == null)
			{
				keyValueObject = GetComponentInChildren<KeyValueObject>(true);
			}
			if (keyValueObject == null)
			{
				keyValueObject = gameObject.AddComponent<KeyValueObject>();
			}
			if (serviceLoader == null)
			{
				serviceLoader = GetComponentInChildren<CarLoadTargetLoader>(true);
			}
			if (serviceLoader == null)
			{
				serviceLoader = gameObject.AddComponent<CarLoadTargetLoader>();
			}
			if (serviceSequencer == null)
			{
				serviceSequencer = GetComponentInChildren<CarLoaderSequencer>(true);
			}
		}

		private void ConfigureCarLoadTargetLoader()
		{
			// Null sourceIndustry is the vanilla signal for infinite supply. A linked industry makes loading finite
			// and drains the same persisted storage used by IndustryUnloader and industry panels.
			serviceLoader.load = _load;
			serviceLoader.sourceIndustry = infiniteSupply ? null : ResolveLinkedIndustry();
			serviceLoader.outputRate = loadingRate;
			serviceLoader.maximumSpeedInMph = maximumSpeedMph;
			serviceLoader.radius = Mathf.Clamp(serviceRadius, 0.1f, 1f);
			serviceLoader.keyValueObject = keyValueObject;
			serviceLoader.canLoadBoolKey = canLoadBoolKey;
			serviceLoader.isLoadingBoolKey = isLoadingBoolKey;
			serviceLoader.onlyLoadPlayerCars = requirePlayerOwnedCars;
		}

		private void ConfigureSequencer()
		{
			if (serviceSequencer == null)
			{
				return;
			}
			serviceSequencer.keyValueObject = keyValueObject;
			serviceSequencer.readWantsLoadingKey = requestLoadingBoolKey;
			serviceSequencer.readIsLoadingKey = isLoadingBoolKey;
			serviceSequencer.writeCanLoadKey = canLoadBoolKey;
			serviceSequencer.writePrepareLoadKey = prepareLoadBoolKey;
			serviceSequencer.writeAnimateLoadKey = animateLoadBoolKey;
		}

		private void ConfigureIndustryComponents()
		{
			Industry source = ResolveLinkedIndustry();
			if (source == null)
			{
				if (!infiniteSupply)
				{
					LogWarning("finite service requested but no linked industry was found; loader will behave as infinite until linkedIndustry is assigned.");
				}
				return;
			}

			if (configureReceivingUnloader)
			{
				ConfigureReceivingUnloader(source);
			}
			if (canPurchaseThroughInterchange && configureInterchangeLoader)
			{
				ConfigureInterchangedLoader(source);
			}
			RefreshIndustryComponents(source);
		}

		private void ConfigureReceivingUnloader(Industry source)
		{
			// This is the delivery side of the service loop: loaded cars unload into facility storage,
			// then the physical loader drains that storage into locomotives/tenders.
			if (receivingUnloader == null)
			{
				receivingUnloader = FindIndustryComponent<IndustryUnloader>(source);
			}
			if (receivingUnloader == null && createMissingIndustryComponents)
			{
				receivingUnloader = source.gameObject.AddComponent<IndustryUnloader>();
				receivingUnloader.subIdentifier = SafeSubIdentifier("receive");
			}
			if (receivingUnloader == null)
			{
				LogWarning("receiving unloader was requested but no IndustryUnloader exists. Add one manually or enable createMissingIndustryComponents.");
				return;
			}
			receivingUnloader.load = _load;
			receivingUnloader.maxStorage = Mathf.Max(facilityCapacity, _load.ZeroThreshold);
			receivingUnloader.storageConsumptionRate = 0f;
			receivingUnloader.carUnloadRate = Mathf.Max(receivingUnloader.carUnloadRate, _load.NominalQuantityPerCarLoad);
			receivingUnloader.orderLoads = false;
			receivingUnloader.orderAwayEmpties = true;
			ApplyIndustryComponentDefaults(receivingUnloader);
		}

		private void ConfigureInterchangedLoader(Industry source)
		{
			// Water should stay local/infinite/finite storage only; vanilla interchange purchase is for billable loads.
			if (string.Equals(serviceLoadId, ServiceLoadIds.Water, StringComparison.OrdinalIgnoreCase))
			{
				LogWarning("water is not configured for interchange purchase.");
				return;
			}
			if (interchangedIndustryLoader == null)
			{
				interchangedIndustryLoader = FindIndustryComponent<InterchangedIndustryLoader>(source);
			}
			if (interchangedIndustryLoader == null && createMissingIndustryComponents)
			{
				interchangedIndustryLoader = source.gameObject.AddComponent<InterchangedIndustryLoader>();
				interchangedIndustryLoader.subIdentifier = SafeSubIdentifier("interchange");
			}
			if (interchangedIndustryLoader == null)
			{
				LogWarning("interchange purchase was requested but no InterchangedIndustryLoader exists. Add one manually or enable createMissingIndustryComponents.");
				return;
			}
			interchangedIndustryLoader.load = _load;
			ApplyIndustryComponentDefaults(interchangedIndustryLoader);
			if (Mathf.Abs(purchaseDelayDays - DefaultPurchaseDelayDays) > 0.001f)
			{
				LogDebug("purchaseDelayDays=" + purchaseDelayDays.ToString("0.###") + " retained in config, but vanilla InterchangedIndustryLoader uses roughly one day.");
			}
		}

		private T FindIndustryComponent<T>(Industry source) where T : IndustryComponent
		{
			T[] components = source.GetComponentsInChildren<T>(true);
			for (int i = 0; i < components.Length; i++)
			{
				T component = components[i];
				if (component == null)
				{
					continue;
				}
				Load componentLoad = LoadForIndustryComponent(component);
				if (componentLoad == null || componentLoad == _load || string.Equals(componentLoad.id, _load.id, StringComparison.OrdinalIgnoreCase))
				{
					return component;
				}
			}
			return null;
		}

		private static Load LoadForIndustryComponent(IndustryComponent component)
		{
			IndustryUnloader unloader = component as IndustryUnloader;
			if (unloader != null)
			{
				return unloader.load;
			}
			IndustryLoaderBase loaderBase = component as IndustryLoaderBase;
			if (loaderBase != null)
			{
				return loaderBase.load;
			}
			InterchangedIndustryLoader interchanged = component as InterchangedIndustryLoader;
			return interchanged != null ? interchanged.load : null;
		}

		private void ApplyIndustryComponentDefaults(IndustryComponent component)
		{
			if (component == null)
			{
				return;
			}
			component.carTypeFilter = new CarTypeFilter(string.IsNullOrWhiteSpace(carTypeFilterQuery) ? "*" : carTypeFilterQuery);
			if (serviceTrackSpan != null)
			{
				// Industry ops use track spans to find standing cars; the physical loader still uses point/radius.
				component.trackSpans = new TrackSpan[]
				{
					serviceTrackSpan
				};
			}
			component.sharedStorage = true;
		}

		private Industry ResolveLinkedIndustry()
		{
			if (linkedIndustry != null)
			{
				return linkedIndustry;
			}
			linkedIndustry = GetComponentInParent<Industry>();
			return linkedIndustry;
		}

		private void RegisterLoader()
		{
			if (serviceLoader == null)
			{
				return;
			}
			FacilityByLoader[serviceLoader] = this;
		}

		private void UpdateCurrentStorageMirror()
		{
			Industry source = ResolveLinkedIndustry();
			if (_load == null || source == null)
			{
				return;
			}
			currentStorage = source.Storage.QuantityInStorage(_load, null);
		}

		private static void RefreshIndustryComponents(Industry industry)
		{
			if (industry == null || IndustryCachedComponentsField == null)
			{
				return;
			}
			IndustryCachedComponentsField.SetValue(industry, null);
		}

		private string SafeSubIdentifier(string suffix)
		{
			string loadId = string.IsNullOrEmpty(serviceLoadId) ? "load" : serviceLoadId.Replace(' ', '-');
			return "service-" + loadId + "-" + suffix;
		}

		private void LogConfigurationIfChanged()
		{
			if (!debugLogging)
			{
				return;
			}
			string sourceName = "infinite";
			Industry source = ResolveLinkedIndustry();
			if (!infiniteSupply)
			{
				sourceName = source != null ? source.identifier : "<missing industry>";
			}
			string summary = "load=" + _load.id +
				", source=" + sourceName +
				", rate=" + loadingRate.ToString("0.###") +
				", radius=" + serviceRadius.ToString("0.###") +
				", maxSpeedMph=" + maximumSpeedMph.ToString("0.###") +
				", capacity=" + facilityCapacity.ToString("0.###") +
				", storage=" + currentStorage.ToString("0.###") +
				", playerCarsOnly=" + requirePlayerOwnedCars;
			if (summary == _lastConfiguredSummary)
			{
				return;
			}
			_lastConfiguredSummary = summary;
			Main.Log("[ServiceFacility] configured " + name + ": " + summary);
		}

		private void LogWarning(string message)
		{
			Main.Warn("[ServiceFacility] " + name + ": " + message);
		}

		private void LogDebug(string message)
		{
			if (debugLogging)
			{
				Main.Log("[ServiceFacility] " + name + ": " + message);
			}
		}

		private static float FlatDistance(Vector3 a, Vector3 b)
		{
			a.y = 0f;
			b.y = 0f;
			return Vector3.Distance(a, b);
		}

		private static string FormatVector(Vector3 value)
		{
			return "(" + value.x.ToString("0.###") + ", " + value.y.ToString("0.###") + ", " + value.z.ToString("0.###") + ")";
		}

		private struct ExtendedLoadCandidate
		{
			public readonly Car Car;
			public readonly LoadSlot LoadSlot;
			public readonly int SlotIndex;
			public readonly float Distance;

			public bool IsValid
			{
				get { return Car != null && LoadSlot != null && SlotIndex >= 0; }
			}

			public ExtendedLoadCandidate(Car car, LoadSlot loadSlot, int slotIndex, float distance)
			{
				Car = car;
				LoadSlot = loadSlot;
				SlotIndex = slotIndex;
				Distance = distance;
			}
		}
	}
}
