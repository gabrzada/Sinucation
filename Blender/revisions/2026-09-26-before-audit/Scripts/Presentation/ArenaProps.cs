using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Presentation
{
    /// <summary>Shared Blender meshes for the arena's recurring industrial props.</summary>
    public static class ArenaProps
    {
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        static Material Resolve(string name)
        {
            if (Materials.TryGetValue(name, out var material) && material != null) return material;
            switch (name)
            {
                case "navy": material = VisualFactory.Lit(ArtPalette.Navy, .38f, .3f); break;
                case "mustard": material = VisualFactory.Lit(ArtPalette.Mustard, .4f, .2f); break;
                case "coral": material = VisualFactory.Lit(ArtPalette.Coral, .32f); break;
                case "teal": material = VisualFactory.Lit(ArtPalette.Teal, .38f); break;
                case "brass": material = VisualFactory.Lit(ArtPalette.Mustard, .6f, .65f); break;
                case "steel": material = VisualFactory.Lit(ArtPalette.IvoryShade, .55f, .7f); break;
                case "wood": material = VisualFactory.Textured(TextureFactory.Wood(), Vector2.one * 2f, .22f, 0f, .25f, new Color(.70f,.43f,.22f)); break;
                case "leather": material = VisualFactory.Lit(ArtPalette.Leather, .25f); break;
                case "glow": material = VisualFactory.Emissive(new Color(1f,.90f,.70f), 3.5f); break;
                default: material = VisualFactory.Lit(ArtPalette.NavyDark, .22f); break;
            }
            Materials[name] = material;
            return material;
        }

        public static Transform TrySpawn(string path, Transform parent, Vector3 position, float yaw, float height,
            float scale, bool collider, bool shadows, float colliderInset)
        {
            string name;
            switch (path)
            {
                case "Survival/barrel": name = "Barrel"; break;
                case "Factory/box-large":
                case "Factory/box-small":
                case "Survival/box-large": name = "Crate"; break;
                case "Furniture/stoolBar":
                case "Furniture/stoolBarSquare": name = "Stool"; break;
                default: return null;
            }
            var model = FsModels.Get("arena", name);
            if (model == null) return null;
            var bounds = model.Mesh.bounds;
            float s = height > 0 && bounds.size.y > .0001f ? height / bounds.size.y : scale;
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            holder.localPosition = position;
            holder.localRotation = Quaternion.Euler(0,yaw,0);
            var offset = new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z) * s;
            var part = FsModels.Spawn(model, holder, offset, Quaternion.identity, Vector3.one * s, Resolve);
            part.GetComponent<Renderer>().shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider)
            {
                var box = holder.gameObject.AddComponent<BoxCollider>();
                var size = bounds.size * s;
                box.size = new Vector3(Mathf.Max(.02f,size.x-colliderInset),size.y,Mathf.Max(.02f,size.z-colliderInset));
                box.center = new Vector3(0,size.y*.5f,0);
            }
            return holder;
        }

        public static bool Pendant(Transform parent, Vector3 position, float diameter)
        {
            var model = FsModels.Get("arena", "Pendant");
            if (model == null) return false;
            var part = FsModels.Spawn(model,parent,position,Quaternion.identity,Vector3.one * diameter,Resolve);
            // The existing spot light sits inside this housing; do not self-occlude it.
            part.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return true;
        }
    }
}
