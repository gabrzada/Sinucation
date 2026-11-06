using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Net;
using FriendSlop.Presentation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Gera os assets data-driven (MatchConfig, ModifierDefinitions, materiais), os prefabs de rede
    /// (NetPlayer, NetGame) e a cena de jogo com NetworkManager. Corre sozinho quando a cena não existe ou
    /// é de uma versão anterior; também disponível no menu FriendSlop.
    /// </summary>
    [InitializeOnLoad]
    public static class FriendSlopSetup
    {
        const int SceneVersion = 3;
        const string Root = "Assets/_Project";
        const string DataFolder = Root + "/Data";
        const string ModifierFolder = DataFolder + "/Modifiers";
        const string MaterialFolder = Root + "/Materials";
        const string PrefabFolder = Root + "/Prefabs";
        const string SceneFolder = Root + "/Scenes";
        const string ScenePath = SceneFolder + "/Game.unity";
        const string ConfigPath = DataFolder + "/MatchConfig.asset";
        const string VersionPath = DataFolder + "/SceneVersion.txt";
        const string NetPlayerPath = PrefabFolder + "/NetPlayer.prefab";
        const string NetGamePath = PrefabFolder + "/NetGame.prefab";

        static FriendSlopSetup()
        {
            EditorApplication.delayCall += AutoSetup;
        }

        static int InstalledVersion()
        {
            if (!File.Exists(VersionPath)) return 0;
            return int.TryParse(File.ReadAllText(VersionPath).Trim(), out int v) ? v : 0;
        }

        static void AutoSetup()
        {
            if (Application.isBatchMode) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(ScenePath) && InstalledVersion() >= SceneVersion) return;

            bool gameSceneOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path != ScenePath) continue;
                gameSceneOpen = true;
                if (s.isDirty)
                {
                    Debug.LogWarning("[FriendSlop] A cena de jogo precisa de ser actualizada (v" + SceneVersion + "). Grava-a e usa FriendSlop ▸ Criar ou reconstruir cena de jogo.");
                    return;
                }
            }

            if (BuildGameScene(gameSceneOpen))
                Debug.Log("[FriendSlop] Cena de jogo v" + SceneVersion + " criada em " + ScenePath + ". Abre-a (FriendSlop ▸ Abrir cena de jogo) e carrega em Play.");
        }

        /// <summary>Para CI: Unity -batchmode -executeMethod FriendSlop.EditorTools.FriendSlopSetup.BuildSceneBatch</summary>
        public static void BuildSceneBatch() => BuildGameScene(false);

        [MenuItem("FriendSlop/Criar ou reconstruir cena de jogo", priority = 0)]
        public static void CreateGameSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildGameScene(true);
        }

        [MenuItem("FriendSlop/Abrir cena de jogo", priority = 1)]
        public static void OpenGameScene()
        {
            if (!File.Exists(ScenePath) || InstalledVersion() < SceneVersion) BuildGameScene(false);
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("FriendSlop/Gerar assets de configuração", priority = 20)]
        public static void GenerateAssetsMenu()
        {
            EnsureConfig(false);
            EnsureModifiers(false);
            EnsureMaterials(out _, out _, out _);
            EnsureExtraMaterials();
            ConfigureAudioImport();
            EnsurePrefabs(out _, out _);
            AssetDatabase.SaveAssets();
            Debug.Log("[FriendSlop] Assets gerados em " + Root);
        }

        static bool BuildGameScene(bool openAfter)
        {
            // Unity não cria cenas aditivas ao lado de uma cena "Untitled"; nesse caso substitui-a,
            // mas só se não houver trabalho por gravar.
            bool single = openAfter;
            var active = SceneManager.GetActiveScene();
            if (!single && string.IsNullOrEmpty(active.path))
            {
                if (active.isDirty)
                {
                    Debug.LogWarning("[FriendSlop] Grava a cena actual e usa o menu FriendSlop ▸ Criar ou reconstruir cena de jogo.");
                    return false;
                }
                single = true;
            }

            // v3: novos valores de economia/sabotagem e modificadores por nível — os assets antigos seriam
            // desserializados por cima dos novos defaults, por isso são refeitos.
            bool refreshData = InstalledVersion() < 3;
            var config = EnsureConfig(refreshData);
            var modifiers = EnsureModifiers(refreshData);
            EnsureMaterials(out var lit, out var unlit, out var litTransparent);
            var extra = EnsureExtraMaterials();
            ConfigureAudioImport();
            EnsurePrefabs(out var netPlayer, out var netGame);
            AssetDatabase.SaveAssets();
            EnsureFolder(SceneFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, single ? NewSceneMode.Single : NewSceneMode.Additive);

            var networkGo = new GameObject("NetworkManager");
            SceneManager.MoveGameObjectToScene(networkGo, scene);
            var network = networkGo.AddComponent<NetworkManager>();
            var transport = networkGo.AddComponent<UnityTransport>();
            network.NetworkConfig.NetworkTransport = transport;
            network.NetworkConfig.PlayerPrefab = netPlayer;
            network.NetworkConfig.ConnectionApproval = true;

            var go = new GameObject("GameBootstrap");
            SceneManager.MoveGameObjectToScene(go, scene);
            var bootstrap = go.AddComponent<GameBootstrap>();

            var so = new SerializedObject(bootstrap);
            so.FindProperty("_config").objectReferenceValue = config;
            var array = so.FindProperty("_modifiers");
            array.arraySize = modifiers.Count;
            for (int i = 0; i < modifiers.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = modifiers[i];
            so.FindProperty("_litTemplate").objectReferenceValue = lit;
            so.FindProperty("_unlitTemplate").objectReferenceValue = unlit;
            so.FindProperty("_litTransparentTemplate").objectReferenceValue = litTransparent;
            so.FindProperty("_complexLitTemplate").objectReferenceValue = extra[0];
            so.FindProperty("_particleTemplate").objectReferenceValue = extra[1];
            var keepers = so.FindProperty("_shaderVariantKeepers");
            keepers.arraySize = extra.Length;
            for (int i = 0; i < extra.Length; i++) keepers.GetArrayElementAtIndex(i).objectReferenceValue = extra[i];
            so.FindProperty("_network").objectReferenceValue = network;
            so.FindProperty("_netGamePrefab").objectReferenceValue = netGame;
            so.FindProperty("_netPlayerPrefab").objectReferenceValue = netPlayer;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            File.WriteAllText(VersionPath, SceneVersion.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            if (!single) EditorSceneManager.CloseScene(scene, true);
            return true;
        }

        static void EnsurePrefabs(out GameObject netPlayer, out GameObject netGame)
        {
            EnsureFolder(PrefabFolder);

            netPlayer = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
            if (netPlayer == null)
            {
                var go = new GameObject("NetPlayer");
                go.AddComponent<NetworkObject>();
                var nt = go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                // Medido em sessões anfitrião+cliente: o LegacyLerp é o mais suave (0–6% de frames parados) para estes
                // bonecos; "Lerp" e "SmoothDampening" davam 43–87%.
                nt.PositionInterpolationType = NetworkTransform.InterpolationTypes.LegacyLerp;
                nt.RotationInterpolationType = NetworkTransform.InterpolationTypes.LegacyLerp;
                nt.SyncRotAngleX = false;
                nt.SyncRotAngleZ = false;
                nt.SyncScaleX = false;
                nt.SyncScaleY = false;
                nt.SyncScaleZ = false;
                go.AddComponent<NetPlayer>();
                netPlayer = PrefabUtility.SaveAsPrefabAsset(go, NetPlayerPath);
                Object.DestroyImmediate(go);
                ValidateNetworkObject(netPlayer);
            }

            netGame = AssetDatabase.LoadAssetAtPath<GameObject>(NetGamePath);
            if (netGame == null)
            {
                var go = new GameObject("NetGame");
                go.AddComponent<NetworkObject>();
                go.AddComponent<NetGame>();
                netGame = PrefabUtility.SaveAsPrefabAsset(go, NetGamePath);
                Object.DestroyImmediate(go);
                ValidateNetworkObject(netGame);
            }
        }

        /// <summary>Garante o GlobalObjectIdHash do prefab (o NGO gera-o no OnValidate do editor).</summary>
        static void ValidateNetworkObject(GameObject prefab)
        {
            var networkObject = prefab.GetComponent<NetworkObject>();
            var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static MatchConfig EnsureConfig(bool refresh)
        {
            EnsureFolder(DataFolder);
            var config = AssetDatabase.LoadAssetAtPath<MatchConfig>(ConfigPath);
            if (config != null)
            {
                if (!refresh) return config;
                var fresh = ScriptableObject.CreateInstance<MatchConfig>();
                string configName = config.name;
                EditorUtility.CopySerialized(fresh, config);
                config.name = configName;
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(config);
                return config;
            }
            config = ScriptableObject.CreateInstance<MatchConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        static List<ModifierDefinition> EnsureModifiers(bool refresh)
        {
            EnsureFolder(ModifierFolder);
            var result = new List<ModifierDefinition>();
            foreach (var def in ModifierCatalog.CreateDefaults())
            {
                string path = ModifierFolder + "/" + def.Id + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<ModifierDefinition>(path);
                if (existing != null)
                {
                    if (refresh)
                    {
                        string name = existing.name;
                        EditorUtility.CopySerialized(def, existing);
                        existing.name = name;
                        EditorUtility.SetDirty(existing);
                    }
                    Object.DestroyImmediate(def);
                    result.Add(existing);
                    continue;
                }
                AssetDatabase.CreateAsset(def, path);
                result.Add(def);
            }
            return result;
        }

        static void EnsureMaterials(out Material lit, out Material unlit, out Material litTransparent)
        {
            EnsureFolder(MaterialFolder);
            lit = LoadOrCreateMaterial(MaterialFolder + "/LitTemplate.mat", "Universal Render Pipeline/Lit", false);
            unlit = LoadOrCreateMaterial(MaterialFolder + "/UnlitLineTemplate.mat", "Sprites/Default", false);
            litTransparent = LoadOrCreateMaterial(MaterialFolder + "/LitTransparentTemplate.mat", "Universal Render Pipeline/Lit", true);
        }

        /// <summary>
        /// Materiais de referência para o build: Complex Lit com verniz (bolas), partículas URP e variantes Lit com
        /// normal map / emissão. Referenciados pela cena, garantem que as variantes de shader não são removidas.
        /// </summary>
        static Material[] EnsureExtraMaterials()
        {
            EnsureFolder(MaterialFolder);
            var complex = LoadOrCreateMaterial(MaterialFolder + "/ComplexLitClearCoatTemplate.mat", "Universal Render Pipeline/Complex Lit", false);
            if (complex != null && complex.HasProperty("_ClearCoatMask"))
            {
                complex.SetFloat("_ClearCoat", 1f);
                complex.SetFloat("_ClearCoatMask", 1f);
                complex.SetFloat("_ClearCoatSmoothness", 0.97f);
                complex.EnableKeyword("_CLEARCOAT");
                EditorUtility.SetDirty(complex);
            }
            var particles = LoadOrCreateMaterial(MaterialFolder + "/ParticleTemplate.mat", "Universal Render Pipeline/Particles/Unlit", true);
            var normal = LoadOrCreateMaterial(MaterialFolder + "/LitNormalTemplate.mat", "Universal Render Pipeline/Lit", false);
            if (normal != null)
            {
                normal.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(normal);
            }
            var emissive = LoadOrCreateMaterial(MaterialFolder + "/LitEmissiveTemplate.mat", "Universal Render Pipeline/Lit", false);
            if (emissive != null)
            {
                emissive.EnableKeyword("_EMISSION");
                emissive.SetColor("_EmissionColor", Color.white);
                emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeIndirectEmission;
                EditorUtility.SetDirty(emissive);
            }
            return new[] { complex, particles, normal, emissive };
        }

        /// <summary>Música em streaming (não ocupa RAM descomprimida); efeitos curtos descomprimidos.</summary>
        static void ConfigureAudioImport()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Resources/Audio/Music" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                var settings = importer.defaultSampleSettings;
                if (settings.loadType == AudioClipLoadType.Streaming) continue;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.6f;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        static Material LoadOrCreateMaterial(string path, string shaderName, bool transparent)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find(shaderName);
            if (shader == null) return null;
            mat = new Material(shader);
            if (transparent) VisualFactory.SetTransparent(mat, true);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
