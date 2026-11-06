using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Revólver de brinquedo low-poly (estilo "toy blaster" do character sheet): corpo azul-petróleo, peças
    /// mostarda, tambor de 6 câmaras, cão e gatilho articulados e bola vermelha na ponta do cano.
    /// Frente = +Z, origem = punho.
    /// </summary>
    public sealed class RevolverModel
    {
        public Transform Root;
        public Transform Cylinder;
        public Transform Hammer;
        public Transform Trigger;
        public Transform Muzzle;
        /// <summary>Cápsulas de gosma nos tambores carregados (activa-se uma por bala da ronda).</summary>
        public GameObject[] Bullets = new GameObject[6];

        public static RevolverModel Build(Transform parent)
        {
            var m = new RevolverModel();
            var body = VisualFactory.Lit(ArtPalette.Navy, 0.55f, 0.3f);
            var trim = VisualFactory.Lit(ArtPalette.Mustard, 0.7f, 0.55f);
            var steel = VisualFactory.Lit(new Color(0.55f, 0.58f, 0.62f), 0.85f, 0.9f);
            var grip = VisualFactory.Lit(ArtPalette.Leather, 0.35f);
            var red = VisualFactory.Lit(ArtPalette.Red, 0.8f);
            var dark = VisualFactory.Lit(new Color(0.03f, 0.03f, 0.04f), 0.6f);
            var box = LowPoly.BevelBox(0.22f);
            var gooMat = VisualFactory.Emissive(ArtPalette.Goo, 1.4f);

            m.Root = LowPoly.Pivot("Revolver", parent, Vector3.zero);
            var r = m.Root;

            // Modelo do Blender (Blender/revolver.py), com as mesmas articulações.
            var frameModel = FsModels.Get("revolver", "RevolverFrame");
            if (frameModel != null)
            {
                var cache = new System.Collections.Generic.Dictionary<string, Material>();
                Material Resolve(string n)
                {
                    if (cache.TryGetValue(n, out var mat)) return mat;
                    switch (n)
                    {
                        case "navy": mat = body; break;
                        case "mustard": mat = trim; break;
                        case "steel": mat = steel; break;
                        case "wood": mat = grip; break;
                        case "red": mat = red; break;
                        default: mat = dark; break;
                    }
                    cache[n] = mat;
                    return mat;
                }
                FsModels.Spawn(frameModel, r, Vector3.zero, Quaternion.identity, Vector3.one, Resolve);
                m.Cylinder = LowPoly.Pivot("CylinderPivot", r, new Vector3(0f, 0.04f, 0.075f));
                FsModels.Spawn(FsModels.Get("revolver", "RevolverDrum"), m.Cylinder, Vector3.zero, Quaternion.identity, Vector3.one, Resolve);
                m.Hammer = LowPoly.Pivot("HammerPivot", r, new Vector3(0f, 0.055f, -0.015f));
                FsModels.Spawn(FsModels.Get("revolver", "RevolverHammer"), m.Hammer, Vector3.zero, Quaternion.identity, Vector3.one, Resolve);
                m.Trigger = LowPoly.Pivot("TriggerPivot", r, new Vector3(0f, 0.012f, 0.045f));
                FsModels.Spawn(FsModels.Get("revolver", "RevolverTrigger"), m.Trigger, Vector3.zero, Quaternion.identity, Vector3.one, Resolve);
                m.Muzzle = LowPoly.Pivot("Muzzle", r, new Vector3(0f, 0.052f, 0.255f));
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    m.Bullets[i] = LowPoly.Part(LowPoly.Icosphere(1), "GooRound", m.Cylinder,
                        new Vector3(Mathf.Cos(a) * 0.019f, Mathf.Sin(a) * 0.019f, 0.025f), new Vector3(0.013f, 0.013f, 0.008f), gooMat);
                }
                return m;
            }

            // Punho inclinado com placas e parafusos.
            var gripPivot = LowPoly.Pivot("Grip", r, Vector3.zero);
            gripPivot.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            LowPoly.Part(box, "GripWood", gripPivot, new Vector3(0f, -0.045f, -0.005f), new Vector3(0.042f, 0.11f, 0.055f), grip);
            LowPoly.Part(box, "GripCap", gripPivot, new Vector3(0f, -0.105f, -0.005f), new Vector3(0.046f, 0.018f, 0.06f), trim);
            foreach (float side in new[] { -0.023f, 0.023f })
                LowPoly.Part(LowPoly.Icosphere(0), "Screw", gripPivot, new Vector3(side, -0.04f, 0f), Vector3.one * 0.01f, trim);

            // Armação, guarda-mato e gatilho.
            LowPoly.Part(box, "Frame", r, new Vector3(0f, 0.035f, 0.035f), new Vector3(0.036f, 0.05f, 0.11f), body);
            LowPoly.Part(box, "TopStrap", r, new Vector3(0f, 0.066f, 0.05f), new Vector3(0.03f, 0.014f, 0.1f), trim);
            var guard = LowPoly.Part(LowPoly.Ring(8, 0.022f, 0.006f, 0.012f, 200f), "TriggerGuard", r, new Vector3(0f, 0.004f, 0.045f), Vector3.one, trim);
            guard.transform.localRotation = Quaternion.Euler(0f, 90f, 190f);
            m.Trigger = LowPoly.Pivot("TriggerPivot", r, new Vector3(0f, 0.012f, 0.045f));
            LowPoly.Part(box, "Trigger", m.Trigger, new Vector3(0f, -0.012f, -0.004f), new Vector3(0.008f, 0.026f, 0.01f), steel);

            // Tambor de 6 câmaras.
            m.Cylinder = LowPoly.Pivot("CylinderPivot", r, new Vector3(0f, 0.04f, 0.075f));
            var drum = LowPoly.Part(LowPoly.Limb(6, 0.03f, 0.03f, 0.045f), "Drum", m.Cylinder, new Vector3(0f, 0f, 0.0225f), Vector3.one, trim);
            drum.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var hole = LowPoly.Part(LowPoly.Limb(6, 0.008f, 0.008f, 0.004f), "Chamber", m.Cylinder,
                    new Vector3(Mathf.Cos(a) * 0.019f, Mathf.Sin(a) * 0.019f, 0.0235f), Vector3.one, dark);
                hole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var bullet = LowPoly.Part(LowPoly.Icosphere(1), "GooRound", m.Cylinder,
                    new Vector3(Mathf.Cos(a) * 0.019f, Mathf.Sin(a) * 0.019f, 0.026f), new Vector3(0.012f, 0.012f, 0.008f), gooMat);
                m.Bullets[i] = bullet;
            }

            // Cano octogonal com mira e bola vermelha de brinquedo na ponta.
            var barrel = LowPoly.Part(LowPoly.Limb(8, 0.014f, 0.014f, 0.13f), "Barrel", r, new Vector3(0f, 0.052f, 0.1f), Vector3.one, body);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            LowPoly.Part(box, "BarrelRib", r, new Vector3(0f, 0.068f, 0.16f), new Vector3(0.01f, 0.01f, 0.12f), trim);
            LowPoly.Part(box, "FrontSight", r, new Vector3(0f, 0.075f, 0.225f), new Vector3(0.006f, 0.014f, 0.01f), trim);
            m.Muzzle = LowPoly.Pivot("Muzzle", r, new Vector3(0f, 0.052f, 0.245f));
            LowPoly.Part(LowPoly.Icosphere(1), "MuzzleBall", m.Muzzle, Vector3.zero, Vector3.one * 0.036f, red);

            // Cão.
            m.Hammer = LowPoly.Pivot("HammerPivot", r, new Vector3(0f, 0.055f, -0.015f));
            LowPoly.Part(box, "Hammer", m.Hammer, new Vector3(0f, 0.012f, -0.008f), new Vector3(0.012f, 0.028f, 0.014f), steel);
            return m;
        }
    }
}
