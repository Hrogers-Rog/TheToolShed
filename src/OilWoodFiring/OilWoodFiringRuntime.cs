using Model;

namespace Toolshed.OilWoodFiring
{
    internal static class OilWoodFiringRuntime
    {
        public static void Initialize()
        {
            RegisterCustomLoads();
        }

        public static void OnToggle(bool enabled)
        {
            if (enabled)
            {
                RegisterCustomLoads();
                return;
            }

            OilLoaderStandService.Restore();
        }

        public static void Update()
        {
            if (!Main.Enabled)
            {
                return;
            }

            RegisterCustomLoads();
            OilLoaderStandService.Update();
        }

        public static void Unload()
        {
            OilLoaderStandService.Restore();
        }

        private static void RegisterCustomLoads()
        {
            CarPrototypeLibrary library = CarPrototypeLibrary.instance;
            if (library != null)
            {
                OilFuelRegistry.EnsureRegistered(library);
            }
        }
    }
}
