using HarmonyLib;
using System;

namespace ModUtilities
{
    [HarmonyPatch(typeof(SaveLoadManager))]
    static class SaveLoadManagerPatch
    {
        [HarmonyPatch("SaveModData"), HarmonyPostfix]
        static void SaveModDataPostfix()
        {
            try { ModUtilities.Persistence.SaveModData(); }
            catch (Exception e)
            {
                Plugin.logger.LogError("Exception encountered saving mod data!");
                Plugin.logger.LogError(e);
            }
        }

        [HarmonyPatch("LoadModData"), HarmonyPostfix]
        static void LoadModDataPostfix()
        {
            try { ModUtilities.Persistence.LoadModData(); }
            catch (Exception e)
            {
                Plugin.logger.LogError("Exception encountered loading mod data!");
                Plugin.logger.LogError(e);
            }
        }
    }
}
