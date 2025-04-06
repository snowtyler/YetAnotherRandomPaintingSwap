using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;

namespace AnotherRandomPaintingSwap;

[BepInPlugin("phnod.randompaintingswap", "Another Random Painting Swap", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private const string IMAGE_FOLDER_NAME = "RandomPaintingSwap_Images";

    private const string IMAGE_LANDSCAPE_FOLDER_NAME = "RandomLandscapePaintingSwap_Images";
    private const string IMAGE_PORTRAIT_FOLDER_NAME  = "RandomPortraitPaintingSwap_Images";

    public static readonly HashSet<string> targetLandscapeMaterials = new HashSet<string>
    {
        "Painting_H_Landscape"
    };
    public static readonly HashSet<string> targetPortraitMaterials = new HashSet<string>
    {
        "Painting_V_Furman",
        "painting teacher01",
        "painting teacher02",
        "painting teacher03",
        "painting teacher04",
        "Painting_S_Tree"
    };

    // Defines groups of paintings depending on their dimensions, allowing to split landscape and portraits
    public class PaintingGroup
    {
        //public PaintingType paintingType;
        public string paintingType;
        public string paintingFolderName;
        public HashSet<string> targetMaterials;
        public List<Material> loadedMaterials;

        public PaintingGroup(string InPaintingType, string InPaintingFolderName, HashSet<string> InTargetMaterials)
        {
            paintingType       = InPaintingType;
            paintingFolderName = InPaintingFolderName;
            targetMaterials    = InTargetMaterials;
            loadedMaterials = new List<Material>();
        }
    }

    public static List<PaintingGroup> paintingGroups;

    public static Plugin Instance { get; private set; }
    internal static new ManualLogSource Logger;

    static Plugin()
    {
        paintingGroups = new List<PaintingGroup>();
        paintingGroups.Add(new PaintingGroup("Landscape", IMAGE_LANDSCAPE_FOLDER_NAME, targetLandscapeMaterials));
        paintingGroups.Add(new PaintingGroup("Portrait" , IMAGE_PORTRAIT_FOLDER_NAME , targetPortraitMaterials ));
    }

    // File extensions
    public static readonly HashSet<string> imagePatterns = new HashSet<string>
    {
        "*.png",
        "*.jpg",
        "*.jpeg"
    };

    private readonly Harmony harmony = new Harmony("phnod.anotherrandompaintingswap");

    /**
     * Init Plugin
     */
    private void Awake()
    {
        Instance = this;

        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin Another Random Painting Swap is loaded!");

        harmony.PatchAll(Assembly.GetExecutingAssembly());

        LoadImagesFromAllPlugins();
    }

    private void LoadImagesFromAllPlugins()
    {
        var pluginDir = Path.Combine(Paths.PluginPath);
        if (!Directory.Exists(pluginDir))
        {
            Logger.LogWarning($"Plugins directory not found: [{pluginDir}]");
            return;
        }

        foreach (var paintingGroup in paintingGroups)
        {
            var folderName = paintingGroup.paintingFolderName;
            string[] directories = Directory.GetDirectories(pluginDir, folderName, SearchOption.AllDirectories);
            if (directories.Length == 0)
            {
                Logger.LogWarning($"No 'CustomPaintings' folders found in plugins.");
                return;
            }
            string[] array = directories;
            foreach (string dirStr in array)
            {
                Logger.LogInfo($"Loading images from: [{dirStr}]");
                LoadImagesFromDirectory(paintingGroup, dirStr);
            }
        }
    }


    private void LoadImagesFromDirectory(PaintingGroup InPaintingGroup, string directoryPath)
    {
        string paintingType = InPaintingGroup.paintingType;

        if (!Directory.Exists(directoryPath))
        {
            Logger.LogWarning($"The folder [{directoryPath}] does not exist!");
            return;
        }

        Logger.LogInfo($"Selecting image patterns for group [{paintingType}] for files : {directoryPath}");
        List<string> imageFiles = imagePatterns.SelectMany(pattern => Directory.GetFiles(directoryPath, pattern)).ToList();

        if (!imageFiles.Any())
        {
            Logger.LogWarning($"No images found in the folder [{directoryPath}]");
            return;
        }

        foreach (var imageFile in imageFiles)
        {
            string filename = Path.GetFileName(imageFile);
            Texture2D texture = LoadTextureFromFile(imageFile);

            if (texture == null)
            {
                Logger.LogWarning($"Error loading image : [{imageFile}]");
                continue;
            }

            Material material = new Material(Shader.Find("Standard")) { mainTexture = texture };
            InPaintingGroup.loadedMaterials.Add(material);

            Logger.LogInfo($"Created Material for group [{paintingType}] for loaded image : {filename}");
        }

        Logger.LogInfo($"Total Images for group [{paintingType}] : [{imageFiles.Count}]");
    }

    private Texture2D LoadTextureFromFile(string filePath)
    {
        byte[] fileData = File.ReadAllBytes(filePath);
        Texture2D texture = new Texture2D(2, 2);

        if (!texture.LoadImage(fileData))
        {
            texture = null; // clear texture
            return null;
        }

        texture.Apply();
        return texture;
    }

    [HarmonyPatch(typeof(LoadingUI), "LevelAnimationComplete")]
    public class PatchLoadingUI
    {
        [HarmonyPostfix]
        /**
         * Replacing base images with plugin images
         */
        private static void Postfix()
        {
            Logger.LogInfo("Replacing base images with plugin images");

            var activeScene = SceneManager.GetActiveScene();
            // All game objects
            var gameObjectList = activeScene.GetRootGameObjects().ToList();

            foreach (var gameObject in gameObjectList)
            {
                foreach (var meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>())
                {
                    var sharedMaterials = meshRenderer.sharedMaterials;

                    if (sharedMaterials == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < sharedMaterials.Length; i++)
                    {
                        foreach (var paintingGroup in paintingGroups)
                        {
                            var material = sharedMaterials[i];
                            if (material != null && paintingGroup.targetMaterials.Contains(material.name) && paintingGroup.loadedMaterials.Count > 0)
                            {
                                Logger.LogInfo($"---------------------------> {material.name}");

                                var randomPaintingIndex = UnityEngine.Random.Range(0, paintingGroup.loadedMaterials.Count);
                                sharedMaterials[i] = paintingGroup.loadedMaterials[randomPaintingIndex];
                                Logger.LogInfo($"---------------------------> {paintingGroup.loadedMaterials[randomPaintingIndex]}");
                            }
                        }

                        meshRenderer.sharedMaterials = sharedMaterials;
                    }
                }
            }
        }
    }
}