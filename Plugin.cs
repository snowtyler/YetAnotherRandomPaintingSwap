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

namespace RandomPaintingSwap
{
    [BepInPlugin("ch.gabzdev.randompaintingswap", "Random Painting Swap", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string IMAGE_FOLDER_NAME = "RandomPaintingSwap_Images";

        public static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger;
        // Liste des material chargees
        public static List<Material> loadedMaterials = new List<Material>();
        // Materiaux cibles
        public static readonly HashSet<string> targetMaterials = new HashSet<string>
        {
            "Painting_H_Landscape",
            "Painting_V_Furman",
            "painting teacher02",
            "Painting_S_Tree"
        };
        // Paterne fichier image
        public static readonly HashSet<string> imagePatterns = new HashSet<string>
        {
            "*.png",
            "*.jpg",
            "*.jpeg"
        };

        private readonly Harmony harmony = new Harmony("ch.gabzdev.randompaintingswap");
        private string imagesDirectoryPath;

        /**
         * Init Plugin
         */
        private void Awake()
        {
            Instance = this;

            // Plugin startup logic
            Logger = base.Logger;
            Logger.LogInfo($"Plugin Random Painting Swap is loaded!");

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            CreateImagesDirectory();
            LoadImagesFromDirectory();
        }

        /**
         * Cree un dossier "IMAGE_FOLDER_NAME" si n'existe pas
         */
        private void CreateImagesDirectory()
        {
            string pluginDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            imagesDirectoryPath = Path.Combine(pluginDirectory, IMAGE_FOLDER_NAME);

            if (!Directory.Exists(imagesDirectoryPath))
            {
                Directory.CreateDirectory(imagesDirectoryPath);
                Logger.LogInfo($"Dossier {imagesDirectoryPath} creer avec succes !");
                return;
            }

            Logger.LogInfo($"Dossier {imagesDirectoryPath} detecte !");
        }

        /**
         * Charge les images du dossier "IMAGE_FOLDER_NAME"
         */
        private void LoadImagesFromDirectory()
        {
            if (!Directory.Exists(imagesDirectoryPath))
            {
                Logger.LogWarning($"Le dossier {imagesDirectoryPath} n'existe pas !");
                return;
            }

            List<string> imageFiles = imagePatterns.SelectMany(pattern => Directory.GetFiles(imagesDirectoryPath, pattern)).ToList();

            if (!imageFiles.Any())
            {
                Logger.LogWarning($"Aucune image trouvee dans le dossier {imagesDirectoryPath}");
                return;
            }

            foreach (var imageFile in imageFiles)
            {
                Texture2D texture = LoadTextureFromFile(imageFile);

                if (texture == null)
                {
                    Logger.LogWarning($"Erreur chargement image : {imageFile}");
                    continue;
                }

                // creer le Material avec la texture
                Material material = new Material(Shader.Find("Standard")) { mainTexture = texture };
                loadedMaterials.Add(material); // ajout dans la liste

                Logger.LogInfo($"Image chargée et Material créé : {Path.GetFileNameWithoutExtension(imageFile)}");
            }

            Logger.LogInfo($"Total Images : {imageFiles.Count}");
        }

        /**
         * Charge une texture d'un fichier png en memory
         */
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
             * Remplacement des images de base par les images du plugin
             */
            private static void Postfix()
            {
                Logger.LogInfo("Remplacement des images de base par les images du plugin");

                Scene activeScene = SceneManager.GetActiveScene();
                // Liste de tous les objects de la scene
                List<GameObject> list = activeScene.GetRootGameObjects().ToList();

                // Parcours de tous les objects de la scene
                foreach (GameObject gameObject in list)
                {
                    // Parcours de tous les MeshRenderer de l'object
                    foreach (MeshRenderer mesh in gameObject.GetComponentsInChildren<MeshRenderer>())
                    {
                        // stocker les materiaux partager du meshrenderer
                        Material[] sharedMaterials = mesh.sharedMaterials;

                        if (sharedMaterials == null)
                        {
                            continue;
                        }

                        // Parcours de tous les materiaux partager du meshrenderer
                        for (int i = 0; i < sharedMaterials.Length; i++)
                        {
                            Material material = sharedMaterials[i];
                            if (material != null && targetMaterials.Contains(material.name) && loadedMaterials.Count > 0)
                            {
                                //Logger.LogInfo($"---------------------------> {material.name}");

                                sharedMaterials[i] = loadedMaterials[UnityEngine.Random.Range(0, loadedMaterials.Count)];
                            }
                        }

                        // Appliquer les materiaux custom
                        mesh.sharedMaterials = sharedMaterials;
                    }
                }
            }
        }
    }
}