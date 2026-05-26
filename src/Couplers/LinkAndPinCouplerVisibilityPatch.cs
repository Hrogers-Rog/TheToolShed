using HarmonyLib;
using Model;
using Model.Definition;
using Model.Definition.Components;
using RollingStock;

namespace Toolshed.Couplers
{
    [HarmonyPatch(typeof(Coupler))]
    internal static class LinkAndPinCouplerVisibilityPatch
    {
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AwakePostfix(Coupler __instance)
        {
            if (ShouldHide(__instance))
            {
                __instance.SetVisible(false);
            }
        }

        [HarmonyPatch(nameof(Coupler.SetVisible))]
        [HarmonyPrefix]
        private static void SetVisiblePrefix(Coupler __instance, ref bool visible)
        {
            if (visible && ShouldHide(__instance))
            {
                visible = false;
            }
        }

        private static bool ShouldHide(Coupler coupler)
        {
            if (!Main.Enabled || coupler?.car?.Definition?.Components == null)
            {
                return false;
            }

            foreach (Component component in coupler.car.Definition.Components)
            {
                if (component is DetailModelComponent detail
                    && LinkAndPinCustomization.IsLinkAndPin(detail)
                    && LinkAndPinCustomization.IsForEnd(detail, coupler.end))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
