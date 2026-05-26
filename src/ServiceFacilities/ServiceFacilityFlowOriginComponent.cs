using System;
using System.Collections.Generic;
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
	/// Runtime marker used by service facility flow effects.
	/// Put this on a small editor-placed component or prefab empty at the exact pipe/chute tip.
	/// </summary>
	public sealed class ServiceFacilityFlowOrigin : MonoBehaviour
	{
		public string originId;
		public bool debugMarker;
	}

	/// <summary>
	/// Railroader definition-editor component. It creates an invisible marker transform that
	/// ServiceFacilityParticleEffectDriver can use as a reliable flow origin.
	/// </summary>
	[Component(ComponentDefinitionMask.Scenery, ComponentLifetime.Model)]
	public sealed class ToolshedFlowOriginComponent : Model.Definition.Component
	{
		public override string Kind { get; } = "ToolshedFlowOrigin";

		public string OriginId { get; set; } = "";

		public bool DebugMarker { get; set; }
	}

	[HarmonyPatch(typeof(ComponentFactory), "BuildComponent")]
	internal static class ToolshedFlowOriginComponentBuilderPatch
	{
		private static bool Prefix(Model.Definition.Component component, ComponentBuilderContext ctx)
		{
			ToolshedFlowOriginComponent flowOrigin = component as ToolshedFlowOriginComponent;
			if (flowOrigin == null)
			{
				return true;
			}

			ServiceFacilityFlowOrigin marker = ctx.GameObject.GetComponent<ServiceFacilityFlowOrigin>() ??
				ctx.GameObject.AddComponent<ServiceFacilityFlowOrigin>();
			marker.originId = flowOrigin.OriginId ?? "";
			marker.debugMarker = flowOrigin.DebugMarker;
			ctx.GameObject.name = string.IsNullOrWhiteSpace(component.Name) ? "Toolshed_FlowOrigin" : component.Name;

			if (flowOrigin.DebugMarker)
			{
				AddDebugMarker(ctx.GameObject);
			}
			return false;
		}

		private static void AddDebugMarker(GameObject parent)
		{
			GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			markerObject.name = "Toolshed Flow Origin Debug Marker";
			markerObject.transform.SetParent(parent.transform, false);
			markerObject.transform.localPosition = Vector3.zero;
			markerObject.transform.localRotation = Quaternion.identity;
			markerObject.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
			Collider collider = markerObject.GetComponent<Collider>();
			if (collider != null)
			{
				UnityEngine.Object.Destroy(collider);
			}
			Renderer renderer = markerObject.GetComponent<Renderer>();
			if (renderer != null)
			{
				renderer.material.color = Color.yellow;
			}
		}
	}

	[HarmonyPatch(typeof(JsonSubtypes), "GetSubTypeMapping")]
	internal static class ToolshedFlowOriginJsonSubtypePatch
	{
		private static void Postfix(Type type, NullableDictionary<object, Type> __result)
		{
			if (type != typeof(Model.Definition.Component) || __result == null)
			{
				return;
			}
			Type ignored;
			if (!__result.TryGetValue("ToolshedFlowOrigin", out ignored))
			{
				__result.Add("ToolshedFlowOrigin", typeof(ToolshedFlowOriginComponent));
			}
		}
	}

	[HarmonyPatch(typeof(CarEditorWindow), "ConfigureAddComponentDropdown")]
	internal static class ToolshedFlowOriginEditorDropdownPatch
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
			if (options == null || options.Contains(typeof(ToolshedFlowOriginComponent)))
			{
				return;
			}

			options.Add(typeof(ToolshedFlowOriginComponent));
			object dropdown = AddComponentDropdownField.GetValue(__instance);
			if (dropdown == null)
			{
				return;
			}

			MethodInfo addOptions = dropdown.GetType().GetMethod("AddOptions", new[] { typeof(List<string>) });
			if (addOptions != null)
			{
				addOptions.Invoke(dropdown, new object[] { new List<string> { "Toolshed Flow Origin" } });
			}
		}
	}
}
