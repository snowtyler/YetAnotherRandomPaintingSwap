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

namespace AnotherRandomPaintingSwap
{
    [BepInPlugin("phnod.randompaintingswap", "Another Random Painting Swap", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string IMAGE_FOLDER_NAME = "RandomPaintingSwap_Images";

        public static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger;

        public static List<Material> loadedMaterials = new List<Material>();
        public static readonly HashSet<string> targetMaterials = new HashSet<string>
        {
            "Painting_H_Landscape",
            "Painting_V_Furman",
            "painting teacher01",
            "painting teacher02",
            "painting teacher03",
            "painting teacher04",
            "Painting_S_Tree"
        };
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
        // File extensions
        public static readonly HashSet<string> imagePatterns = new HashSet<string>
        {
            "*.png",
            "*.jpg",
            "*.jpeg"
        };

        private readonly Harmony harmony = new Harmony("phnod.anotherrandompaintingswap");
        private string imagesDirectoryPath;

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

            CreateImagesDirectory();
            LoadImagesFromDirectory();
        }

        private void CreateImagesDirectory()
        {
            string pluginDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            imagesDirectoryPath = Path.Combine(pluginDirectory, IMAGE_FOLDER_NAME);

            if (!Directory.Exists(imagesDirectoryPath))
            {
                Directory.CreateDirectory(imagesDirectoryPath);
                Logger.LogInfo($"Folder [{imagesDirectoryPath}] created successfully!");
                return;
            }

            Logger.LogInfo($"Folder [{imagesDirectoryPath}] detected!");
        }

        private void LoadImagesFromDirectory()
        {
            if (!Directory.Exists(imagesDirectoryPath))
            {
                Logger.LogWarning($"The folder [{imagesDirectoryPath}] does not exist!");
                return;
            }

            List<string> imageFiles = imagePatterns.SelectMany(pattern => Directory.GetFiles(imagesDirectoryPath, pattern)).ToList();

            if (!imageFiles.Any())
            {
                Logger.LogWarning($"No images found in the folder [{imagesDirectoryPath}]");
                return;
            }

            foreach (var imageFile in imageFiles)
            {
                Texture2D texture = LoadTextureFromFile(imageFile);

                if (texture == null)
                {
                    Logger.LogWarning($"Error loading image : [{imageFile}]");
                    continue;
                }

                Material material = new Material(Shader.Find("Standard")) { mainTexture = texture };
                loadedMaterials.Add(material);

                string filename = Path.GetFileName(imageFile);
                Logger.LogInfo($"Created Material for loaded image : {filename}");
            }

            Logger.LogInfo($"Total Images : [{imageFiles.Count}]");
        }

        private Texture2D LoadTextureFromFile(string filePath)
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);

            if (texture.LoadImage(fileData))
            {
                texture.Apply();
                return texture;
            }

            texture = null; // clear texture
            return null;
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
                            var material = sharedMaterials[i];
                            if (material != null && targetMaterials.Contains(material.name) && loadedMaterials.Count > 0)
                            {
                                //Logger.LogInfo($"---------------------------> {material.name}");

                                sharedMaterials[i] = loadedMaterials[UnityEngine.Random.Range(0, loadedMaterials.Count)];
                            }
                        }

                        meshRenderer.sharedMaterials = sharedMaterials;
                    }
                }
            }
        }
    }
}