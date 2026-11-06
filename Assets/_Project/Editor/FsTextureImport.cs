using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Texturas pintadas no Blender (Blender/textures.py) importadas pelo Unity: comprimidas (BC7) e com mipmaps
    /// feitos no build, em vez de PNG descodificado em runtime (congelava o arranque ~1,8 s). As normais vêm no
    /// formato AG (X no alfa, Y no verde, como o DXT5nm): lineares, com alfa, sem o tipo "Normal Map" do Unity.
    /// </summary>
    sealed class FsTextureImport : AssetPostprocessor
    {
        public const string Folder = "/Resources/Textures/FSImported/";

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (path.Contains("/Resources/UI/"))
            {
                // Emblema e arte de UI: sRGB com transparência, mipmaps (desenha-se em vários tamanhos), sem repetir.
                var ui = (TextureImporter)assetImporter;
                ui.textureType = TextureImporterType.Default;
                ui.sRGBTexture = true;
                ui.alphaIsTransparency = true;
                ui.mipmapEnabled = true;
                ui.wrapMode = TextureWrapMode.Clamp;
                ui.filterMode = FilterMode.Trilinear;
                ui.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (path.Contains("/Resources/Textures/Balls/"))
            {
                // Bolas: cor sRGB, mipmaps trilineares e anisotrópico (números legíveis de lado e ao longe).
                var ball = (TextureImporter)assetImporter;
                ball.textureType = TextureImporterType.Default;
                ball.sRGBTexture = true;
                ball.mipmapEnabled = true;
                ball.wrapModeU = TextureWrapMode.Repeat;
                ball.wrapModeV = TextureWrapMode.Clamp;
                ball.filterMode = FilterMode.Trilinear;
                ball.anisoLevel = 8;
                ball.ignoreMipmapLimit = true; // Number markings stay readable on the low texture preset.
                ball.maxTextureSize = 1024;
                ball.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (!path.Contains(Folder)) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_normal.png");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = false;
        }
    }
}
