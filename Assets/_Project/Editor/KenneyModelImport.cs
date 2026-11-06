using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Definições de importação dos modelos CC0 do Kenney em Resources/Models: personagens com rig genérico e
    /// animações; adereços estáticos sem animação; paletas de cor com filtro "point" (sem sangrar cores).
    /// </summary>
    public sealed class KenneyModelImport : AssetPostprocessor
    {
        const string Root = "Assets/_Project/Resources/Models/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (ModelImporter)assetImporter;
            bool character = assetPath.StartsWith(Root + "Characters/");
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            if (character)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (TextureImporter)assetImporter;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
        }
    }
}
