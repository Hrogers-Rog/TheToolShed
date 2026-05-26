using System;

namespace Toolshed.OilWoodFiring
{
	internal static class OilFuelConstants
	{
		public const float GallonsPerCubicFoot = 7.4805193f;

		public const string CoalLoadId = "coal";

		public const string WaterLoadId = "water";

		public const string DieselFuelLoadId = "diesel-fuel";

		public const string BunkerCLoadId = "bunker-c";

		public const string BunkerCDescription = "Bunker C";

		public const string WoodLoadId = "pulpwood";

		public const string WoodDescription = "Pulpwood";

		public const float BunkerCDensityLbsPerCubicFoot = 62f;

		public const float BunkerCPoundsPerGallon = BunkerCDensityLbsPerCubicFoot / GallonsPerCubicFoot;

		// Tuned from live testing to land near 12-20 gal/mi on light 4-4-0 oil burners.
		public const float BunkerCGallonsPerCoalPound = 0.043f;

		// First-pass dry cordwood estimate. Wood has less heat per pound than coal,
		// so wood-fired engines drain more pounds for the same steam demand.
		public const float WoodPoundsPerCoalPound = 1.8f;

		public const float BunkerCLoadingRateGallonsPerSecond = 60f;

		public const float LoadPresentThreshold = 0.001f;

		public static bool IsSteamFuelId(string loadId)
		{
			return string.Equals(loadId, CoalLoadId, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(loadId, BunkerCLoadId, StringComparison.OrdinalIgnoreCase) ||
				IsWoodFuelId(loadId);
		}

		public static bool IsCustomSteamFuelId(string loadId)
		{
			return string.Equals(loadId, BunkerCLoadId, StringComparison.OrdinalIgnoreCase) ||
				IsWoodFuelId(loadId);
		}

		public static bool IsWoodFuelId(string loadId)
		{
			return string.Equals(loadId, WoodLoadId, StringComparison.OrdinalIgnoreCase);
		}
	}
}
