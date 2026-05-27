using HarmonyLib;
using Toolshed.SelectiveInterchanges;
using UnityEngine;
using UnityModManagerNet;

namespace Toolshed
{
    /// <summary>
    /// RailLoader-era standalone entry point for the selective interchange component.
    /// Keep this package separate from the full Toolshed mod; both expose the same
    /// Toolshed.SelectiveInterchanges component types for route JSON compatibility.
    /// </summary>
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static bool Enabled { get; private set; }

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Enabled = true;

            modEntry.OnToggle = OnToggle;
            modEntry.OnUpdate = OnUpdate;
            modEntry.OnUnload = OnUnload;

            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll();
            SelectiveInterchangeRuntime.Initialize();

            Log("Loaded RailLoader selective interchange component.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            Log(value ? "Enabled." : "Disabled.");
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float deltaTime)
        {
            if (!Enabled)
            {
                return;
            }
            SelectiveInterchangeRuntime.Update();
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            SelectiveInterchangeRuntime.Unload();
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
