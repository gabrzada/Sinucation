using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Adereços 3D CC0 (Kenney) em Resources/Models. Os packs têm escalas e pivôs diferentes: cada instância é
    /// normalizada para um tamanho real (altura em metros), pousada no chão pelo centro da base e recebe um
    /// BoxCollider à medida — é isto que dá colisões correctas a todo o cenário.
    /// </summary>
    public static class PropLibrary
    {
        static readonly Dictionary<string, GameObject> s_cache = new Dictionary<string, GameObject>();

        public static GameObject Load(string path)
        {
            if (s_cache.TryGetValue(path, out var go)) return go;
            go = Resources.Load<GameObject>("Models/" + path);
            if (go == null) Debug.LogWarning("[FriendSlop] Modelo em falta: Models/" + path);
            s_cache[path] = go;
            return go;
        }

        /// <summary>
        /// Cria o adereço. <paramref name="height"/> &gt; 0 escala para essa altura; senão usa <paramref name="scale"/>.
        /// Devolve o "holder" (pivô na base, centro), ou null se o modelo não existir.
        /// </summary>
        public static Transform Spawn(string path, Transform parent, Vector3 position, float yaw, float height = 0f, float scale = 1f,
            bool collider = true, bool shadows = true, float colliderInset = 0f)
        {
            var refined = ArenaProps.TrySpawn(path, parent, position, yaw, height, scale, collider, shadows, colliderInset);
            if (refined != null) return refined;
            var prefab = Load(path);
            if (prefab == null) return null;

            var holder = new GameObject(prefab.name).transform;
            var model = Object.Instantiate(prefab, holder, false).transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;

            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in renderers)
            {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = VisualFactory.SceneMaterial(mats[i]);
                r.sharedMaterials = mats;
            }
            if (!any) bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);

            float s = height > 0f && bounds.size.y > 0.0001f ? height / bounds.size.y : scale;
            model.localScale = Vector3.one * s;
            model.localPosition = new Vector3(-bounds.center.x * s, -bounds.min.y * s, -bounds.center.z * s);

            if (collider)
            {
                var box = holder.gameObject.AddComponent<BoxCollider>();
                var size = bounds.size * s;
                box.size = new Vector3(Mathf.Max(0.02f, size.x - colliderInset), size.y, Mathf.Max(0.02f, size.z - colliderInset));
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
            }

            holder.SetParent(parent, false);
            holder.localPosition = position;
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return holder;
        }

        /// <summary>Tamanho (m) do adereço já instanciado, pelo colisor ou pelos renderers.</summary>
        public static Vector3 SizeOf(Transform holder)
        {
            if (holder == null) return Vector3.zero;
            var box = holder.GetComponent<BoxCollider>();
            if (box != null) return box.size;
            var b = new Bounds(holder.position, Vector3.zero);
            foreach (var r in holder.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            return b.size;
        }
    }
}
