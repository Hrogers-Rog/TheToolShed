using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Definition.Components;

namespace Toolshed.Couplers
{
    internal static class LinkAndPinCustomization
    {
        public const string AssetPackIdentifier = "LinkAndPinCoupler";
        public const string AssetIdentifier = "link-pin-coupler";

        public static bool HasEnd(Car car, Car.End end)
        {
            if (car?.Definition?.Components == null)
            {
                return false;
            }

            foreach (Component component in car.Definition.Components)
            {
                if (component is DetailModelComponent detail
                    && IsLinkAndPin(detail)
                    && IsForEnd(detail, end))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ShowLooseLink(Car car, Car.End end)
        {
            return GetBool(car, LinkKey(end), true);
        }

        public static void SetShowLooseLink(Car car, Car.End end, bool value)
        {
            SetBool(car, LinkKey(end), end, value);
        }

        public static bool ShowPin(Car car, Car.End end)
        {
            return GetBool(car, PinKey(end), false);
        }

        public static void SetShowPin(Car car, Car.End end, bool value)
        {
            SetBool(car, PinKey(end), end, value);
        }

        public static bool ShowPocket(Car car, Car.End end)
        {
            return GetBool(car, PocketKey(end), false);
        }

        public static void SetShowPocket(Car car, Car.End end, bool value)
        {
            SetBool(car, PocketKey(end), end, value);
        }

        public static bool IsLinkAndPin(DetailModelComponent detail)
        {
            return detail?.Model != null
                && detail.Model.AssetPackIdentifier == AssetPackIdentifier
                && detail.Model.AssetIdentifier == AssetIdentifier;
        }

        public static bool IsForEnd(DetailModelComponent detail, Car.End end)
        {
            if (string.IsNullOrEmpty(detail.Name))
            {
                return true;
            }

            string name = detail.Name.Trim();
            if (name.EndsWith(" F") || name.EndsWith(" Front"))
            {
                return end == Car.End.F;
            }

            if (name.EndsWith(" R") || name.EndsWith(" Rear"))
            {
                return end == Car.End.R;
            }

            return true;
        }

        private static string LinkKey(Car.End end)
        {
            return end == Car.End.F ? "toolshed.linkpin.a.link" : "toolshed.linkpin.b.link";
        }

        private static string PinKey(Car.End end)
        {
            return end == Car.End.F ? "toolshed.linkpin.a.pin" : "toolshed.linkpin.b.pin";
        }

        private static string PocketKey(Car.End end)
        {
            return end == Car.End.F ? "toolshed.linkpin.a.pocket" : "toolshed.linkpin.b.pocket";
        }

        private static bool GetBool(Car car, string key, bool defaultValue)
        {
            if (car?.KeyValueObject == null)
            {
                return defaultValue;
            }

            return car.KeyValueObject[key].BoolValueOrDefault(defaultValue);
        }

        private static void SetBool(
            Car car,
            string key,
            Car.End end,
            bool value)
        {
            if (car?.KeyValueObject != null)
            {
                car.KeyValueObject[key] = Value.Bool(value);
                LinkAndPinEndRegistry.Refresh(car, end);
            }
        }
    }
}
