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

namespace FittingPaintings
{
    [BepInPlugin("ZeroTails.FittingPaintings", "FittingPaintings", "1.1.0")]
    public class FittingPaintings : BaseUnityPlugin
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
                        FittingPaintingsSwap.ReceivedSeed = receivedSeed.Value;
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
                if (swapper.GetModState() == FittingPaintingsSwap.ModState.Client)
                {
                    PhotonNetwork.AddCallbackTarget(sync);
                }
                if (swapper.GetModState() == FittingPaintingsSwap.ModState.Host)
                {
                    FittingPaintingsSwap.HostSeed = UnityEngine.Random.Range(0, int.MaxValue);
                    logger.LogInfo($"Generated Hostseed: {FittingPaintingsSwap.HostSeed}");
                    PhotonNetwork.AddCallbackTarget(sync);
                    sync.SendSeed(FittingPaintingsSwap.HostSeed);
                }
            }
        }

        [HarmonyPatch(typeof(NetworkConnect), "TryJoiningRoom")]
        public class JoinLobbyPatch
        {
            private static void Prefix()
            {
                logger.LogInfo("JoinLobbyPatch Prefix called.");
                if (swapper.GetModState() == FittingPaintingsSwap.ModState.SinglePlayer)
                {
                    swapper.SetState(FittingPaintingsSwap.ModState.Client);
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
                    swapper.SetState(FittingPaintingsSwap.ModState.Host);
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
                swapper.SetState(FittingPaintingsSwap.ModState.SinglePlayer);
            }
        }

        public static List<PaintingGroup> paintingGroups = new List<PaintingGroup>
        {
            new PaintingGroup("Landscape", "FittingSwapLandscapePaintings", new HashSet<string> { "Painting_H_Landscape" }),
            new PaintingGroup("Portrait", "FittingSwapPortraitPaintings", new HashSet<string> { "Painting_V_Furman", "painting teacher01", "painting teacher02", "painting teacher03", "painting teacher04", "Painting_S_Tree" }),
            new PaintingGroup("Square", "FittingSwapSquarePaintings", new HashSet<string> { "Painting_S_Creep", "Painting_S_Creep 2_0", "Painting_S_Creep 2", "Painting Wizard Class" })
        };

        private static Logger logger = null;
        private static FittingPaintingsLoader loader = null;
        private static FittingPaintingsSwap swapper = null;
        private static FittingPaintingsSync sync = null;
        private static GrungeMaterialManager grungeMaterialManager = null;

        public static int? receivedSeed = null;
        public static readonly int maxWaitTimeMs = 3000;

        private readonly Harmony harmony = new Harmony("ZeroTails.FittingPaintings");

        private void Awake()
        {
            logger = new Logger("FittingPaintings");
            logger.LogInfo("FittingPaintings mod initialized.");
            
            // Initialize configuration
            PluginConfig.Init(Config);
            
            // Initialize grunge material manager
            grungeMaterialManager = new GrungeMaterialManager(logger);
            bool grungeLoaded = grungeMaterialManager.LoadGrungeMaterials();
            
            if (grungeLoaded)
            {
                logger.LogInfo("Grunge materials loaded successfully");
            }
            
            // Initialize loader with grunge manager
            loader = new FittingPaintingsLoader(logger, grungeMaterialManager);
            loader.LoadImagesFromAllPlugins();
            
            swapper = new FittingPaintingsSwap(logger, loader);
            sync = new FittingPaintingsSync(logger);
            
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
                "_GrungeCracksPow",
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

        private static Material _landscapeMaterial;
        private static Material _portraitMaterial;
        private readonly Logger logger;

        public GrungeMaterialManager(Logger logger)
        {
            this.logger = logger;
        }

        public bool LoadGrungeMaterials()
        {
            if (!PluginConfig.Grunge.enableGrunge.Value)
            {
                logger.LogInfo("Grunge overlay is disabled in config");
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

            ConfigureGrungeMaterials();
            return true;
        }

        private void ConfigureGrungeMaterials()
        {
            // Configure landscape material
            _landscapeMaterial.SetColor("_BaseColor", PluginConfig.Grunge._BaseColor.Value);
            _landscapeMaterial.SetColor("_MainColor", PluginConfig.Grunge._MainColor.Value);
            _landscapeMaterial.SetColor("_CracksColor", PluginConfig.Grunge._CracksColor.Value);
            _landscapeMaterial.SetColor("_OutlineColor", PluginConfig.Grunge._OutlineColor.Value);
            _landscapeMaterial.SetFloat("_CracksPower", PluginConfig.Grunge._CracksPower.Value);

            // Configure portrait material
            _portraitMaterial.SetColor("_BaseColor", PluginConfig.Grunge._BaseColor.Value);
            _portraitMaterial.SetColor("_MainColor", PluginConfig.Grunge._MainColor.Value);
            _portraitMaterial.SetColor("_CracksColor", PluginConfig.Grunge._CracksColor.Value);
            _portraitMaterial.SetColor("_OutlineColor", PluginConfig.Grunge._OutlineColor.Value);
            _portraitMaterial.SetFloat("_CracksPower", PluginConfig.Grunge._CracksPower.Value);
        }

        public Material GetGrungeMaterial(string paintingType)
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
    }

    public class FittingPaintingsLoader
    {
        private readonly Logger logger;
        private readonly GrungeMaterialManager grungeMaterialManager;
        private static readonly string[] validExtensions = new string[] { ".png", ".jpg", ".jpeg", ".bmp" };

        public List<Material> LoadedMaterials { get; } = new List<Material>();

        public FittingPaintingsLoader(Logger logger, GrungeMaterialManager grungeMaterialManager)
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
            foreach (FittingPaintings.PaintingGroup paintingGroup in FittingPaintings.paintingGroups)
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

        private void LoadImagesFromDirectory(FittingPaintings.PaintingGroup paintingGroup, string directoryPath)
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

            bool useGrunge = PluginConfig.Grunge.enableGrunge.Value;
            Material grungeMaterial = useGrunge ? grungeMaterialManager.GetGrungeMaterial(paintingGroup.paintingType) : null;

            for (int i = 0; i < array.Length; i++)
            {
                string filePath = array[i];
                string fileName = Path.GetFileName(filePath);
                Texture2D texture = LoadTextureFromFile(filePath);
                
                if (texture != null)
                {
                    Material material;
                    
                    if (useGrunge && grungeMaterial != null)
                    {
                        // Create a new material using the grunge material as a base
                        material = new Material(grungeMaterial);
                        material.SetTexture("_MainTex", texture);
                    }
                    else
                    {
                        // Use standard shader if grunge is disabled or not available
                        material = new Material(Shader.Find("Standard"))
                        {
                            mainTexture = texture
                        };
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

    public class FittingPaintingsSwap
    {
        public enum ModState
        {
            Host,
            Client,
            SinglePlayer
        }

        private readonly Logger logger;
        private readonly FittingPaintingsLoader loader;
        private static int randomSeed = 0;
        public static int HostSeed = 0;
        public static int ReceivedSeed = 0;
        public static int Seed = 0;
        private int paintingsChangedCount;
        private static ModState currentState = ModState.SinglePlayer;

        public FittingPaintingsSwap(Logger logger, FittingPaintingsLoader loader)
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
            logger.LogInfo("Replacing all materials containing 'painting' with custom images...");
            
            paintingsChangedCount = 0;
            int num = 0;
            GameObject[] rootGameObjects = activeScene.GetRootGameObjects();
            
            foreach (GameObject gameObject in rootGameObjects)
            {
                MeshRenderer[] componentsInChildren = gameObject.GetComponentsInChildren<MeshRenderer>();
                foreach (MeshRenderer meshRenderer in componentsInChildren)
                {
                    Material[] sharedMaterials = meshRenderer.sharedMaterials;
                    for (int k = 0; k < sharedMaterials.Length; k++)
                    {
                        num++;
                        foreach (FittingPaintings.PaintingGroup paintingGroup in FittingPaintings.paintingGroups)
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
                            }
                        }
                    }
                    meshRenderer.sharedMaterials = sharedMaterials;
                }
            }
            
            logger.LogInfo($"Total materials checked: {num}");
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

    public class FittingPaintingsSync : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private readonly Logger logger;
        public const byte SeedEventCode = 1;

        public FittingPaintingsSync(Logger logger)
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
                FittingPaintings.receivedSeed = num;
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
