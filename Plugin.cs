using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;

namespace ModUtilities
{
    [BepInPlugin("net.natorius.sailwind.modutilities", "ModUtilities", "0.0.1")]
    class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource logger; 

        private void Awake()
        {
            logger = this.Logger;
            ModUtilities.Init();

            // Inject patches into SaveLoadManager
            var harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Register save/load procedures for GUIDs
            ModUtilities.Persistence.RegisterProcedures(
                "ModUtilities.GUIDManager",
                ModUtilities.GUIDManager.SaveGUIDs,
                ModUtilities.GUIDManager.LoadGUIDs
            );
        }
    }
}
