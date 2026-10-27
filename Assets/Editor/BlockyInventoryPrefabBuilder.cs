using BlockyInventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlockyInventory.EditorTools
{
    public static class BlockyInventoryPrefabBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/BlockyInventory.prefab";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string InstanceName = "Blocky Inventory";

        [InitializeOnLoadMethod]
        private static void ScheduleInventorySetup()
        {
            EditorApplication.delayCall += EnsurePrefabAndSceneInstance;
        }

        private static void EnsurePrefabAndSceneInstance()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                prefab = SavePrefab();
            }
            else
            {
                prefab = RefreshPrefabDefaults();
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != SampleScenePath)
                return;

            GameObject existingInstance = GameObject.Find(InstanceName);
            if (existingInstance == null)
            {
                AddPrefabToScene(prefab, activeScene);
                return;
            }

            BlockyInventoryUI inventory = existingInstance.GetComponent<BlockyInventoryUI>();
            if (inventory == null)
                inventory = existingInstance.AddComponent<BlockyInventoryUI>();
            ConfigureHudDefaults(inventory);
            EditorUtility.SetDirty(inventory);
            PrefabUtility.RecordPrefabInstancePropertyModifications(inventory);
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
            }
        }

        /// <summary>Creates the draggable prefab and adds an instance to the sample scene.</summary>
        [MenuItem("Tools/Blocky Inventory/Create Prefab and Add to Sample Scene")]
        public static void CreatePrefabAndAddToSampleScene()
        {
            GameObject prefab = SavePrefab();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SampleScenePath)
                scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);

            GameObject existingInstance = GameObject.Find(InstanceName);
            if (existingInstance != null && PrefabUtility.IsPartOfPrefabInstance(existingInstance))
                Object.DestroyImmediate(existingInstance);

            AddPrefabToScene(prefab, scene);
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("Created " + PrefabPath + " and added it to SampleScene. Press Play and select the Game tab to view the HUD.");
        }

        private static GameObject SavePrefab()
        {
            EnsureFolder("Assets/Prefabs");
            GameObject prefabContents = new GameObject(InstanceName);
            BlockyInventoryUI inventory = prefabContents.AddComponent<BlockyInventoryUI>();
            ConfigureHudDefaults(inventory);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(prefabContents, PrefabPath);
            Object.DestroyImmediate(prefabContents);
            if (prefab == null)
                throw new System.InvalidOperationException("Failed to create " + PrefabPath);

            AssetDatabase.SaveAssets();
            Debug.Log("Created draggable inventory HUD prefab at " + PrefabPath + ".");
            return prefab;
        }

        private static GameObject RefreshPrefabDefaults()
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                BlockyInventoryUI inventory = prefabContents.GetComponent<BlockyInventoryUI>();
                if (inventory == null)
                    inventory = prefabContents.AddComponent<BlockyInventoryUI>();
                ConfigureHudDefaults(inventory);
                return PrefabUtility.SaveAsPrefabAsset(prefabContents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        private static void ConfigureHudDefaults(BlockyInventoryUI inventory)
        {
            inventory.panelWidth = 580f;
            inventory.panelHeight = 112f;
            inventory.screenPadding = new Vector2(0f, 16f);
            inventory.canvasSortingOrder = 40;
            inventory.createEventSystemWhenMissing = true;
        }

        private static void AddPrefabToScene(GameObject prefab, Scene scene)
        {
            if (prefab == null)
                throw new System.InvalidOperationException("The inventory prefab could not be loaded.");

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = InstanceName;
            instance.transform.position = Vector3.zero;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
