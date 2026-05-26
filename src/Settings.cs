using UnityModManagerNet;

namespace Toolshed
{
    public sealed class Settings : UnityModManager.ModSettings
    {
        public float LinkAndPinEquipmentSeparation = 0.84f;
        public float LinkAndPinCabooseSeparation = 0.84f;
        public float LinkAndPinLocomotiveOrTenderSeparation = 0.93f;
        public float LinkAndPinSlack = 0.02f;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
