using HarmonyLib;
using Model;
using Model.Definition;
using Model.Definition.Components;

namespace Toolshed.Couplers
{
    [HarmonyPatch(typeof(Car))]
    internal static class LinkAndPinCouplerSlackPatch
    {
        [HarmonyPatch(nameof(Car.CouplerSlack))]
        [HarmonyPostfix]
        private static void CouplerSlackPostfix(Car __instance, Car.End end, ref float __result)
        {
            if (Main.Enabled && HasLinkAndPinEnd(__instance, end))
            {
                __result = Main.Settings.LinkAndPinSlack;
            }
        }

        private static bool HasLinkAndPinEnd(Car car, Car.End end)
        {
            if (car?.Definition?.Components == null)
            {
                return false;
            }

            foreach (Component component in car.Definition.Components)
            {
                if (component is DetailModelComponent detail
                    && LinkAndPinCustomization.IsLinkAndPin(detail)
                    && LinkAndPinCustomization.IsForEnd(detail, end))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
