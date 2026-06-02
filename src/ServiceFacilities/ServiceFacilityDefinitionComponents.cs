using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JsonSubTypes;
using Model;
using Model.Definition;
using Model.Definition.Data;
using UI.CarEditor;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Editor-authored storage settings for a service facility scenery asset.
	/// This is intentionally only the asset-side default; the live storage remains the vanilla Industry storage.
	/// </summary>
	[Component(ComponentDefinitionMask.Scenery, ComponentLifetime.Model)]
	public sealed class ToolshedServiceStorageComponent : Model.Definition.Component
	{
		public override string Kind { get; } = "ToolshedServiceStorage";

		public string FacilityId { get; set; } = "";

		public string StorageId { get; set; } = "";

		public string ServiceLoadId { get; set; } = "";

		public bool InfiniteSupply { get; set; }

		public float FacilityCapacity { get; set; } = 10000f;

		public float InitialStorage { get; set; }

		public float DefaultLoadingRate { get; set; } = 1f;

		public bool ShowStorageTooltip { get; set; } = true;

		public string DisplayTitle { get; set; } = "";

		public float MaxPickDistance { get; set; } = 50f;

		public float InteractionRadius { get; set; } = 1f;

		public bool UseBoxInteractionCollider { get; set; }

		public Vector3 InteractionBoxCenter { get; set; } = Vector3.zero;

		public Vector3 InteractionBoxSize { get; set; } = Vector3.zero;

		public bool EditorPreviewEnabled { get; set; }

		public bool EditorPreviewShowClickBox { get; set; } = true;

		public string StorageAnimationMapKey { get; set; } = "";

		public string StorageAnimationLoadId { get; set; } = "";

		public float StorageAnimationCapacity { get; set; }

		public bool StorageAnimationInvert { get; set; }

		public bool StorageAnimationUseTransformFallback { get; set; }

		public string StorageAnimationFallbackTransformName { get; set; } = "";

		public float StorageAnimationEmptyLocalY { get; set; }

		public float StorageAnimationFullLocalY { get; set; }

		public float StorageAnimationEmptyLocalScaleZ { get; set; } = 1f;

		public float StorageAnimationFullLocalScaleZ { get; set; } = 1f;

		public bool DebugLogging { get; set; }
	}

	/// <summary>
	/// Editor-authored load point for one physical chute, pipe, standpipe, or hose.
	/// Add one component per outlet. Multiple load points can share the same ToolshedServiceStorageComponent.
	/// </summary>
	[Component(ComponentDefinitionMask.Scenery, ComponentLifetime.Model)]
	public sealed class ToolshedServiceLoadPointComponent : Model.Definition.Component
	{
		public override string Kind { get; } = "ToolshedServiceLoadPoint";

		public string FacilityId { get; set; } = "";

		public string StorageId { get; set; } = "";

		public string LoadPointId { get; set; } = "";

		public string ServiceLoadId { get; set; } = "";

		public float LoadingRate { get; set; } = 1f;

		public float ServiceRadius { get; set; } = 0.65f;

		public float MaximumSpeedMph { get; set; } = 5f;

		public bool RequirePlayerOwnedCars { get; set; } = true;

		public bool EnableExtendedTenderSearch { get; set; } = true;

		public float ExtendedSearchRadius { get; set; } = 8f;

		public float ExtendedLoadTargetRadius { get; set; } = 3f;

		public bool UseServiceTargetBox { get; set; }

		public Vector3 ServiceTargetBoxCenter { get; set; } = Vector3.zero;

		public Vector3 ServiceTargetBoxSize { get; set; } = Vector3.zero;

		public bool RestrictLoadingToServiceTrackSpan { get; set; }

		public float ServiceTrackRouteLimit { get; set; } = 80f;

		public string DisplayTitle { get; set; } = "";

		public string MessageWhenActive { get; set; } = "Raise";

		public string MessageWhenInactive { get; set; } = "Lower";

		public float MaxPickDistance { get; set; } = 50f;

		public float InteractionRadius { get; set; } = 0.45f;

		public bool UseBoxInteractionCollider { get; set; }

		public Vector3 InteractionBoxCenter { get; set; } = Vector3.zero;

		public Vector3 InteractionBoxSize { get; set; } = Vector3.zero;

		public Vector3 LoaderLocalPosition { get; set; } = Vector3.zero;

		public Vector3 LoaderLocalRotation { get; set; } = Vector3.zero;

		public string AnimationMapKey { get; set; } = "";

		public float AnimationSpeed { get; set; } = 1f;

		public bool AnimationInvert { get; set; }

		public bool RequireLoweredBeforeLoading { get; set; } = true;

		public bool CreateParticleSystem { get; set; }

		public bool CreateVisibleStream { get; set; }

		public string ExistingEffectObjectName { get; set; } = "";

		public string EffectBoolKey { get; set; } = "animateLoad";

		public float EffectEmissionRate { get; set; } = 80f;

		public float EffectStartLifetime { get; set; } = 0.35f;

		public float EffectStartSpeed { get; set; } = 1f;

		/// <summary>
		/// Visual-only speed for authored chute/chunk streams. Leave zero to use EffectStartSpeed.
		/// </summary>
		public float FlowAnimationSpeed { get; set; }

		public float EffectStartSize { get; set; } = 0.045f;

		public float EffectGravityModifier { get; set; } = 1f;

		public Vector3 EffectColorRgb { get; set; } = new Vector3(0.08f, 0.06f, 0.04f);

		public float EffectAlpha { get; set; } = 0.9f;

		public Vector3 EffectLocalEuler { get; set; } = new Vector3(90f, 0f, 0f);

		public float StreamLength { get; set; } = 1f;

		public float StreamWidth { get; set; } = 0.06f;

		public bool StreamUsesWorldDown { get; set; } = true;

		/// <summary>
		/// Local start offset for chute-aligned streams. Leave zero for pipe/tank streams.
		/// </summary>
		public Vector3 StreamLocalStart { get; set; } = Vector3.zero;

		/// <summary>
		/// Local end offset for chute-aligned streams. Zero means the load point origin.
		/// </summary>
		public Vector3 StreamLocalEnd { get; set; } = Vector3.zero;

		public bool DebugOriginMarker { get; set; }

		public bool EditorPreviewEnabled { get; set; }

		public bool EditorPreviewAnimation { get; set; } = true;

		public float EditorPreviewAnimationPosition { get; set; }

		public bool EditorPreviewFlow { get; set; } = true;

		public bool EditorPreviewShowOrigin { get; set; } = true;

		public bool EditorPreviewShowClickBox { get; set; } = true;

		public bool EditorPreviewShowServiceRadius { get; set; } = true;

		public bool DebugLogging { get; set; }
	}

	public sealed class ServiceFacilityStorageAuthoring : MonoBehaviour
	{
		public string facilityId;
		public string storageId;
		public string serviceLoadId;
		public bool infiniteSupply;
		public float facilityCapacity;
		public float initialStorage;
		public float defaultLoadingRate;
		public bool showStorageTooltip = true;
		public string displayTitle;
		public float maxPickDistance = 50f;
		public float interactionRadius = 1f;
		public bool useBoxInteractionCollider;
		public Vector3 interactionBoxCenter;
		public Vector3 interactionBoxSize;
		public string storageAnimationMapKey;
		public string storageAnimationLoadId;
		public float storageAnimationCapacity;
		public bool storageAnimationInvert;
		public bool storageAnimationUseTransformFallback;
		public string storageAnimationFallbackTransformName;
		public float storageAnimationEmptyLocalY;
		public float storageAnimationFullLocalY;
		public float storageAnimationEmptyLocalScaleZ = 1f;
		public float storageAnimationFullLocalScaleZ = 1f;
		public bool debugLogging;

		public bool HasStorageAnimation
		{
			get { return !string.IsNullOrWhiteSpace(storageAnimationMapKey); }
		}

		public bool Matches(string requestedFacilityId, string requestedStorageId, string requestedLoadId)
		{
			if (!string.IsNullOrWhiteSpace(requestedFacilityId) &&
				!string.Equals(facilityId ?? "", requestedFacilityId, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(requestedStorageId) &&
				!string.Equals(EffectiveStorageId, requestedStorageId, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return string.IsNullOrWhiteSpace(requestedLoadId) ||
				string.IsNullOrWhiteSpace(serviceLoadId) ||
				string.Equals(serviceLoadId, requestedLoadId, StringComparison.OrdinalIgnoreCase);
		}

		public string EffectiveStorageId
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(storageId))
				{
					return storageId;
				}
				return !string.IsNullOrWhiteSpace(serviceLoadId) ? serviceLoadId : name;
			}
		}
	}

	public sealed class ServiceFacilityLoadPointAuthoring : MonoBehaviour
	{
		public string facilityId;
		public string storageId;
		public string loadPointId;
		public string serviceLoadId;
		public float loadingRate;
		public float serviceRadius;
		public float maximumSpeedMph;
		public bool requirePlayerOwnedCars = true;
		public bool enableExtendedTenderSearch = true;
		public float extendedSearchRadius = 8f;
		public float extendedLoadTargetRadius = 3f;
		public bool useServiceTargetBox;
		public Vector3 serviceTargetBoxCenter;
		public Vector3 serviceTargetBoxSize;
		public bool restrictLoadingToServiceTrackSpan;
		public float serviceTrackRouteLimit = 80f;
		public string displayTitle;
		public string messageWhenActive = "Raise";
		public string messageWhenInactive = "Lower";
		public float maxPickDistance = 50f;
		public float interactionRadius = 0.45f;
		public bool useBoxInteractionCollider;
		public Vector3 interactionBoxCenter;
		public Vector3 interactionBoxSize;
		public Vector3 loaderLocalPosition;
		public Vector3 loaderLocalRotation;
		public string animationMapKey;
		public float animationSpeed = 1f;
		public bool animationInvert;
		public bool requireLoweredBeforeLoading = true;
		public bool createParticleSystem;
		public bool createVisibleStream;
		public string existingEffectObjectName;
		public string effectBoolKey = "animateLoad";
		public float effectEmissionRate = 80f;
		public float effectStartLifetime = 0.35f;
		public float effectStartSpeed = 1f;
		public float flowAnimationSpeed;
		public float effectStartSize = 0.045f;
		public float effectGravityModifier = 1f;
		public Vector3 effectColorRgb = new Vector3(0.08f, 0.06f, 0.04f);
		public float effectAlpha = 0.9f;
		public Vector3 effectLocalEuler = new Vector3(90f, 0f, 0f);
		public float streamLength = 1f;
		public float streamWidth = 0.06f;
		public bool streamUsesWorldDown = true;
		public Vector3 streamLocalStart;
		public Vector3 streamLocalEnd;
		public bool debugOriginMarker;
		public bool debugLogging;
		public string flowOriginId;

		public string EffectiveLoadPointId
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(loadPointId))
				{
					return loadPointId;
				}
				return !string.IsNullOrWhiteSpace(name) ? name : "load-point";
			}
		}

		public bool HasParticleEffect
		{
			get
			{
				return createParticleSystem || createVisibleStream || !string.IsNullOrWhiteSpace(existingEffectObjectName);
			}
		}
	}

	[HarmonyPatch(typeof(ComponentFactory), "BuildComponent")]
	internal static class ToolshedServiceDefinitionComponentBuilderPatch
	{
		private static bool Prefix(Model.Definition.Component component, ComponentBuilderContext ctx)
		{
			ToolshedServiceStorageComponent storage = component as ToolshedServiceStorageComponent;
			if (storage != null)
			{
				BuildStorage(ctx, storage);
				return false;
			}

			ToolshedServiceLoadPointComponent loadPoint = component as ToolshedServiceLoadPointComponent;
			if (loadPoint != null)
			{
				BuildLoadPoint(ctx, loadPoint);
				return false;
			}

			return true;
		}

		private static void BuildStorage(ComponentBuilderContext ctx, ToolshedServiceStorageComponent component)
		{
			ServiceFacilityStorageAuthoring authoring = ctx.GameObject.GetComponent<ServiceFacilityStorageAuthoring>() ??
				ctx.GameObject.AddComponent<ServiceFacilityStorageAuthoring>();
			authoring.facilityId = Clean(component.FacilityId);
			authoring.storageId = Clean(component.StorageId);
			authoring.serviceLoadId = Clean(component.ServiceLoadId);
			authoring.infiniteSupply = component.InfiniteSupply;
			authoring.facilityCapacity = Mathf.Max(0f, component.FacilityCapacity);
			authoring.initialStorage = Mathf.Max(0f, component.InitialStorage);
			authoring.defaultLoadingRate = Mathf.Max(0f, component.DefaultLoadingRate);
			authoring.showStorageTooltip = component.ShowStorageTooltip;
			authoring.displayTitle = Clean(component.DisplayTitle);
			authoring.maxPickDistance = Mathf.Max(0f, component.MaxPickDistance);
			authoring.interactionRadius = Mathf.Max(0f, component.InteractionRadius);
			authoring.useBoxInteractionCollider = component.UseBoxInteractionCollider;
			authoring.interactionBoxCenter = component.InteractionBoxCenter;
			authoring.interactionBoxSize = component.InteractionBoxSize;
			authoring.storageAnimationMapKey = Clean(component.StorageAnimationMapKey);
			authoring.storageAnimationLoadId = Clean(component.StorageAnimationLoadId);
			authoring.storageAnimationCapacity = Mathf.Max(0f, component.StorageAnimationCapacity);
			authoring.storageAnimationInvert = component.StorageAnimationInvert;
			authoring.storageAnimationUseTransformFallback = component.StorageAnimationUseTransformFallback;
			authoring.storageAnimationFallbackTransformName = Clean(component.StorageAnimationFallbackTransformName);
			authoring.storageAnimationEmptyLocalY = component.StorageAnimationEmptyLocalY;
			authoring.storageAnimationFullLocalY = component.StorageAnimationFullLocalY;
			authoring.storageAnimationEmptyLocalScaleZ = component.StorageAnimationEmptyLocalScaleZ;
			authoring.storageAnimationFullLocalScaleZ = component.StorageAnimationFullLocalScaleZ;
			authoring.debugLogging = component.DebugLogging;
			ServiceFacilityEditorPreview.ConfigureStorage(ctx.GameObject, authoring, component);
		}

		private static void BuildLoadPoint(ComponentBuilderContext ctx, ToolshedServiceLoadPointComponent component)
		{
			string flowOriginId = BuildFlowOriginId(component, ctx.GameObject.name);
			GameObject authoringHost = ResolveLoadPointAuthoringHost(ctx, flowOriginId, component.DebugLogging);
			if (authoringHost == null)
			{
				return;
			}
			if (authoringHost != ctx.GameObject)
			{
				ServiceFacilityLoadPointAuthoring stale = ctx.GameObject.GetComponent<ServiceFacilityLoadPointAuthoring>();
				if (stale != null)
				{
					DestroyComponent(stale);
				}
				ServiceFacilityFlowOrigin staleOrigin = ctx.GameObject.GetComponent<ServiceFacilityFlowOrigin>();
				if (staleOrigin != null && string.Equals(staleOrigin.originId ?? "", flowOriginId, StringComparison.OrdinalIgnoreCase))
				{
					DestroyComponent(staleOrigin);
				}
			}

			ServiceFacilityLoadPointAuthoring authoring = authoringHost.GetComponent<ServiceFacilityLoadPointAuthoring>() ??
				authoringHost.AddComponent<ServiceFacilityLoadPointAuthoring>();
			authoring.facilityId = Clean(component.FacilityId);
			authoring.storageId = Clean(component.StorageId);
			authoring.loadPointId = Clean(component.LoadPointId);
			authoring.serviceLoadId = Clean(component.ServiceLoadId);
			authoring.loadingRate = Mathf.Max(0f, component.LoadingRate);
			authoring.serviceRadius = Mathf.Max(0f, component.ServiceRadius);
			authoring.maximumSpeedMph = Mathf.Max(0f, component.MaximumSpeedMph);
			authoring.requirePlayerOwnedCars = component.RequirePlayerOwnedCars;
			authoring.enableExtendedTenderSearch = component.EnableExtendedTenderSearch;
			authoring.extendedSearchRadius = Mathf.Max(0f, component.ExtendedSearchRadius);
			authoring.extendedLoadTargetRadius = Mathf.Max(0f, component.ExtendedLoadTargetRadius);
			authoring.useServiceTargetBox = component.UseServiceTargetBox;
			authoring.serviceTargetBoxCenter = component.ServiceTargetBoxCenter;
			authoring.serviceTargetBoxSize = component.ServiceTargetBoxSize;
			authoring.restrictLoadingToServiceTrackSpan = component.RestrictLoadingToServiceTrackSpan;
			authoring.serviceTrackRouteLimit = Mathf.Max(0f, component.ServiceTrackRouteLimit);
			authoring.displayTitle = Clean(component.DisplayTitle);
			authoring.messageWhenActive = string.IsNullOrWhiteSpace(component.MessageWhenActive) ? "Raise" : component.MessageWhenActive.Trim();
			authoring.messageWhenInactive = string.IsNullOrWhiteSpace(component.MessageWhenInactive) ? "Lower" : component.MessageWhenInactive.Trim();
			authoring.maxPickDistance = Mathf.Max(0f, component.MaxPickDistance);
			authoring.interactionRadius = Mathf.Max(0f, component.InteractionRadius);
			authoring.useBoxInteractionCollider = component.UseBoxInteractionCollider;
			authoring.interactionBoxCenter = component.InteractionBoxCenter;
			authoring.interactionBoxSize = component.InteractionBoxSize;
			authoring.loaderLocalPosition = component.LoaderLocalPosition;
			authoring.loaderLocalRotation = component.LoaderLocalRotation;
			authoring.animationMapKey = Clean(component.AnimationMapKey);
			authoring.animationSpeed = Mathf.Max(0f, component.AnimationSpeed);
			authoring.animationInvert = component.AnimationInvert;
			authoring.requireLoweredBeforeLoading = component.RequireLoweredBeforeLoading;
			authoring.createParticleSystem = component.CreateParticleSystem;
			authoring.createVisibleStream = component.CreateVisibleStream;
			authoring.existingEffectObjectName = Clean(component.ExistingEffectObjectName);
			authoring.effectBoolKey = string.IsNullOrWhiteSpace(component.EffectBoolKey) ? "animateLoad" : component.EffectBoolKey.Trim();
			authoring.effectEmissionRate = Mathf.Max(0f, component.EffectEmissionRate);
			authoring.effectStartLifetime = Mathf.Max(0f, component.EffectStartLifetime);
			authoring.effectStartSpeed = Mathf.Max(0f, component.EffectStartSpeed);
			authoring.flowAnimationSpeed = Mathf.Max(0f, component.FlowAnimationSpeed);
			authoring.effectStartSize = Mathf.Max(0f, component.EffectStartSize);
			authoring.effectGravityModifier = component.EffectGravityModifier;
			authoring.effectColorRgb = component.EffectColorRgb;
			authoring.effectAlpha = Mathf.Clamp01(component.EffectAlpha);
			authoring.effectLocalEuler = component.EffectLocalEuler;
			authoring.streamLength = Mathf.Max(0f, component.StreamLength);
			authoring.streamWidth = Mathf.Max(0f, component.StreamWidth);
			authoring.streamUsesWorldDown = component.StreamUsesWorldDown;
			authoring.streamLocalStart = component.StreamLocalStart;
			authoring.streamLocalEnd = component.StreamLocalEnd;
			authoring.debugOriginMarker = component.DebugOriginMarker;
			authoring.debugLogging = component.DebugLogging;
			authoring.flowOriginId = flowOriginId;
			AttachToNamedOutletIfPresent(authoringHost.transform, authoring.flowOriginId, component.DebugLogging);

			ServiceFacilityFlowOrigin origin = authoringHost.GetComponent<ServiceFacilityFlowOrigin>() ??
				authoringHost.AddComponent<ServiceFacilityFlowOrigin>();
			origin.originId = authoring.flowOriginId;
			origin.debugMarker = component.DebugOriginMarker;
			ServiceFacilityEditorPreview.ConfigureLoadPoint(ctx, authoring, component);
		}

		private static GameObject ResolveLoadPointAuthoringHost(ComponentBuilderContext ctx, string outletName, bool debugLogging)
		{
			if (ctx.GameObject == null || string.IsNullOrWhiteSpace(outletName))
			{
				return ctx.GameObject;
			}
			if (NamesEqual(ctx.GameObject.name, outletName))
			{
				return EnsureNormalizedAuthoringProxy(ctx.GameObject.transform, ctx.GameObject.name, outletName, debugLogging);
			}

			Transform outlet = FindChildByName(ctx.GameObject.transform, outletName, null);
			if (outlet == null && ctx.AnimatorGameObject != null)
			{
				outlet = FindChildByName(ctx.AnimatorGameObject.transform, outletName, null);
			}
			if (outlet == null && ctx.GameObject.transform.parent != null)
			{
				outlet = FindChildByName(ctx.GameObject.transform.parent, outletName, ctx.GameObject.transform);
			}
			if (outlet == null)
			{
				if (debugLogging)
				{
					Main.Warn("[ServiceFacility][Loader] outlet empty '" + outletName +
						"' was not found while building " + ctx.GameObject.name +
						"; using component transform.");
				}
				return ctx.GameObject;
			}

			if (debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] authored load point host " + ctx.GameObject.name +
					" -> outlet " + outletName + " at " + BuildPath(outlet));
			}
			return EnsureNormalizedAuthoringProxy(outlet, ctx.GameObject.name, outletName, debugLogging);
		}

		private static GameObject EnsureNormalizedAuthoringProxy(Transform outlet, string componentName, string outletName, bool debugLogging)
		{
			if (outlet == null)
			{
				return null;
			}

			string proxyName = BuildAuthoringProxyName(componentName, outletName);
			Transform proxy = FindDirectChildByName(outlet, proxyName);
			if (proxy == null)
			{
				proxy = FindExistingAuthoringProxy(outlet, outletName);
			}
			if (proxy == null)
			{
				GameObject proxyObject = new GameObject(proxyName);
				proxy = proxyObject.transform;
				proxy.SetParent(outlet, false);
			}

			proxy.localPosition = Vector3.zero;
			proxy.localRotation = Quaternion.identity;
			proxy.localScale = InverseLossyScale(outlet);
			RemoveStaleDirectLoadPointComponents(outlet.gameObject, outletName);

			if (debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] normalized authored load point proxy " +
					proxy.name + " under outlet " + outletName +
					", outletLossyScale=" + outlet.lossyScale +
					", proxyLocalScale=" + proxy.localScale +
					", proxyLossyScale=" + proxy.lossyScale);
			}
			return proxy.gameObject;
		}

		private static string BuildAuthoringProxyName(string componentName, string outletName)
		{
			string baseName = !string.IsNullOrWhiteSpace(componentName) &&
				!string.Equals(componentName, outletName, StringComparison.OrdinalIgnoreCase)
				? componentName.Trim()
				: "ToolshedServiceLoadPoint";
			return baseName.EndsWith(" Authoring", StringComparison.OrdinalIgnoreCase)
				? baseName
				: baseName + " Authoring";
		}

		private static Transform FindExistingAuthoringProxy(Transform outlet, string outletName)
		{
			if (outlet == null)
			{
				return null;
			}
			for (int i = 0; i < outlet.childCount; i++)
			{
				Transform child = outlet.GetChild(i);
				if (child == null)
				{
					continue;
				}
				ServiceFacilityFlowOrigin origin = child.GetComponent<ServiceFacilityFlowOrigin>();
				if (origin != null && NamesEqual(origin.originId, outletName))
				{
					return child;
				}
				ServiceFacilityLoadPointAuthoring loadPoint = child.GetComponent<ServiceFacilityLoadPointAuthoring>();
				if (loadPoint != null && NamesEqual(loadPoint.flowOriginId, outletName))
				{
					return child;
				}
			}
			return null;
		}

		private static Transform FindDirectChildByName(Transform parent, string name)
		{
			if (parent == null || string.IsNullOrWhiteSpace(name))
			{
				return null;
			}
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform child = parent.GetChild(i);
				if (child != null && NamesEqual(child.name, name))
				{
					return child;
				}
			}
			return null;
		}

		private static void RemoveStaleDirectLoadPointComponents(GameObject outletObject, string outletName)
		{
			if (outletObject == null)
			{
				return;
			}

			bool hadDirectAuthoring = false;
			ServiceFacilityLoadPointAuthoring directAuthoring = outletObject.GetComponent<ServiceFacilityLoadPointAuthoring>();
			if (directAuthoring != null)
			{
				hadDirectAuthoring = true;
				DestroyComponent(directAuthoring);
			}

			ServiceFacilityFlowOrigin directOrigin = outletObject.GetComponent<ServiceFacilityFlowOrigin>();
			if (directOrigin != null && NamesEqual(directOrigin.originId, outletName))
			{
				hadDirectAuthoring = true;
				DestroyComponent(directOrigin);
			}

			ServiceFacilityEditorPreview preview = outletObject.GetComponent<ServiceFacilityEditorPreview>();
			if (preview != null)
			{
				DestroyComponent(preview);
			}

			ServiceFacilityPickable pickable = outletObject.GetComponent<ServiceFacilityPickable>();
			if (pickable != null)
			{
				hadDirectAuthoring = true;
				DestroyComponent(pickable);
			}

			if (!hadDirectAuthoring)
			{
				return;
			}

			BoxCollider box = outletObject.GetComponent<BoxCollider>();
			if (box != null && box.isTrigger)
			{
				DestroyComponent(box);
			}
			SphereCollider sphere = outletObject.GetComponent<SphereCollider>();
			if (sphere != null && sphere.isTrigger)
			{
				DestroyComponent(sphere);
			}
		}

		private static void AttachToNamedOutletIfPresent(Transform componentTransform, string outletName, bool debugLogging)
		{
			if (componentTransform == null || string.IsNullOrWhiteSpace(outletName))
			{
				return;
			}

			Transform searchRoot = componentTransform.parent;
			if (searchRoot == null)
			{
				return;
			}

			Transform outlet = FindChildByName(searchRoot, outletName, componentTransform);
			if (outlet == null || outlet == componentTransform)
			{
				return;
			}
			if (IsAncestorOf(componentTransform, outlet))
			{
				return;
			}

			if (componentTransform.parent != outlet)
			{
				componentTransform.SetParent(outlet, false);
			}
			componentTransform.localPosition = Vector3.zero;
			componentTransform.localRotation = Quaternion.identity;
			componentTransform.localScale = InverseLossyScale(outlet);
			if (debugLogging)
			{
				Main.Log("[ServiceFacility][Loader] normalized authored load point " +
					componentTransform.name + " onto outlet " + outletName +
					", outletLossyScale=" + outlet.lossyScale +
					", componentLocalScale=" + componentTransform.localScale);
			}
		}

		private static bool IsAncestorOf(Transform possibleAncestor, Transform child)
		{
			if (possibleAncestor == null || child == null)
			{
				return false;
			}
			Transform current = child.parent;
			while (current != null)
			{
				if (current == possibleAncestor)
				{
					return true;
				}
				current = current.parent;
			}
			return false;
		}

		private static Vector3 InverseLossyScale(Transform parent)
		{
			if (parent == null)
			{
				return Vector3.one;
			}
			Vector3 scale = parent.lossyScale;
			return new Vector3(SafeInverse(scale.x), SafeInverse(scale.y), SafeInverse(scale.z));
		}

		private static float SafeInverse(float value)
		{
			return Mathf.Abs(value) <= 0.0001f ? 1f : 1f / value;
		}

		private static Transform FindChildByName(Transform root, string name, Transform excluded)
		{
			if (root == null || string.IsNullOrWhiteSpace(name))
			{
				return null;
			}
			if (root != excluded && NamesEqual(root.name, name))
			{
				return root;
			}
			for (int i = 0; i < root.childCount; i++)
			{
				Transform match = FindChildByName(root.GetChild(i), name, excluded);
				if (match != null)
				{
					return match;
				}
			}
			return null;
		}

		private static string BuildPath(Transform transform)
		{
			if (transform == null)
			{
				return "<null>";
			}
			List<string> parts = new List<string>();
			Transform current = transform;
			while (current != null)
			{
				parts.Add(current.name);
				current = current.parent;
			}
			parts.Reverse();
			return string.Join("/", parts.ToArray());
		}

		private static void DestroyComponent(UnityEngine.Object component)
		{
			if (component == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(component);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(component);
			}
		}

		private static string BuildFlowOriginId(ToolshedServiceLoadPointComponent component, string objectName)
		{
			if (!string.IsNullOrWhiteSpace(component.LoadPointId))
			{
				return component.LoadPointId.Trim();
			}
			return !string.IsNullOrWhiteSpace(objectName) ? objectName : Guid.NewGuid().ToString("N");
		}

		private static string Clean(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
		}

		private static bool NamesEqual(string left, string right)
		{
			return string.Equals(Clean(left), Clean(right), StringComparison.OrdinalIgnoreCase);
		}
	}

	[HarmonyPatch(typeof(JsonSubtypes), "GetSubTypeMapping")]
	internal static class ToolshedServiceComponentJsonSubtypeMappingPatch
	{
		private static void Postfix(Type type, NullableDictionary<object, Type> __result)
		{
			if (type != typeof(Model.Definition.Component) || __result == null)
			{
				return;
			}
			AddIfMissing(__result, "ToolshedServiceStorage", typeof(ToolshedServiceStorageComponent));
			AddIfMissing(__result, "ToolshedServiceLoadPoint", typeof(ToolshedServiceLoadPointComponent));
		}

		private static void AddIfMissing(NullableDictionary<object, Type> mapping, string key, Type type)
		{
			Type ignored;
			if (!mapping.TryGetValue(key, out ignored))
			{
				mapping.Add(key, type);
			}
		}
	}

	[HarmonyPatch(typeof(JsonSubtypes))]
	internal static class ToolshedServiceComponentJsonSubtypeAttributesPatch
	{
		private static MethodInfo TargetMethod()
		{
			return AccessTools.GetDeclaredMethods(typeof(JsonSubtypes))
				.Single(method => method.Name == "GetAttributes" &&
					method.IsStatic &&
					!method.IsGenericMethod &&
					!method.IsGenericMethodDefinition &&
					method.ReturnType == typeof(IEnumerable<object>));
		}

		private static void Postfix(Type typeInfo, ref IEnumerable<object> __result)
		{
			if (typeInfo != typeof(Model.Definition.Component))
			{
				return;
			}
			__result = __result.Concat(new object[]
			{
				new JsonSubtypes.KnownSubTypeAttribute(typeof(ToolshedServiceStorageComponent), "ToolshedServiceStorage"),
				new JsonSubtypes.KnownSubTypeAttribute(typeof(ToolshedServiceLoadPointComponent), "ToolshedServiceLoadPoint")
			});
		}
	}

	[HarmonyPatch(typeof(CarEditorWindow), "ConfigureAddComponentDropdown")]
	internal static class ToolshedServiceComponentEditorDropdownPatch
	{
		private static readonly FieldInfo AddComponentOptionsField = AccessTools.Field(typeof(CarEditorWindow), "_addComponentOptions");
		private static readonly FieldInfo AddComponentDropdownField = AccessTools.Field(typeof(CarEditorWindow), "addComponentDropdown");
		private static readonly FieldInfo ItemField = AccessTools.Field(typeof(CarEditorWindow), "_item");

		private static void Postfix(CarEditorWindow __instance)
		{
			if (__instance == null || AddComponentOptionsField == null || AddComponentDropdownField == null || ItemField == null)
			{
				return;
			}

			ContainerItem item = ItemField.GetValue(__instance) as ContainerItem;
			if (item == null || !(item.Definition is SceneryDefinition))
			{
				return;
			}

			List<Type> options = AddComponentOptionsField.GetValue(__instance) as List<Type>;
			if (options == null)
			{
				return;
			}

			List<string> labels = new List<string>();
			AddOption(options, labels, typeof(ToolshedServiceStorageComponent), "Toolshed Service Storage");
			AddOption(options, labels, typeof(ToolshedServiceLoadPointComponent), "Toolshed Service Load Point");
			if (labels.Count == 0)
			{
				return;
			}

			object dropdown = AddComponentDropdownField.GetValue(__instance);
			MethodInfo addOptions = dropdown != null ? dropdown.GetType().GetMethod("AddOptions", new[] { typeof(List<string>) }) : null;
			if (addOptions != null)
			{
				addOptions.Invoke(dropdown, new object[] { labels });
			}
		}

		private static void AddOption(List<Type> options, List<string> labels, Type type, string label)
		{
			if (options.Contains(type))
			{
				return;
			}
			options.Add(type);
			labels.Add(label);
		}
	}
}
