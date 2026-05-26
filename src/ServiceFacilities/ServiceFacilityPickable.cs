using System.Text;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Model.Ops;
using Model.Ops.Definition;
using UnityEngine;

namespace Toolshed.ServiceFacilities
{
	/// <summary>
	/// Pickable bridge attached to the visible scenery root.
	/// Vanilla ObjectPicker asks the hit collider for an IPickable in its parents, so the
	/// runtime loader child is not enough when the player's mouse hits the asset mesh.
	/// </summary>
	public sealed class ServiceFacilityPickable : MonoBehaviour, IPickable
	{
		public KeyValueObject keyValueObject;
		public string requestKey = "request";
		public string displayTitle = "Service Loader";
		public string displayMessageTrue = "Click to Stop Loading";
		public string displayMessageFalse = "Click to Start Loading";
		public Industry sourceIndustry;
		public Load load;
		public float capacity;
		public float maxPickDistance = 50f;

		public float MaxPickDistance
		{
			get { return maxPickDistance; }
		}

		public int Priority
		{
			get { return 1; }
		}

		public PickableActivationFilter ActivationFilter
		{
			get { return PickableActivationFilter.PrimaryOnly; }
		}

		public TooltipInfo TooltipInfo
		{
			get { return new TooltipInfo(displayTitle, TooltipText()); }
		}

		public void Activate(PickableActivateEvent evt)
		{
			if (!CanSendToggle())
			{
				return;
			}
			bool targetValue = !CurrentValue();
			StateManager.ApplyLocal(PropertyChangeMessage(targetValue));
		}

		public void Deactivate()
		{
		}

		private string TooltipText()
		{
			StringBuilder builder = new StringBuilder();
			string storage = StorageText();
			if (!string.IsNullOrEmpty(storage))
			{
				builder.Append(storage);
				builder.Append('\n');
			}

			if (!CanSendToggle())
			{
				builder.Append("<sprite name=\"MouseNo\"> N/A");
			}
			else
			{
				builder.Append(CurrentValue() ? displayMessageTrue : displayMessageFalse);
			}
			return builder.ToString();
		}

		private string StorageText()
		{
			if (sourceIndustry == null || load == null)
			{
				return null;
			}

			float quantity = sourceIndustry.Storage.QuantityInStorage(load, null);
			float storageCapacity = capacity;
			if (storageCapacity <= 0f)
			{
				sourceIndustry.TryGetStorageCapacity(load, out storageCapacity);
			}

			if (storageCapacity > 0f)
			{
				return TextSprites.PiePercent(quantity, storageCapacity) + " " + load.QuantityString(quantity);
			}
			return load.QuantityString(quantity);
		}

		private bool CurrentValue()
		{
			return keyValueObject != null && keyValueObject[requestKey].BoolValue;
		}

		private bool CanSendToggle()
		{
			if (keyValueObject == null || string.IsNullOrEmpty(keyValueObject.RegisteredId) || string.IsNullOrEmpty(requestKey))
			{
				return false;
			}
			return StateManager.CheckAuthorizedToSendMessage(PropertyChangeMessage(!CurrentValue()));
		}

		private PropertyChange PropertyChangeMessage(bool targetValue)
		{
			return new PropertyChange(keyValueObject.RegisteredId, requestKey, new BoolPropertyValue(targetValue));
		}
	}
}
