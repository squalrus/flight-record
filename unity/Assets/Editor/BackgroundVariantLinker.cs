using UnityEditor;
using UnityEngine;

namespace FlightRecord.Editor
{
    public static class BackgroundVariantLinker
    {
        private static readonly (string prefabPath, string spriteFolder)[] _targets =
        {
            ("Assets/Prefabs/World/BG_Deep.prefab",    "Assets/Art/Backgrounds/Deep"),
            ("Assets/Prefabs/World/BG_Mid.prefab",     "Assets/Art/Backgrounds/Mid"),
            ("Assets/Prefabs/World/BG_Fore.prefab",    "Assets/Art/Backgrounds/Fore"),
            ("Assets/Prefabs/World/GalaxyDecal.prefab","Assets/Art/Backgrounds/Galaxy"),
        };

        [MenuItem("Flight Record/Refresh Background Variants")]
        static void RefreshVariants()
        {
            int total = 0;

            foreach (var (prefabPath, spriteFolder) in _targets)
            {
                var guids = AssetDatabase.FindAssets("t:Sprite", new[] { spriteFolder });
                if (guids.Length == 0)
                {
                    Debug.LogWarning($"[BackgroundVariantLinker] No sprites found in {spriteFolder}");
                    continue;
                }

                var sprites = new Sprite[guids.Length];
                for (int i = 0; i < guids.Length; i++)
                    sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guids[i]));

                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                var component = root.GetComponent<MonoBehaviour>();
                if (component == null)
                {
                    Debug.LogWarning($"[BackgroundVariantLinker] No MonoBehaviour on root of {prefabPath}");
                    PrefabUtility.UnloadPrefabContents(root);
                    continue;
                }

                var so = new SerializedObject(component);
                var variantsProp = so.FindProperty("_variants");
                variantsProp.arraySize = sprites.Length;
                for (int i = 0; i < sprites.Length; i++)
                    variantsProp.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                PrefabUtility.UnloadPrefabContents(root);

                Debug.Log($"[BackgroundVariantLinker] {prefabPath} — {sprites.Length} variants linked");
                total += sprites.Length;
            }

            Debug.Log($"[BackgroundVariantLinker] Done — {total} sprites assigned across {_targets.Length} prefabs");
        }
    }
}
