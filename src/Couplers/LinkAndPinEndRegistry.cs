using System.Collections.Generic;
using System.Reflection;
using Model;

namespace Toolshed.Couplers
{
    internal static class LinkAndPinEndRegistry
    {
        private static readonly FieldInfo EndGearFField = typeof(Car).GetField("EndGearF", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo EndGearRField = typeof(Car).GetField("EndGearR", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly Dictionary<Car.EndGear, Entry> Entries = new Dictionary<Car.EndGear, Entry>();

        public static void Register(Car car, Car.End end, LinkAndPinVisualController controller)
        {
            Car.EndGear gear = EndGearFor(car, end);
            if (gear != null)
            {
                Entries[gear] = new Entry(car, end, controller);
            }
        }

        public static bool TryGet(Car.EndGear gear, out Entry entry)
        {
            if (gear != null && Entries.TryGetValue(gear, out entry))
            {
                return true;
            }

            entry = null;
            return false;
        }

        public static Car.EndGear EndGearFor(Car car, Car.End end)
        {
            if (car == null)
            {
                return null;
            }

            FieldInfo field = end == Car.End.F ? EndGearFField : EndGearRField;
            return field?.GetValue(car) as Car.EndGear;
        }

        internal sealed class Entry
        {
            public readonly Car Car;
            public readonly Car.End End;
            public readonly LinkAndPinVisualController Controller;

            public Entry(Car car, Car.End end, LinkAndPinVisualController controller)
            {
                Car = car;
                End = end;
                Controller = controller;
            }
        }
    }
}
