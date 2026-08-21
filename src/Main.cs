using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;
using HarmonyLib;
using Toolshed.OilWoodFiring;
#if !RAILLOADER
// The RailLoader edition ships selective interchanges as the standalone
// "Toolshed Selective Interchanges" mod; bundling it here too would double-apply
// every route config onto the same industries.
using Toolshed.SelectiveInterchanges;
#endif
using Toolshed.ServiceFacilities;

namespace Toolshed
{
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static bool Enabled { get; private set; }
        public static Settings Settings { get; private set; }
        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Enabled = true;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;
            modEntry.OnUnload = OnUnload;
            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll();
            OilWoodFiringRuntime.Initialize();
#if !RAILLOADER
            SelectiveInterchangeRuntime.Initialize();
#endif
            ServiceFacilityRuntime.Initialize();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
#if RAILLOADER
            Log("Loaded RailLoader edition (selective interchanges ship separately).");
#else
            Log("Loaded. Toolshed components are available to FUSE packages.");
#endif
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            OilWoodFiringRuntime.OnToggle(value);
            Log(value ? "Enabled." : "Disabled.");
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float deltaTime)
        {
            OilWoodFiringRuntime.Update();
#if !RAILLOADER
            SelectiveInterchangeRuntime.Update();
#endif
            ServiceFacilityRuntime.Update();
        }

        private static void OnActiveSceneChanged(Scene previous, Scene current)
        {
            OilWoodFiringRuntime.OnSceneChanged();
#if !RAILLOADER
            SelectiveInterchangeRuntime.OnSceneChanged();
#endif
            ServiceFacilityRuntime.OnSceneChanged();
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Link And Pin Couplers");
            DrawSeparationSlider(
                "Slack",
                ref Settings.LinkAndPinSlack,
                0.001f,
                0.08f);
        }

        private static void DrawSeparationSlider(string label, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.00} m", GUILayout.Width(190f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(260f));
            if (GUILayout.Button("-", GUILayout.Width(28f)))
            {
                value -= 0.01f;
            }
            if (GUILayout.Button("+", GUILayout.Width(28f)))
            {
                value += 0.01f;
            }

            value = Mathf.Clamp(value, min, max);
            GUILayout.EndHorizontal();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            OilWoodFiringRuntime.Unload();
#if !RAILLOADER
            SelectiveInterchangeRuntime.Unload();
#endif
            ServiceFacilityRuntime.Unload();
            if (_harmony != null)
            {
                _harmony.UnpatchAll(modEntry.Info.Id);
                _harmony = null;
            }
            Log("Unloaded.");
            return true;
        }

        public static void Log(string message)
        {
            ModEntry?.Logger.Log(message);
        }

        public static void Warn(string message)
        {
            ModEntry?.Logger.Warning(message);
        }

        public static void Error(string message)
        {
            ModEntry?.Logger.Error(message);
        }
    }
}
