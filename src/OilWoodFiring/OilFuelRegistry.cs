using System;
using System.Linq;
using Model;
using Model.Definition.Data;
using Model.Ops.Definition;
using UnityEngine;

namespace Toolshed.OilWoodFiring
{
	internal static class OilFuelRegistry
	{
		private static Load _runtimeBunkerCLoad;

		private static Load _runtimeWoodLoad;

		private static bool _loggedRegistration;

		public static Load GetOrCreateBunkerCLoad()
		{
			if (_runtimeBunkerCLoad != null)
			{
				return _runtimeBunkerCLoad;
			}
			Load load = ScriptableObject.CreateInstance<Load>();
			load.name = OilFuelConstants.BunkerCLoadId;
			load.description = OilFuelConstants.BunkerCDescription;
			load.units = LoadUnits.Gallons;
			load.density = OilFuelConstants.BunkerCDensityLbsPerCubicFoot;
			load.importable = true;
			load.hideFlags = HideFlags.HideAndDontSave;
			_runtimeBunkerCLoad = load;
			return _runtimeBunkerCLoad;
		}

		public static Load GetOrCreateWoodLoad()
		{
			if (_runtimeWoodLoad != null)
			{
				return _runtimeWoodLoad;
			}
			Load load = ScriptableObject.CreateInstance<Load>();
			load.name = OilFuelConstants.WoodLoadId;
			load.description = OilFuelConstants.WoodDescription;
			load.units = LoadUnits.Pounds;
			load.importable = true;
			load.hideFlags = HideFlags.HideAndDontSave;
			_runtimeWoodLoad = load;
			return _runtimeWoodLoad;
		}

		public static Load GetOrCreateLoad(string loadId)
		{
			if (string.Equals(loadId, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase))
			{
				return GetOrCreateBunkerCLoad();
			}
			if (string.Equals(loadId, OilFuelConstants.WoodLoadId, StringComparison.OrdinalIgnoreCase))
			{
				return GetOrCreateWoodLoad();
			}
			CarPrototypeLibrary library = CarPrototypeLibrary.instance;
			return library != null ? library.LoadForId(loadId) : null;
		}

		public static void EnsureRegistered(CarPrototypeLibrary library)
		{
			if (library == null)
			{
				return;
			}
			Load bunkerCLoad = FindBunkerCLoad(library);
			if (bunkerCLoad != null)
			{
				_runtimeBunkerCLoad = bunkerCLoad;
			}
			Load woodLoad = FindWoodLoad(library);
			if (woodLoad != null)
			{
				_runtimeWoodLoad = woodLoad;
			}
			Load[] source = library.opsLoads ?? Array.Empty<Load>();
			Load[] loadsToAdd = new Load[]
			{
				bunkerCLoad ?? GetOrCreateBunkerCLoad(),
				woodLoad ?? GetOrCreateWoodLoad()
			}.Where((Load load) => load != null && !source.Any((Load existing) => existing != null && string.Equals(existing.id, load.id, StringComparison.OrdinalIgnoreCase))).ToArray();
			if (loadsToAdd.Length == 0)
			{
				return;
			}
			library.opsLoads = source.Concat(loadsToAdd).ToArray();
			if (!_loggedRegistration)
			{
				_loggedRegistration = true;
				Main.Log("Registered service loads in CarPrototypeLibrary: " + string.Join(", ", loadsToAdd.Select((Load load) => load.id).ToArray()) + ".");
			}
		}

		public static Load FindBunkerCLoad(CarPrototypeLibrary library)
		{
			if (library == null || library.opsLoads == null)
			{
				return null;
			}
			return library.opsLoads.FirstOrDefault((Load load) => load != null && string.Equals(load.id, OilFuelConstants.BunkerCLoadId, StringComparison.OrdinalIgnoreCase));
		}

		public static Load FindWoodLoad(CarPrototypeLibrary library)
		{
			if (library == null || library.opsLoads == null)
			{
				return null;
			}
			return library.opsLoads.FirstOrDefault((Load load) => load != null && string.Equals(load.id, OilFuelConstants.WoodLoadId, StringComparison.OrdinalIgnoreCase));
		}
	}
}
