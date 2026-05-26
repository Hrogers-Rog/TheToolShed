using HarmonyLib;
using Model;
using Model.Definition.Components;
using RollingStock;

namespace Toolshed.Couplers
{
    [HarmonyPatch(typeof(DetailModelController))]
    internal static class LinkAndPinDetailModelPatch
    {
        [HarmonyPatch(nameof(DetailModelController.Configure))]
        [HarmonyPostfix]
        private static void ConfigurePostfix(DetailModelController __instance, DetailModelComponent component)
        {
            if (!LinkAndPinCustomization.IsLinkAndPin(component))
            {
                return;
            }

            LinkAndPinVisualController controller = __instance.gameObject.GetComponent<LinkAndPinVisualController>();
            if (controller == null)
            {
                controller = __instance.gameObject.AddComponent<LinkAndPinVisualController>();
            }

            Car car = Traverse.Create(__instance).Field("_car").GetValue<Car>();
            controller.Configure(car, EndFromName(component.Name));
        }

        private static Car.End EndFromName(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                string trimmed = name.Trim();
                if (trimmed.EndsWith(" F") || trimmed.EndsWith(" Front"))
                {
                    return Car.End.F;
                }
            }

            return Car.End.R;
        }
    }
}
