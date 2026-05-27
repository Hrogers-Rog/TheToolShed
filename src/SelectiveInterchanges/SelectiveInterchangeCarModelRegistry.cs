using System;
using System.Collections.Generic;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;

namespace Toolshed.SelectiveInterchanges
{
	internal static class SelectiveInterchangeCarModelRegistry
	{
		private sealed class Selection
		{
			public readonly List<Entry> Entries = new List<Entry>();
		}

		private sealed class Entry
		{
			public string Identifier;
			public int Weight;
		}

		private sealed class Candidate
		{
			public TypedContainerItem<CarDefinition> DefinitionInfo;
			public int CumulativeWeight;
		}

		private static readonly object SyncRoot = new object();
		private static readonly Dictionary<string, Selection> Selections = new Dictionary<string, Selection>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> WarnedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public static void Clear()
		{
			lock (SyncRoot)
			{
				Selections.Clear();
				WarnedMissing.Clear();
			}
		}

		public static void Register(string orderTag, string[] identifiers, SelectiveInterchangeWeightedCarModel[] weightedModels)
		{
			if (string.IsNullOrWhiteSpace(orderTag))
			{
				return;
			}

			Selection selection = BuildSelection(identifiers, weightedModels);
			lock (SyncRoot)
			{
				if (selection.Entries.Count == 0)
				{
					Selections.Remove(orderTag);
					return;
				}
				Selections[orderTag] = selection;
			}
		}

		public static bool TrySelect(string orderTag, IPrefabStore prefabStore, CarTypeFilter carTypeFilter, Load load, System.Random rnd, out TypedContainerItem<CarDefinition> definitionInfo)
		{
			definitionInfo = null;
			if (string.IsNullOrWhiteSpace(orderTag) || prefabStore == null)
			{
				return false;
			}

			Selection selection;
			lock (SyncRoot)
			{
				if (!Selections.TryGetValue(orderTag, out selection))
				{
					return false;
				}
			}

			List<Candidate> candidates = BuildCandidates(selection, prefabStore, carTypeFilter, load);
			if (candidates.Count == 0)
			{
				WarnNoMatch(orderTag, carTypeFilter, load);
				return false;
			}

			int totalWeight = candidates[candidates.Count - 1].CumulativeWeight;
			int roll = rnd != null ? rnd.Next(1, totalWeight + 1) : new System.Random().Next(1, totalWeight + 1);
			for (int i = 0; i < candidates.Count; i++)
			{
				if (roll <= candidates[i].CumulativeWeight)
				{
					definitionInfo = candidates[i].DefinitionInfo;
					return true;
				}
			}

			definitionInfo = candidates[candidates.Count - 1].DefinitionInfo;
			return true;
		}

		private static Selection BuildSelection(string[] identifiers, SelectiveInterchangeWeightedCarModel[] weightedModels)
		{
			Selection selection = new Selection();
			if (identifiers != null)
			{
				for (int i = 0; i < identifiers.Length; i++)
				{
					AddEntry(selection, identifiers[i], 1);
				}
			}
			if (weightedModels != null)
			{
				for (int i = 0; i < weightedModels.Length; i++)
				{
					SelectiveInterchangeWeightedCarModel weighted = weightedModels[i];
					if (weighted == null)
					{
						continue;
					}
					string identifier = FirstNonEmpty(
						weighted.carModelIdentifier,
						weighted.modelIdentifier,
						weighted.carPrototypeIdentifier,
						weighted.carPrototypeId,
						weighted.carDefinitionIdentifier);
					AddEntry(selection, identifier, weighted.weight);
				}
			}
			return selection;
		}

		private static void AddEntry(Selection selection, string identifier, int weight)
		{
			if (selection == null || string.IsNullOrWhiteSpace(identifier) || weight <= 0)
			{
				return;
			}
			selection.Entries.Add(new Entry
			{
				Identifier = identifier.Trim(),
				Weight = weight
			});
		}

		private static string FirstNonEmpty(params string[] values)
		{
			for (int i = 0; i < values.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(values[i]))
				{
					return values[i];
				}
			}
			return null;
		}

		private static List<Candidate> BuildCandidates(Selection selection, IPrefabStore prefabStore, CarTypeFilter carTypeFilter, Load load)
		{
			List<Candidate> candidates = new List<Candidate>();
			HashSet<string> seenForEntry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int totalWeight = 0;
			for (int i = 0; i < selection.Entries.Count; i++)
			{
				Entry entry = selection.Entries[i];
				seenForEntry.Clear();
				foreach (TypedContainerItem<CarDefinition> definitionInfo in ResolveDefinitions(prefabStore, entry.Identifier))
				{
					if (!IsValidForOrder(definitionInfo, carTypeFilter, load) || !seenForEntry.Add(definitionInfo.Identifier))
					{
						continue;
					}
					totalWeight += entry.Weight;
					candidates.Add(new Candidate
					{
						DefinitionInfo = definitionInfo,
						CumulativeWeight = totalWeight
					});
				}
			}
			return candidates;
		}

		private static IEnumerable<TypedContainerItem<CarDefinition>> ResolveDefinitions(IPrefabStore prefabStore, string identifier)
		{
			TypedContainerItem<CarDefinition> exact = null;
			try
			{
				exact = prefabStore.CarDefinitionInfoForIdentifier(identifier);
			}
			catch
			{
				exact = null;
			}

			if (exact != null)
			{
				yield return exact;
			}

			IEnumerable<TypedContainerItem<CarDefinition>> allDefinitions = null;
			try
			{
				allDefinitions = prefabStore.AllCarDefinitionInfos;
			}
			catch
			{
				allDefinitions = null;
			}

			if (allDefinitions == null)
			{
				yield break;
			}

			foreach (TypedContainerItem<CarDefinition> item in allDefinitions)
			{
				if (item == null || item.Definition == null)
				{
					continue;
				}
				if (exact != null && string.Equals(item.Identifier, exact.Identifier, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (string.Equals(item.Identifier, identifier, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(item.Definition.ModelIdentifier, identifier, StringComparison.OrdinalIgnoreCase))
				{
					yield return item;
				}
			}
		}

		private static bool IsValidForOrder(TypedContainerItem<CarDefinition> definitionInfo, CarTypeFilter carTypeFilter, Load load)
		{
			if (definitionInfo == null || definitionInfo.Definition == null)
			{
				return false;
			}
			if (carTypeFilter != null && !carTypeFilter.Matches(definitionInfo.Definition.CarType))
			{
				return false;
			}
			List<LoadSlot> loadSlots = definitionInfo.Definition.LoadSlots;
			if (loadSlots == null || loadSlots.Count == 0)
			{
				return false;
			}
			return load == null || loadSlots[0].LoadRequirementsMatch(load);
		}

		private static void WarnNoMatch(string orderTag, CarTypeFilter carTypeFilter, Load load)
		{
			string key = orderTag + "|" + (carTypeFilter != null ? carTypeFilter.ToString() : "") + "|" + (load != null ? load.id : "");
			lock (SyncRoot)
			{
				if (!WarnedMissing.Add(key))
				{
					return;
				}
			}
			Main.Warn("[SelectiveInterchange] No configured car model matched order tag '" + orderTag + "' with carTypes='" + carTypeFilter + "' load='" + (load != null ? load.id : "empty") + "'. Falling back to Railroader's normal car selection.");
		}
	}
}
