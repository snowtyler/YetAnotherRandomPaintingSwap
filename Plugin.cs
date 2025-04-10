using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YetAnotherRandomPaintingSwap
{
    [BepInPlugin("snowtyler.YetAnotherRandomPaintingSwap", "YetAnotherRandomPaintingSwap", "1.1.0")]
    public class YetAnotherRandomPaintingSwap : BaseUnityPlugin
    {
        public class PaintingGroup
        {
            public string paintingType;
            public string paintingFolderName;
            public HashSet<string> targetMaterials;
            public List<Material> loadedMaterials;

            public PaintingGroup(string paintingType, string paintingFolderName, HashSet<string> targetMaterials)
            {
                this.paintingType = paintingType;
                this.paintingFolderName = paintingFolderName;
                this.targetMaterials = targetMaterials;
                loadedMaterials = new List<Material>();
            }
        }

        [HarmonyPatch(typeof(LoadingUI), "LevelAnimationComplete")]
        public class PaintingSwapPatch
        {
            private static void Postfix()
            {
                Task.Run(async delegate
                {
                    int waited = 0;
                    int interval = 50;
                    while (!receivedSeed.HasValue && waited < maxWaitTimeMs)
                    {
                        await Task.Delay(interval);
                        waited += interval;
                    }
                    if (receivedSeed.HasValue)
                    {
                        logger.LogInfo($"[Postfix] Client using received seed: {receivedSeed.Value}");
                        YetAnotherRandomPaintingSwapSwap.ReceivedSeed = receivedSeed.Value;
                        receivedSeed = null;
                    }
                    else
                    {
                        logger.LogWarning("[Postfix] Client did not receive seed in time. Proceeding without it.");
                    }
                    swapper.ReplacePaintings();
                });
            }

            private static void Prefix()
            {
                if (swapper.GetModState() == YetAnotherRandomPaintingSwapSwap.ModState.Client)
                {
                    PhotonNetwork.AddCallbackTarget(sync);
                }
                if (swapper.GetModState() == YetAnotherRandomPaintingSwapSwap.ModState.Host)
                {
                    YetAnotherRandomPaintingSwapSwap.HostSeed = UnityEngine.Random.Range(0, int.MaxValue);
                    logger.LogInfo($"Generated Hostseed: {YetAnotherRandomPaintingSwapSwap.HostSeed}");
                    PhotonNetwork.AddCallbackTarget(sync);
                    sync.SendSeed(YetAnotherRandomPaintingSwapSwap.HostSeed);
                }
            }
        }

        [HarmonyPatch(typeof(NetworkConnect), "TryJoiningRoom")]
        public class JoinLobbyPatch
        {
            private static void Prefix()
            {
                logger.LogInfo("JoinLobbyPatch Prefix called.");
                if (swapper.GetModState() == YetAnotherRandomPaintingSwapSwap.ModState.SinglePlayer)
                {
                    swapper.SetState(YetAnotherRandomPaintingSwapSwap.ModState.Client);
                }
            }
        }

        [HarmonyPatch(typeof(SteamManager), "HostLobby")]
        public class HostLobbyPatch
        {
            private static bool Prefix()
            {
                logger.LogInfo("HostLobbyPatch Prefix called.");
                if (swapper.GetModState() != 0)
                {
                    swapper.SetState(YetAnotherRandomPaintingSwapSwap.ModState.Host);
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(SteamManager), "LeaveLobby")]
        public class LeaveLobbyPatch
        {
            private static void Postfix()
            {
                PhotonNetwork.RemoveCallbackTarget(sync);
                swapper.SetState(YetAnotherRandomPaintingSwapSwap.ModState.SinglePlayer);
            }
        }

        public static List<PaintingGroup> paintingGroups = new List<PaintingGroup>
        {
            new PaintingGroup("Landscape", "RandomLandscapePaintingSwap_Images", new HashSet<string> { "Painting_H_Landscape" }),
            new PaintingGroup("Portrait", "RandomPortraitPaintingSwap_Images", new HashSet<string> { "Painting_V_Furman", "painting teacher01", "painting teacher02", "painting teacher03", "painting teacher04", "Painting_S_Tree" }),
            new PaintingGroup("Square", "RandomSquarePaintingSwap_Images", new HashSet<string> { "Painting_S_Creep", "Painting_S_Creep 2_0", "Painting_S_Creep 2", "Painting Wizard Class" })
        };

        private static Logger logger = null;
        private static YetAnotherRandomPaintingSwapLoader loader = null;
        private static YetAnotherRandomPaintingSwapSwap swapper = null;
        private static YetAnotherRandomPaintingSwapSync sync = null;
        private static GrungeMaterialManager grungeMaterialManager = null;

        public static int? receivedSeed = null;
        public static readonly int maxWaitTimeMs = 3000;

        private readonly Harmony harmony = new Harmony("snowtyler.YetAnotherRandomPaintingSwap");

        private void Awake()
        {
            logger = new Logger("YetAnotherRandomPaintingSwap");
            logger.LogInfo("YetAnotherRandomPaintingSwap mod initialized.");
            
            // Initialize configuration
            PluginConfig.Init(Config);
            
            // Log the grunge configuration
            bool grungeEnabled = PluginConfig.Grunge.enableGrunge.Value;
            logger.LogInfo($"Grunge effect is {(grungeEnabled ? "ENABLED" : "DISABLED")} in configuration");
            
            // Initialize grunge material manager
            grungeMaterialManager = new GrungeMaterialManager(logger);
            
            // Only attempt to load grunge materials if enabled
            bool grungeLoaded = false;
            if (grungeEnabled)
            {
                grungeLoaded = grungeMaterialManager.LoadGrungeMaterials();
                if (grungeLoaded)
                {
                    logger.LogInfo("Grunge materials loaded successfully");
                }
                else
                {
                    logger.LogWarning("Failed to load grunge materials");
                }
            }
            
            // Initialize loader with grunge manager
            loader = new YetAnotherRandomPaintingSwapLoader(logger, grungeMaterialManager);
            loader.LoadImagesFromAllPlugins();
            
            swapper = new YetAnotherRandomPaintingSwapSwap(logger, loader);
            sync = new YetAnotherRandomPaintingSwapSync(logger);
            
            harmony.PatchAll();
            
            if (PluginConfig.enableDebugLog.Value)
            {
                logger.LogInfo("Debug logging enabled");
            }
        }
        
        private void OnDestroy()
        {
            // Clean up when plugin is unloaded
            harmony.UnpatchSelf();
        }
    }

    internal static class PluginConfig
    {
        internal static ConfigEntry<bool> enableDebugLog;
        internal static ConfigEntry<float> customPaintingChance;

        internal static class Grunge
        {
            internal static ConfigEntry<bool> enableGrunge;
            internal static ConfigEntry<Color> _BaseColor;
            internal static ConfigEntry<Color> _MainColor;
            internal static ConfigEntry<Color> _CracksColor;
            internal static ConfigEntry<Color> _OutlineColor;
            internal static ConfigEntry<float> _CracksPower;
        }

        internal static void Init(ConfigFile config)
        {
            enableDebugLog = config.Bind(
                "General",
                "DebugLog",
                false,
                "Print extra logs for debugging"
            );

            customPaintingChance = config.Bind(
                "General",
                "CustomPaintingChance",
                1.0f,
                "The chance of a painting being replaced by a custom painting (1 = 100%, 0.5 = 50%)"
            );

            Grunge.enableGrunge = config.Bind(
                "Grunge",
                "EnableGrunge",
                true,
                "Whether the grunge effect is enabled"
            );

            Grunge._BaseColor = config.Bind(
                "Grunge",
                "_GrungeBaseColor",
                new Color(0, 0, 0, 1),
                "The base color of the grunge"
            );

            Grunge._MainColor = config.Bind(
                "Grunge",
                "_GrungeMainColor",
                new Color(0, 0, 0, 1),
                "The color of the main overlay of grunge"
            );

            Grunge._CracksColor = config.Bind(
                "Grunge",
                "_GrungeCracksColor",
                new Color(0, 0, 0, 1),
                "The color of the cracks in the grunge"
            );

            Grunge._OutlineColor = config.Bind(
                "Grunge",
                "_GrungeOutlineColor",
                new Color(0, 0, 0, 1),
                "The color of the grunge outlining the painting"
            );

            Grunge._CracksPower = config.Bind(
                "Grunge",
                "_GrungeCracksPower",
                1.0f,
                "The inverse of intensity of the cracks. 1.0 will have plenty of cracks, higher numbers will have less cracks (Values below 1.0 will start to look bad)"
            );
        }
    }

    public class GrungeMaterialManager
    {
        private const string MATERIAL_LANDSCAPE_ASSET_NAME = "GrungeHorizontalMaterial";
        private const string MATERIAL_PORTRAIT_ASSET_NAME = "GrungeVerticalMaterial";
        private const string ASSET_BUNDLE_NAME = "painting";

        // Shader property names
        private const string PROP_BASE_COLOR = "_BaseColor";
        private const string PROP_MAIN_COLOR = "_MainColor";
        private const string PROP_CRACKS_COLOR = "_CracksColor";
        private const string PROP_OUTLINE_COLOR = "_OutlineColor";
        private const string PROP_CRACKS_POWER = "_CracksPower";
        private const string PROP_MAIN_TEX = "_MainTex";

        private Material _landscapeMaterial;
        private Material _portraitMaterial;
        private readonly Logger logger;

        public GrungeMaterialManager(Logger logger)
        {
            this.logger = logger;
        }

        public bool LoadGrungeMaterials()
        {
            // Early return if grunge is disabled
            if (!IsGrungeEnabled())
            {
                logger.LogInfo("Skipping grunge material loading as it's disabled in config");
                return false;
            }

            string location = Assembly.GetExecutingAssembly().Location;
            string directoryName = Path.GetDirectoryName(location);
            string assetLocation = Path.Combine(directoryName, ASSET_BUNDLE_NAME);
            
            logger.LogInfo($"Loading asset bundle from: {assetLocation}");
            
            if (!File.Exists(assetLocation))
            {
                logger.LogWarning($"Asset Bundle doesn't exist at: {assetLocation}");
                return false;
            }

            AssetBundle assetBundle = AssetBundle.LoadFromFile(assetLocation);
            if (assetBundle == null)
            {
                logger.LogError($"Failed to load asset bundle: {ASSET_BUNDLE_NAME}");
                return false;
            }

            _landscapeMaterial = assetBundle.LoadAsset<Material>(MATERIAL_LANDSCAPE_ASSET_NAME);
            if (_landscapeMaterial == null)
            {
                logger.LogError($"Could not load landscape painting material [{MATERIAL_LANDSCAPE_ASSET_NAME}]!");
                return false;
            }

            _portraitMaterial = assetBundle.LoadAsset<Material>(MATERIAL_PORTRAIT_ASSET_NAME);
            if (_portraitMaterial == null)
            {
                logger.LogError($"Could not load portrait painting material [{MATERIAL_PORTRAIT_ASSET_NAME}]!");
                return false;
            }

            // Configure the template materials
            ConfigureGrungeMaterials();
            
            return true;
        }

        private void ConfigureGrungeMaterials()
        {
            logger.LogInfo($"Configuring grunge materials with CracksPower: {PluginConfig.Grunge._CracksPower.Value}");
            
            // Configure landscape material
            ConfigureGrungeMaterial(_landscapeMaterial);
            
            // Configure portrait material
            ConfigureGrungeMaterial(_portraitMaterial);
        }
        
        private void ConfigureGrungeMaterial(Material material)
        {
            if (material == null) return;
            
            material.SetColor(PROP_BASE_COLOR, PluginConfig.Grunge._BaseColor.Value);
            material.SetColor(PROP_MAIN_COLOR, PluginConfig.Grunge._MainColor.Value);
            material.SetColor(PROP_CRACKS_COLOR, PluginConfig.Grunge._CracksColor.Value);
            material.SetColor(PROP_OUTLINE_COLOR, PluginConfig.Grunge._OutlineColor.Value);
            material.SetFloat(PROP_CRACKS_POWER, PluginConfig.Grunge._CracksPower.Value);
            
            // Ensure the shader knows these properties have changed
            material.shader.maximumLOD = 600;
        }

        public Material CreateMaterialInstance(string paintingType, Texture2D texture)
        {
            // Always check current config state
            if (!IsGrungeEnabled())
            {
                return null;
            }
            
            Material baseMaterial = GetBaseMaterial(paintingType);
            if (baseMaterial == null) return null;
            
            // Create a new material instance with all properties copied
            Material newMaterial = new Material(baseMaterial);
            
            // Apply the texture
            newMaterial.SetTexture(PROP_MAIN_TEX, texture);
            
            // Re-apply the grunge settings to ensure they're correctly set
            newMaterial.SetColor(PROP_BASE_COLOR, PluginConfig.Grunge._BaseColor.Value);
            newMaterial.SetColor(PROP_MAIN_COLOR, PluginConfig.Grunge._MainColor.Value);
            newMaterial.SetColor(PROP_CRACKS_COLOR, PluginConfig.Grunge._CracksColor.Value);
            newMaterial.SetColor(PROP_OUTLINE_COLOR, PluginConfig.Grunge._OutlineColor.Value);
            newMaterial.SetFloat(PROP_CRACKS_POWER, PluginConfig.Grunge._CracksPower.Value);
            
            return newMaterial;
        }

        private Material GetBaseMaterial(string paintingType)
        {
            if (paintingType == "Portrait")
            {
                return _portraitMaterial;
            }
            else // Square and Landscape paintings use the same material
            {
                return _landscapeMaterial;
            }
        }
        
        public bool IsGrungeEnabled()
        {
            // Always check the current value from config
            return PluginConfig.Grunge.enableGrunge.Value;
        }
    }

    public class YetAnotherRandomPaintingSwapLoader
    {
        private readonly Logger logger;
        private readonly GrungeMaterialManager grungeMaterialManager;
        private static readonly string[] validExtensions = new string[] { ".png", ".jpg", ".jpeg", ".bmp" };

        public List<Material> LoadedMaterials { get; } = new List<Material>();

        public YetAnotherRandomPaintingSwapLoader(Logger logger, GrungeMaterialManager grungeMaterialManager)
        {
            this.logger = logger;
            this.grungeMaterialManager = grungeMaterialManager;
        }

        public void LoadImagesFromAllPlugins()
        {
            string pluginPath = Paths.PluginPath;
            if (!Directory.Exists(pluginPath))
            {
                logger.LogWarning("Plugins directory not found: " + pluginPath);
                return;
            }
            
            // Log the grunge state at load time
            logger.LogInfo($"Grunge effect is {(grungeMaterialManager.IsGrungeEnabled() ? "enabled" : "disabled")} while loading images");
            
            foreach (YetAnotherRandomPaintingSwap.PaintingGroup paintingGroup in YetAnotherRandomPaintingSwap.paintingGroups)
            {
                string[] directories = Directory.GetDirectories(pluginPath, paintingGroup.paintingFolderName, SearchOption.AllDirectories);
                if (directories.Length == 0)
                {
                    logger.LogWarning("No '" + paintingGroup.paintingFolderName + "' folders found in plugins.");
                    continue;
                }
                string[] array = directories;
                foreach (string text in array)
                {
                    logger.LogInfo("Loading images from: " + text);
                    LoadImagesFromDirectory(paintingGroup, text);
                }
            }
        }

        private void LoadImagesFromDirectory(YetAnotherRandomPaintingSwap.PaintingGroup paintingGroup, string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                logger.LogWarning("Directory does not exist: " + directoryPath);
                return;
            }

            string[] array = (from file in Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                             where validExtensions.Contains(Path.GetExtension(file).ToLower())
                             select file).ToArray();
            
            if (array.Length == 0)
            {
                logger.LogWarning("No images found in " + directoryPath);
                return;
            }

            // Always check current config state
            bool useGrunge = grungeMaterialManager.IsGrungeEnabled();
            logger.LogInfo($"Creating materials with grunge effect {(useGrunge ? "enabled" : "disabled")}");
            
            for (int i = 0; i < array.Length; i++)
            {
                string filePath = array[i];
                string fileName = Path.GetFileName(filePath);
                Texture2D texture = LoadTextureFromFile(filePath);
                
                if (texture != null)
                {
                    Material material;
                    
                    if (useGrunge)
                    {
                        // Use the grunge material manager to create a properly configured material
                        material = grungeMaterialManager.CreateMaterialInstance(paintingGroup.paintingType, texture);
                        
                        // Fallback to standard shader if grunge material creation failed
                        if (material == null)
                        {
                            material = CreateStandardMaterial(texture);
                            logger.LogInfo($"Using standard shader for image: {fileName} (grunge creation failed)");
                        }
                        else
                        {
                            logger.LogInfo($"Created grunge material for image: {fileName}");
                        }
                    }
                    else
                    {
                        // Use standard shader if grunge is disabled
                        material = CreateStandardMaterial(texture);
                        logger.LogInfo($"Using standard shader for image: {fileName} (grunge disabled)");
                    }
                    
                    paintingGroup.loadedMaterials.Add(material);
                    logger.LogInfo($"Loaded image #{i + 1}: {fileName}");
                }
                else
                {
                    logger.LogWarning($"Failed to load image #{i + 1}: {filePath}");
                }
            }
            
            logger.LogInfo($"Total images loaded for {paintingGroup.paintingType}: {paintingGroup.loadedMaterials.Count}");
        }

        private Material CreateStandardMaterial(Texture2D texture)
        {
            // Create a simple standard material with just the texture
            return new Material(Shader.Find("Standard"))
            {
                mainTexture = texture
            };
        }

        private Texture2D LoadTextureFromFile(string filePath)
        {
            byte[] array = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            if (ImageConversion.LoadImage(texture, array))
            {
                texture.Apply();
                return texture;
            }
            return null;
        }
    }

    public class YetAnotherRandomPaintingSwapSwap
    {
        public enum ModState
        {
            Host,
            Client,
            SinglePlayer
        }

        private readonly Logger logger;
        private readonly YetAnotherRandomPaintingSwapLoader loader;
        private static int randomSeed = 0;
        public static int HostSeed = 0;
        public static int ReceivedSeed = 0;
        public static int Seed = 0;
        private int paintingsChangedCount;
        private static ModState currentState = ModState.SinglePlayer;

        public YetAnotherRandomPaintingSwapSwap(Logger logger, YetAnotherRandomPaintingSwapLoader loader)
        {
            this.logger = logger;
            this.loader = loader;
            logger.LogInfo($"Initial ModState: {currentState}");
            if (randomSeed == 0)
            {
                randomSeed = UnityEngine.Random.Range(0, int.MaxValue);
                logger.LogInfo($"Generated initial random seed: {randomSeed}");
            }
        }

        public ModState GetModState()
        {
            return currentState;
        }

        public void ReplacePaintings()
        {
            ModState modState = currentState;
            int seed = modState switch
            {
                ModState.SinglePlayer => randomSeed,
                ModState.Host => HostSeed,
                ModState.Client => ReceivedSeed,
                _ => Seed,
            };
            
            Seed = seed;
            Scene activeScene = SceneManager.GetActiveScene();
            logger.LogInfo($"Applying seed {Seed} for painting swaps in scene: {activeScene.name}");
            logger.LogInfo("Replacing materials with custom images...");
            
            paintingsChangedCount = 0;
            int materialsChecked = 0;
            GameObject[] rootGameObjects = activeScene.GetRootGameObjects();
            
            foreach (GameObject gameObject in rootGameObjects)
            {
                MeshRenderer[] componentsInChildren = gameObject.GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer meshRenderer in componentsInChildren)
                {
                    Material[] sharedMaterials = meshRenderer.sharedMaterials;
                    bool materialsChanged = false;
                    
                    for (int k = 0; k < sharedMaterials.Length; k++)
                    {
                        materialsChecked++;
                        foreach (YetAnotherRandomPaintingSwap.PaintingGroup paintingGroup in YetAnotherRandomPaintingSwap.paintingGroups)
                        {
                            if (sharedMaterials[k] != null && 
                                paintingGroup.targetMaterials.Contains(sharedMaterials[k].name) && 
                                paintingGroup.loadedMaterials.Count > 0)
                            {
                                // Apply custom painting chance from config
                                if (UnityEngine.Random.value > PluginConfig.customPaintingChance.Value)
                                {
                                    if (PluginConfig.enableDebugLog.Value)
                                    {
                                        logger.LogInfo($"Skipping replacement of {sharedMaterials[k].name} due to chance setting");
                                    }
                                    continue;
                                }
                                
                                int index = Mathf.Abs((Seed + paintingsChangedCount) % paintingGroup.loadedMaterials.Count);
                                sharedMaterials[k] = paintingGroup.loadedMaterials[index];
                                paintingsChangedCount++;
                                materialsChanged = true;
                                
                                if (PluginConfig.enableDebugLog.Value)
                                {
                                    logger.LogInfo($"Replaced {meshRenderer.gameObject.name} material {k} with {paintingGroup.paintingType} painting");
                                }
                            }
                        }
                    }
                    
                    // Only apply if we actually changed materials
                    if (materialsChanged)
                    {
                        meshRenderer.sharedMaterials = sharedMaterials;
                    }
                }
            }
            
            logger.LogInfo($"Total materials checked: {materialsChecked}");
            logger.LogInfo($"Total paintings changed in this scene: {paintingsChangedCount}");
            logger.LogInfo($"RandomSeed = {randomSeed}");
            logger.LogInfo($"HostSeed = {HostSeed}");
            logger.LogInfo($"ReceivedSeed = {ReceivedSeed}");
        }

        public void SetState(ModState newState)
        {
            currentState = newState;
            logger.LogInfo($"Mod state set to: {currentState}");
        }
    }

    public class YetAnotherRandomPaintingSwapSync : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private readonly Logger logger;
        public const byte SeedEventCode = 1;

        public YetAnotherRandomPaintingSwapSync(Logger logger)
        {
            this.logger = logger;
        }

        public void SendSeed(int seed)
        {
            object[] array = new object[1] { seed };
            logger.LogInfo("Sharing seed with other clients");
            RaiseEventOptions options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others,
                CachingOption = EventCaching.AddToRoomCache
            };
            PhotonNetwork.RaiseEvent(SeedEventCode, array, options, SendOptions.SendReliable);
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == SeedEventCode)
            {
                int num = (int)((object[])photonEvent.CustomData)[0];
                logger.LogInfo($"Received seed: {num}");
                YetAnotherRandomPaintingSwap.receivedSeed = num;
            }
        }
    }

    public class Logger
    {
        private readonly string logFilePath;
        private readonly ManualLogSource logSource;

        public Logger(string modName)
        {
            string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            logFilePath = Path.Combine(directoryName, modName + "_log.txt");
            if (File.Exists(logFilePath))
            {
                File.Delete(logFilePath);
            }
            logSource = BepInEx.Logging.Logger.CreateLogSource(modName);
        }

        public void LogInfo(string message)
        {
            WriteLog("INFO", message);
            logSource.LogInfo(message);
        }

        public void LogWarning(string message)
        {
            WriteLog("WARNING", message);
            logSource.LogWarning(message);
        }

        public void LogError(string message)
        {
            WriteLog("ERROR", message);
            logSource.LogError(message);
        }

        private void WriteLog(string level, string message)
        {
            string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            File.AppendAllText(logFilePath, text + Environment.NewLine);
        }

        public void LogMaterial(Material material)
        {
            if (material != null && material.name.ToLower().Contains("painting"))
            {
                LogInfo("Material containing 'painting': " + material.name);
            }
        }

        public void ClearLog()
        {
            if (File.Exists(logFilePath))
            {
                File.Delete(logFilePath);
            }
        }
    }
}