using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model.Ops;

namespace Toolshed.SelectiveInterchanges
{
	[HarmonyPatch(typeof(OpsController), "get_EnabledInterchanges")]
	internal static class SelectiveInterchangeOpsControllerPatch
	{
		[HarmonyPostfix]
		private static void ExcludeSelectiveInterchanges(ref IEnumerable<Interchange> __result)
		{
			if (!Main.Enabled || __result == null)
			{
				return;
			}
			__result = __result.Where(interchange => !SelectiveInterchangeRuntime.ShouldExcludeFromGenericInterchangeSelection(interchange));
		}
	}
}
