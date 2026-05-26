using System;
using System.Reflection;
using HarmonyLib;
using Model;
using UI.Builder;
using UI.CarCustomizeWindow;
using UnityEngine;

namespace Toolshed.Couplers
{
    [HarmonyPatch(typeof(CarCustomizeWindow), "BuildColorTab")]
    internal static class LinkAndPinCustomizeWindowPatch
    {
        private static Func<UIPanelBuilder, Func<bool>, Action<bool>, bool, RectTransform> _addToggle;

        [HarmonyPostfix]
        private static void BuildColorTabPostfix(UIPanelBuilder builder, Car ____car)
        {
            if (!Main.Enabled || ____car == null)
            {
                return;
            }

            bool hasAEnd = LinkAndPinCustomization.HasEnd(____car, Car.End.F);
            bool hasBEnd = LinkAndPinCustomization.HasEnd(____car, Car.End.R);
            if (!hasAEnd && !hasBEnd)
            {
                return;
            }

            builder.AddSection("Link And Pin", section =>
            {
                if (hasAEnd)
                {
                    AddEndFields(section, ____car, Car.End.F, hasBEnd ? "A-End " : "");
                }

                if (hasBEnd)
                {
                    AddEndFields(section, ____car, Car.End.R, hasAEnd ? "B-End " : "");
                }
            }, 0f);
        }

        private static void AddEndFields(UIPanelBuilder builder, Car car, Car.End end, string prefix)
        {
            builder.AddField(prefix + "Loose Link", AddToggleCompat(
                builder,
                () => LinkAndPinCustomization.ShowLooseLink(car, end),
                value => LinkAndPinCustomization.SetShowLooseLink(car, end, value),
                true));

            builder.AddField(prefix + "Pin", AddToggleCompat(
                builder,
                () => LinkAndPinCustomization.ShowPin(car, end),
                value => LinkAndPinCustomization.SetShowPin(car, end, value),
                true));

            builder.AddField(prefix + "Pocket", AddToggleCompat(
                builder,
                () => LinkAndPinCustomization.ShowPocket(car, end),
                value => LinkAndPinCustomization.SetShowPocket(car, end, value),
                true));
        }

        private static RectTransform AddToggleCompat(UIPanelBuilder builder, Func<bool> value, Action<bool> action, bool interactable)
        {
            if (_addToggle == null)
            {
                MethodInfo method = typeof(UIPanelBuilder).GetMethod("AddToggle", BindingFlags.Instance | BindingFlags.Public);
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2)
                {
                    _addToggle = (panelBuilder, valueClosure, toggleAction, _) =>
                        (RectTransform)method.Invoke(panelBuilder, new object[] { valueClosure, toggleAction });
                }
                else if (parameters.Length == 3)
                {
                    _addToggle = (panelBuilder, valueClosure, toggleAction, isInteractable) =>
                        (RectTransform)method.Invoke(panelBuilder, new object[] { valueClosure, toggleAction, isInteractable });
                }
                else
                {
                    throw new NotSupportedException("UIPanelBuilder.AddToggle signature is not supported.");
                }
            }

            return _addToggle(builder, value, action, interactable);
        }
    }
}
