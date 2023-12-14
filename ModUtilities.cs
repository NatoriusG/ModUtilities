using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ModUtilities
{
    public static class ModUtilities
    {
        static ManualLogSource logger;

        internal static void Init()
        {
            logger = Plugin.logger;
            logger.LogDebug("Logger assigned; ModUtilities initialized.");
        }

        /// <summary>
        /// Miscellaneous functions relating to game-specific behaviors.
        /// </summary>
        public static class GameUtilities
        {
            /// <summary>
            /// Force the player to drop whatever item is currently being held.
            /// </summary>
            public static void DropHeldItem()
            {
                logger.LogDebug("Dropping held item.");
                var lookPointer = References.GetPlayerLookPointer();
                lookPointer.GetHeldItem().OnDrop();
                lookPointer.DropItem();
            }
        }

        /// <summary>
        /// Attaches a GUID to a GameObject, using a component to hold the ID.
        /// </summary>
        public class GUID : MonoBehaviour
        {
            public uint ID;

            /// <summary>
            /// Override ToString to return the GUID.
            /// </summary>
            /// <returns>the GUID held by this component</returns>
            public override string ToString()
            {
                return ID.ToString();
            }
        }

        /// <summary>
        /// Injects GUIDs into game objects, and handles GUID assignment.
        /// </summary>
        public static class GUIDManager
        {
            /// <summary>
            /// Stores a particular GameObject's GUID, along with other information used for
            /// error-checking.
            /// </summary>
            struct ObjectNode
            {
                public uint GUID;
                public string name;
                public string tag;
                public Vector3 position;
                public Quaternion rotation;
            }

            const uint startID = 0x1000_0000;
            static uint maxAssignedID;

            /// <summary>
            /// Retrieves the next valid GUID that can be assigned to a new object.
            /// The maxAssignedID must be set when the game is loaded to prevent
            /// non-unique IDs.
            /// </summary>
            /// <returns>a new GUID</returns>
            static uint NextGUID()
            {
                logger.LogDebug("New GUID requested.");
                logger.LogDebug($"Current max is {maxAssignedID}; incrementing and returning.");
                logger.LogDebug($"New ID will be {maxAssignedID + 1}");
                return ++maxAssignedID; // Increment maxAssignedID, then return that new value.
                                        // Unfortunately, this does not nicely simplify by
                                        // splitting the assignment and comparison.
            }

            /// <summary>
            /// Find a GameObject using its GUID.
            /// </summary>
            /// <param name="id">the GUID of the object</param>
            /// <returns>the object if it exists; null otherwise</returns>
            public static GameObject FindObjectByID(int id)
            {
                // TODO: test if this properly catches NREs if an object is deleted/destroyed
                // TODO: cache this result; exceptionally easy if IDs are incremental
                return GameObject.FindObjectsOfType<GUID>()
                    .Where(guid => guid.ID == id)?.First()?.gameObject;
            }

            /// <summary>
            /// Create a JSON-encoded string associating an ObjectNode with each object
            /// registered to save. The ObjectNode is created using the object's GUID
            /// and current state. If the object does not have a GUID, one is assigned.
            /// </summary>
            /// <returns>GUID save data</returns>
            public static string SaveGUIDs()
            {
                var registeredObjects = References.GetObjectsRegisteredToSave();
                var nodesToSave = new List<ObjectNode>();

                logger.LogDebug($"Found {registeredObjects.Count} objects to save data for.");

                // Create an ObjectNode for each object registered to save
                foreach (var gameObject in registeredObjects)
                {
                    logger.LogDebug($"Creating node for game object: {gameObject.name}");
                    var node = new ObjectNode();

                    var objectGUID = gameObject.GetComponent<GUID>()?.ID;
                    if (objectGUID == null)
                        objectGUID = NextGUID();    // No component needs to be attached; that will
                                                    // be handled on game load.
                    node.GUID = (uint)objectGUID;

                    // Store other data for error-checking
                    node.name = gameObject.name;
                    node.tag = gameObject.tag;
                    node.position = gameObject.transform.position;
                    node.rotation = gameObject.transform.rotation;

                    logger.LogDebug($"> Saved GUID: {node.GUID}");
                    logger.LogDebug($"> Saved tag: {node.tag}");
                    logger.LogDebug($"> Saved position: {node.position}");
                    logger.LogDebug($"> Saved rotation: {node.rotation}");

                    nodesToSave.Add(node);
                }

                logger.LogDebug("All nodes created, returning JSON encoding.");

                return JsonUtility.ToJson(nodesToSave);
            }

            /// <summary>
            /// Decompose a JSON-encoded snapshot of previously saved GUIDs and reattach them
            /// to their respective game objects.
            /// </summary>
            /// <param name="data">loaded GUID data</param>
            public static void LoadGUIDs(string data)
            {
                maxAssignedID = startID - 1;
                logger.LogDebug("Setting maxAssignedID to default.");

                if (data != null) // Assumption: data found and provided by Persistence module
                {
                    logger.LogDebug("Data retrieved. Parsing object nodes.");
                    var registeredObjects = References.GetObjectsRegisteredToSave();
                    var savedObjectNodes = JsonUtility.FromJson<List<ObjectNode>>(data);
                    logger.LogDebug($"Found {registeredObjects.Count} objects to save data for.");

                    // The entire GUID assignment process assumes there is a one-to-one mapping
                    // between indexes in the SaveLoadManager registered objects and the saved
                    // GUIDs. If this invariant is violated, it indicates a critical error, and
                    // the save data is almost certainly corrupted and unusable.
                    if (registeredObjects.Count != savedObjectNodes.Count)
                    {
                        logger.LogError("ERROR LOADING GUID DATA: Sizes of registeredObjects and" +
                            " savedObjectNodes do not match!");
                        logger.LogError($"{registeredObjects.Count} != {savedObjectNodes.Count}");
                        return;
                        //throw new InvalidOperationException("mismatched object list sizes");

                        // TODO: Decide:
                        //  Should I throw exceptions here, to be handled at the injection point?
                        //  Or should I rely on the error logs to show the problem?
                        //  Leaning towards no exceptions: the only benefit of the exception is the
                        //  stack trace, and errors in these situations should have trivial call
                        //  paths. Maybe switch to exceptions when mods are complex enough that
                        //  tracing the function calls is non-trivial.
                    }

                    for (int i = 0; i < savedObjectNodes.Count; i++)
                    {
                        // TODO: error checking using saved metadata

                        logger.LogDebug($"Attaching GUID {savedObjectNodes[i].GUID} to object" +
                            $" {registeredObjects[i].name}");
                        var guidComponent = registeredObjects[i].AddComponent<GUID>();
                        guidComponent.ID = savedObjectNodes[i].GUID;

                        if (guidComponent.ID > maxAssignedID)
                            maxAssignedID = guidComponent.ID;
                    }

                    logger.LogDebug("Finished attaching GUIDs.");
                    logger.LogDebug($"Max assigned ID: {maxAssignedID}");

                    // TODO: find and fill holes in assigned IDs
                }
            }
        }

        /// <summary>
        /// Uses ModUtilities GUIDs to keep track of mod data between game sessions.
        /// Client mods provide JSON-encoded data, which is then encoded into
        /// a master .modsave file.
        /// </summary>
        public static class Persistence
        {
            /// <summary>
            /// Holds the save/load functions associated with a particular mod.
            /// </summary>
            struct PersistenceProcedures
            {
                public Func<string> saveProcedure;
                public Action<string> loadProcedure;
            }

            /// <summary>
            /// Ties a string holding JSON-encoded data to a particular mod ID.
            /// </summary>
            struct ModData
            {
                public string modID;
                public string modData;
            }

            static string ModsaveFilepath
            {
                get
                {
                    return $"{Application.persistentDataPath}/slot{SaveSlots.currentSlot}.modsave";
                }
            }

            static Dictionary<string, PersistenceProcedures> registeredProcedures = 
                new Dictionary<string, PersistenceProcedures>();

            /// <summary>
            /// Add a new pair of save/load procedures to this classes dictionary,
            /// indexed by the mod's ID.
            /// </summary>
            /// <param name="modID">the index of the entry</param>
            /// <param name="saveProcedure">the mod-specific save procedure</param>
            /// <param name="loadProcedure">the mod-specific load procedure</param>
            public static void RegisterProcedures(string modID, Func<string> saveProcedure, Action<string> loadProcedure)
            {
                // Using array access means new procedures are added, and existing are overwritten.
                registeredProcedures[modID] = new PersistenceProcedures
                {
                    saveProcedure = saveProcedure,
                    loadProcedure = loadProcedure,
                };
            }

            /// <summary>
            /// Iterate through the registered procedure entries, creating a ModData object
            /// for each one. Then store the ModData collection in a JSON-encoded file.
            /// </summary>
            internal static void SaveModData()
            {
                logger.LogInfo("Saving mod data.");

                var modDataEntries = new List<ModData>();

                // Collect the mod data entries
                // Note: this assumes that no save procedures are dependent on each other.
                // Dependency must be handled internally by mods using Persistence.
                foreach (var procedureSet in registeredProcedures)
                {
                    var modID = procedureSet.Key;
                    var procedures = procedureSet.Value;
                    string modData;

                    logger.LogDebug($"Beginning save process for mod: {modID}");

                    try { modData = procedures.saveProcedure.Invoke(); }
                    catch (Exception e)
                    {
                        logger.LogError($"Error saving mod data");
                        logger.LogError(e); // TODO: verify this actually makes sense when output
                        continue;
                    }

                    var modDataEntry = new ModData
                    {
                        modID = modID,
                        modData = modData,
                    };

                    modDataEntries.Add(modDataEntry);
                    logger.LogDebug($"Save data for mod '{modID}' successfully collected.");
                    // TODO: check for duplicate entries?
                }

                logger.LogDebug($"Attempting to save to disk at: {ModsaveFilepath}");

                try
                {
                    // Encode the mod data collection and save to disk.
                    var saveData = JsonUtility.ToJson(modDataEntries);
                    var filestream = File.Create(ModsaveFilepath);
                    var filewriter = new StreamWriter(filestream);
                    filewriter.WriteLine(saveData); // WriteLine so the file ends with a newline
                    filewriter.Close();
                    logger.LogInfo("Successfully saved mod data to disk.");
                }
                catch (Exception e)
                {
                    logger.LogError("CRITICAL ERROR IN PERSISTENCE MODULE:" +
                        " Could not save mod data to disk!");
                    logger.LogError(e);
                    return;
                }

                logger.LogInfo("Finished saving mod data.");
            }

            /// <summary>
            /// Retrieve the JSON-encoded mod data container, and delegate loading to the
            /// registered mods.
            /// </summary>
            internal static void LoadModData()
            {
                logger.LogInfo("Loading mod data.");
                logger.LogDebug($"Attempting to load from disk at: {ModsaveFilepath}");
                List<ModData> loadedData;

                try
                {
                    // Retrieve the mod data from disk.
                    var filestream = File.OpenRead(ModsaveFilepath);
                    var filereader = new StreamReader(filestream);
                    var rawData = filereader.ReadToEnd();
                    filereader.Close();
                    loadedData = JsonUtility.FromJson<List<ModData>>(rawData);
                }
                catch (Exception e)
                {
                    logger.LogError("CRITICAL ERROR IN PERSISTENCE MODULE:" +
                        " Could not load mod data from disk!");
                    logger.LogError(e);
                    return;
                }

                // Note: Again, dependency is not handled here.
                // Theoretically, there could be massive issues if a mod relies on GUIDs,
                // and that mods load procedure is called before the GUID loading injector.
                // I'm assuming right now that won't be an issue, since I think ModUtilities
                // will always have its own save/load procedures as the first in the list, and
                // therefore the first to run using foreach.
                foreach (var modDataEntry in loadedData)
                {
                    var modID = modDataEntry.modID;
                    var savedData = modDataEntry.modData;

                    logger.LogDebug($"Beginning load process for mod: {modID}");

                    try { registeredProcedures[modID].loadProcedure.Invoke(savedData); }
                    catch (Exception e)
                    {
                        logger.LogError("Error loading mod data");
                        logger.LogError(e);
                        continue;
                    }

                    logger.LogDebug($"Mod data for mod '{modID}' successfully loaded.");
                }

                logger.LogInfo("Finished loading mod data.");
            }
        }

        public static class References
        {
            static GoPointer playerLookPointer;

            /// <summary>
            /// Retrieves the GoPointer associated with the mouse. Result is cached.
            /// </summary>
            /// <returns>player look GoPointer</returns>
            public static GoPointer GetPlayerLookPointer()
            {
                if (playerLookPointer == null)
                    playerLookPointer = GameObject.Find("Go Pointer (mouse)")
                        .GetComponent<GoPointer>();

                return playerLookPointer;
            }

            /// <summary>
            /// Retrieves the list of GameObjects which have been registered to the
            /// Save-Load Manager.
            /// </summary>
            /// <returns></returns>
            public static List<GameObject> GetObjectsRegisteredToSave()
            {
                var currentlyRegisteredPrefabs = Traverse.Create(SaveLoadManager.instance)
                    .Field<List<SaveablePrefab>>("currentPrefabs").Value;
                return currentlyRegisteredPrefabs.Select(prefab => prefab.gameObject).ToList();
            }
        }
    }
}
