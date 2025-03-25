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
            }
            else
            {
                Logger.LogInfo($"Dossier {imagesDirectoryPath} detecte !");
            }
        }

        /**
         * Charge les images du dossier "IMAGE_FOLDER_NAME"
         */
        private void LoadImagesFromDirectory()
        {
            if (Directory.Exists(imagesDirectoryPath))
            {
                List<string> imageFiles = new List<string>();

                // Recuperer tout les fichiers avec les patterns
                foreach (var pattern in imagePatterns)
                {
                    imageFiles.AddRange(Directory.GetFiles(imagesDirectoryPath, pattern));
                }

                if (imageFiles.Any())
                {
                    foreach (var imageFile in imageFiles)
                    {
                        Texture2D texture = LoadTextureFromFile(imageFile);

                        if (texture != null)
                        {
                            // creer le Material avec la texture
                            Material material = new Material(Shader.Find("Standard"));
                            material.mainTexture = texture;

                            loadedMaterials.Add(material); // ajout dans la liste

                            string fileName = Path.GetFileNameWithoutExtension(imageFile);
                            Logger.LogInfo($"Image chargée et Material créé : {fileName}");
                        }
                        else
                        {
                            Logger.LogWarning($"Erreur chargement image : {imageFile}");
                        }
                    }

                    Logger.LogInfo($"Total Images : {imageFiles.Count}");
                }
                else
                {
                    Logger.LogWarning($"Aucune image trouvee dans le dossier {imagesDirectoryPath}");
                }
            }
            else
            {
                Logger.LogWarning($"Le dossier {imagesDirectoryPath} n'existe pas !");
            }
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
                    // stocker les MeshRenderer de l'object
                    MeshRenderer[] componentsInChildren = gameObject.GetComponentsInChildren<MeshRenderer>();

                    // Parcours de tous les MeshRenderer de l'object
                    foreach (MeshRenderer mesh in componentsInChildren)
                    {
                        // stocker les materiaux partager du meshrenderer
                        Material[] sharedMaterials = mesh.sharedMaterials;

                        if (sharedMaterials != null)
                        {
                            // Parcours de tous les materiaux partager du meshrenderer
                            for (int i = 0; i < sharedMaterials.Length; i++)
                            {
                                if (sharedMaterials[i] == null || !targetMaterials.Contains(sharedMaterials[i].name)) continue;

                                //Logger.LogInfo($"---------------------------> {material.name}");
                                if (loadedMaterials.Count > 0)
                                {
                                    // Appliquer un materiaux custom aleatoire
                                    int randomIndex = UnityEngine.Random.Range(0, loadedMaterials.Count);
                                    sharedMaterials[i] = loadedMaterials[randomIndex];
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
}