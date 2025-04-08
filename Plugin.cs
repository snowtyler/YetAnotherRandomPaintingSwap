using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

namespace YetAnotherRandomPaintingSwap
{
    [BepInPlugin("snowtyler.YetAnotherRandomPaintingSwap", "Yet Another Random Painting Swap", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        // Configuration settings
        public static class PluginConfig
        {
            public static ConfigEntry<bool> GrungeEnabled;
            public static ConfigEntry<float> CustomPaintingChance;
            public static ConfigEntry<Color> BaseColor;
            public static ConfigEntry<Color> MainColor;
            public static ConfigEntry<Color> CracksColor;
            public static ConfigEntry<Color> OutlineColor;
            public static ConfigEntry<float> CracksPower;

            public static void Init(ConfigFile config)
            {
                GrungeEnabled = config.Bind("General", "GrungeEnabled", true, "Enable grunge assets");
                CustomPaintingChance = config.Bind("General", "CustomPaintingChance", 1.0f, "Chance (0-1) to replace a painting");

                BaseColor = config.Bind("Grunge", "BaseColor", Color.gray, "Base color for grunge material");
                MainColor = config.Bind("Grunge", "MainColor", Color.white, "Main color for grunge material");
                CracksColor = config.Bind("Grunge", "CracksColor", Color.black, "Cracks color for grunge material");
                OutlineColor = config.Bind("Grunge", "OutlineColor", Color.red, "Outline color for grunge material");
                CracksPower = config.Bind("Grunge", "CracksPower", 1.0f, "Cracks power for grunge material");
            }
        }

        // Defines painting groups used for material swapping
        public class PaintingGroup
        {
            public string paintingType;
            public string paintingFolderName;
            public HashSet<string> whitelistMaterials;
            public List<Material> loadedMaterials;
            public List<string> loadedTextureNames;
            public Material baseMaterial = null;

            public PaintingGroup(string type, string folderName, HashSet<string> whitelist)
            {
                paintingType = type;
                paintingFolderName = folderName;
                whitelistMaterials = whitelist;
                loadedMaterials = new List<Material>();
                loadedTextureNames = new List<string>();
            }
        }

        // Whitelists (union from both mods)
        public static readonly HashSet<string> whitelistLandscapeMaterials = new HashSet<string>
        {
            "Painting_H_Landscape", "Painting_H_crow", "Painting_H_crow_0"
        };
        public static readonly HashSet<string> whitelistSquareMaterials = new HashSet<string>
        {
            "Painting_S_Creep", "Painting_S_Creep 2_0", "Painting_S_Creep 2", "Painting Wizard Class"
        };
        public static readonly HashSet<string> whitelistPortraitMaterials = new HashSet<string>
        {
            "Painting_V_jannk", "Painting_V_Furman", "Painting_V_surrealistic", "Painting_V_surrealistic_0",
            "painting teacher01", "painting teacher02", "painting teacher03", "painting teacher04", "Painting_S_Tree"
        };

        // Define painting groups with folder names (second mod naming used)
        public static List<PaintingGroup> paintingGroups = new List<PaintingGroup>
        {
            new PaintingGroup("Landscape", "RandomLandscapePaintingSwap_Images", whitelistLandscapeMaterials),
            new PaintingGroup("Square", "RandomSquarePaintingSwap_Images", whitelistSquareMaterials),
            new PaintingGroup("Portrait", "RandomPortraitPaintingSwap_Images", whitelistPortraitMaterials)
        };

        // File search patterns for images
        public static readonly List<string> imagePatterns = new List<string>
        {
            "*.png", "*.jpg", "*.jpeg", "*.bmp"
        };

        // Asset bundle settings for grunge materials
        private const string ASSET_BUNDLE_NAME = "painting";
        private const string MATERIAL_LANDSCAPE_ASSET_NAME = "GrungeHorizontalMaterial";
        private const string MATERIAL_PORTRAIT_ASSET_NAME = "GrungeVerticalMaterial";
        private static Material _LandscapeMaterial;
        private static Material _PortraitMaterial;

        // Multiplayer and seed settings
        public enum ModState { Host, Client, SinglePlayer }
        public static ModState currentState = ModState.SinglePlayer;
        public static int randomSeed = 0;
        public static int HostSeed = 0;
        public static int ReceivedSeed = 0;
        public static int Seed = 0;
        public static readonly int maxWaitTimeMs = 3000;

        // Instance and logger
        public static Plugin Instance { get; private set; }
        internal static ManualLogSource Log;
        private readonly Harmony harmony = new Harmony("snowtyler.YetAnotherRandomPaintingSwap");
        internal static PaintingSync sync;

        private void Awake()
        {
            Instance = this;
            Log = base.Logger;
            PluginConfig.Init(Config);
            Log.LogInfo("Yet Another Random Painting Swap mod initialized.");

            // Initialize random seed if not set
            if (randomSeed == 0)
            {
                randomSeed = UnityEngine.Random.Range(0, int.MaxValue);
                Log.LogInfo($"Generated initial random seed: {randomSeed}");
            }

            // Load asset bundle and assign base materials if grunge option is enabled
            if (PluginConfig.GrungeEnabled.Value)
            {
                AssignMaterialGroups();
            }

            LoadImagesFromAllPlugins();

            // Add network sync component
            sync = gameObject.AddComponent<PaintingSync>();

            harmony.PatchAll();
        }

        private void AssignMaterialGroups()
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string directoryName = Path.GetDirectoryName(assemblyLocation);
            string assetLocation = Path.Combine(directoryName, ASSET_BUNDLE_NAME);
            Log.LogInfo($"Loading asset bundle from: {assetLocation}");
            if (!File.Exists(assetLocation))
            {
                Log.LogWarning("Asset bundle not found.");
                return;
            }
            AssetBundle bundle = AssetBundle.LoadFromFile(assetLocation);
            if (bundle == null)
            {
                Log.LogError("Failed to load asset bundle.");
                return;
            }
            _LandscapeMaterial = bundle.LoadAsset<Material>(MATERIAL_LANDSCAPE_ASSET_NAME);
            if (_LandscapeMaterial == null)
                Log.LogError($"Could not load landscape material [{MATERIAL_LANDSCAPE_ASSET_NAME}]!");
            _PortraitMaterial = bundle.LoadAsset<Material>(MATERIAL_PORTRAIT_ASSET_NAME);
            if (_PortraitMaterial == null)
                Log.LogError($"Could not load portrait material [{MATERIAL_PORTRAIT_ASSET_NAME}]!");

            foreach (var group in paintingGroups)
            {
                group.baseMaterial = (group.paintingType == "Portrait") ? _PortraitMaterial : _LandscapeMaterial;
                if (group.baseMaterial == null)
                {
                    Log.LogWarning($"No base material found for [{group.paintingType}]!");
                    continue;
                }
                group.baseMaterial.SetColor(PluginConfig.BaseColor.Definition.Key, PluginConfig.BaseColor.Value);
                group.baseMaterial.SetColor(PluginConfig.MainColor.Definition.Key, PluginConfig.MainColor.Value);
                group.baseMaterial.SetColor(PluginConfig.CracksColor.Definition.Key, PluginConfig.CracksColor.Value);
                group.baseMaterial.SetColor(PluginConfig.OutlineColor.Definition.Key, PluginConfig.OutlineColor.Value);
                group.baseMaterial.SetFloat(PluginConfig.CracksPower.Definition.Key, PluginConfig.CracksPower.Value);
            }
        }

        private void LoadImagesFromAllPlugins()
        {
            string pluginDir = Paths.PluginPath;
            if (!Directory.Exists(pluginDir))
            {
                Log.LogWarning($"Plugins directory not found: {pluginDir}");
                return;
            }
            foreach (var group in paintingGroups)
            {
                string[] directories = Directory.GetDirectories(pluginDir, group.paintingFolderName, SearchOption.AllDirectories);
                if (directories.Length == 0)
                {
                    Log.LogWarning($"No folders named {group.paintingFolderName} found in plugins.");
                    continue;
                }
                foreach (string dir in directories)
                {
                    Log.LogInfo($"Loading images from: {dir}");
                    LoadImagesFromDirectory(group, dir);
                }
            }
        }

        private void LoadImagesFromDirectory(PaintingGroup group, string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Log.LogWarning($"Directory does not exist: {directoryPath}");
                return;
            }
            List<string> imageFiles = new List<string>();
            foreach (string pattern in imagePatterns)
            {
                imageFiles.AddRange(Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories));
            }
            if (imageFiles.Count == 0)
            {
                Log.LogWarning($"No images found in {directoryPath}");
                return;
            }
            for (int i = 0; i < imageFiles.Count; i++)
            {
                string file = imageFiles[i];
                Texture2D texture = LoadTextureFromFile(file);
                if (texture != null)
                {
                    Material mat;
                    if (group.baseMaterial != null)
                    {
                        mat = new Material(group.baseMaterial);
                        mat.SetTexture("_MainTex", texture);
                    }
                    else
                    {
                        mat = new Material(Shader.Find("Standard")) { mainTexture = texture };
                    }
                    group.loadedMaterials.Add(mat);
                    group.loadedTextureNames.Add(Path.GetFileName(file));
                    Log.LogInfo($"Loaded image #{i + 1}: {Path.GetFileName(file)} for group {group.paintingType}");
                }
                else
                {
                    Log.LogWarning($"Failed to load image #{i + 1}: {file}");
                }
            }
            Log.LogInfo($"Total images loaded for {group.paintingType}: {group.loadedMaterials.Count}");
        }

        private Texture2D LoadTextureFromFile(string filePath)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(2, 2);
                if (texture.LoadImage(fileData))
                {
                    texture.Apply();
                    return texture;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Exception loading texture from {filePath}: {ex.Message}");
            }
            return null;
        }

        // Replaces painting materials in the active scene using loaded images and the computed seed.
        public static void ReplacePaintings()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            Log.LogInfo($"Applying seed {Seed} for painting swaps in scene: {activeScene.name}");
            int paintingsChangedCount = 0;
            int totalMaterialsChecked = 0;
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            foreach (GameObject go in rootObjects)
            {
                foreach (MeshRenderer renderer in go.GetComponentsInChildren<MeshRenderer>())
                {
                    Material[] mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        totalMaterialsChecked++;
                        Material currentMat = mats[i];
                        if (currentMat == null) continue;
                        foreach (var group in paintingGroups)
                        {
                            if (group.whitelistMaterials.Contains(currentMat.name) && group.loadedMaterials.Count > 0)
                            {
                                float roll = UnityEngine.Random.Range(0f, 1f);
                                if (roll > PluginConfig.CustomPaintingChance.Value)
                                {
                                    Log.LogInfo($"Skipping replacement for {currentMat.name} with roll {roll}");
                                    continue;
                                }
                                int index = Mathf.Abs((Seed + paintingsChangedCount) % group.loadedMaterials.Count);
                                mats[i] = group.loadedMaterials[index];
                                paintingsChangedCount++;
                                break;
                            }
                        }
                    }
                    renderer.sharedMaterials = mats;
                }
            }
            Log.LogInfo($"Total materials checked: {totalMaterialsChecked}");
            Log.LogInfo($"Total paintings replaced: {paintingsChangedCount}");
            Log.LogInfo($"RandomSeed = {randomSeed}, HostSeed = {HostSeed}, ReceivedSeed = {ReceivedSeed}");
        }

        // Harmony patch for when level animation completes.
        [HarmonyPatch(typeof(LoadingUI), "LevelAnimationComplete")]
        public class PatchLoadingUI
        {
            static void Prefix()
            {
                if (currentState == ModState.Client)
                {
                    PhotonNetwork.AddCallbackTarget(sync);
                }
                if (currentState == ModState.Host)
                {
                    HostSeed = UnityEngine.Random.Range(0, int.MaxValue);
                    Log.LogInfo($"Generated HostSeed: {HostSeed}");
                    PhotonNetwork.AddCallbackTarget(sync);
                    sync.SendSeed(HostSeed);
                }
            }

            static void Postfix()
            {
                Task.Run(async () =>
                {
                    int waited = 0, interval = 50;
                    if (currentState == ModState.Client)
                    {
                        while (ReceivedSeed == 0 && waited < maxWaitTimeMs)
                        {
                            await Task.Delay(interval);
                            waited += interval;
                        }
                        if (ReceivedSeed != 0)
                        {
                            Log.LogInfo($"Client using ReceivedSeed: {ReceivedSeed}");
                            Seed = ReceivedSeed;
                            ReceivedSeed = 0;
                        }
                        else
                        {
                            Log.LogWarning("Client did not receive seed in time. Using fallback.");
                            Seed = randomSeed;
                        }
                    }
                    else if (currentState == ModState.SinglePlayer)
                    {
                        Seed = randomSeed;
                    }
                    else if (currentState == ModState.Host)
                    {
                        Seed = HostSeed;
                    }
                    ReplacePaintings();
                });
            }
        }

        // Patches to update mod state during lobby events.
        [HarmonyPatch(typeof(NetworkConnect), "TryJoiningRoom")]
        public class JoinLobbyPatch
        {
            static void Prefix()
            {
                Log.LogInfo("JoinLobbyPatch Prefix called.");
                if (currentState == ModState.SinglePlayer)
                {
                    currentState = ModState.Client;
                }
            }
        }

        [HarmonyPatch(typeof(SteamManager), "HostLobby")]
        public class HostLobbyPatch
        {
            static bool Prefix()
            {
                Log.LogInfo("HostLobbyPatch Prefix called.");
                if (currentState != ModState.SinglePlayer)
                    currentState = ModState.Host;
                return true;
            }
        }

        [HarmonyPatch(typeof(SteamManager), "LeaveLobby")]
        public class LeaveLobbyPatch
        {
            static void Postfix()
            {
                PhotonNetwork.RemoveCallbackTarget(sync);
                currentState = ModState.SinglePlayer;
            }
        }
    }

    // Network sync class to send and receive the seed using Photon.
    public class PaintingSync : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public const byte SeedEventCode = 1;
        public void SendSeed(int seed)
        {
            object[] content = new object[] { seed };
            var options = new RaiseEventOptions { Receivers = ReceiverGroup.Others, CachingOption = EventCaching.AddToRoomCache };
            PhotonNetwork.RaiseEvent(SeedEventCode, content, options, SendOptions.SendReliable);
            Plugin.Log.LogInfo("Sending seed to other clients");
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == SeedEventCode)
            {
                int received = (int)((object[])photonEvent.CustomData)[0];
                Plugin.Log.LogInfo($"Received seed: {received}");
                Plugin.ReceivedSeed = received;
            }
        }
    }
}
