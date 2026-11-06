using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class ArtStyleTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void VisualStyleSurvivesSaveAndQualityPreset(int style)
        {
            var options = GraphicsOptions.FromJson(new GraphicsOptions { CelShading = style }.ToJson());
            Assert.That(options.CelShading, Is.EqualTo(style));
            for (int quality = 0; quality < 4; quality++)
            {
                options.ApplyPreset(quality);
                Assert.That(options.CelShading, Is.EqualTo(style), "Quality presets must not overwrite the player's art preference.");
            }
        }

        [Test]
        public void OlderSettingsKeepExistingValuesAndInitializeNewStyle()
        {
            var options = GraphicsOptions.FromJson("{\"Shadows\":1,\"Bloom\":false,\"TextureQuality\":2}");
            Assert.That(options.Shadows, Is.EqualTo(1));
            Assert.That(options.Bloom, Is.False);
            Assert.That(options.TextureQuality, Is.EqualTo(2));
            Assert.That(options.CelShading, Is.Zero, "Instalações novas e definições sem estilo salvo começam no visual original.");
        }

        [TestCase("Survival/barrel", .95f)]
        [TestCase("Factory/box-small", .45f)]
        [TestCase("Furniture/stoolBar", .78f)]
        public void RefinedPropsRestOnGroundAndMatchRequestedHeight(string path, float height)
        {
            var root = new GameObject("TestRoot");
            try
            {
                var prop = PropLibrary.Spawn(path,root.transform,Vector3.zero,0,height,colliderInset:.1f);
                Assert.That(prop, Is.Not.Null);
                var model = prop.GetComponentInChildren<MeshFilter>();
                Assert.That(model.sharedMesh.name, Does.StartWith("arena_"));
                var bounds = model.GetComponent<Renderer>().bounds;
                Assert.That(bounds.min.y, Is.EqualTo(0).Within(.0001));
                Assert.That(bounds.size.y, Is.EqualTo(height).Within(.0001));
                var collider = prop.GetComponent<BoxCollider>();
                Assert.That(collider.bounds.min.y, Is.EqualTo(0).Within(.0001));
                Assert.That(collider.size.y, Is.EqualTo(height).Within(.0001));
                Assert.That(collider.size.x, Is.EqualTo(bounds.size.x-.1f).Within(.0001));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("kitchenBar", 1.05f)]
        [TestCase("kitchenBarEnd", 1.05f)]
        [TestCase("kitchenFridge", 1.9f)]
        [TestCase("bookcaseOpen", 1.8f)]
        [TestCase("loungeSofaLong", .85f)]
        [TestCase("loungeChair", .85f)]
        [TestCase("tableCoffee", .42f)]
        [TestCase("speaker", 1.3f)]
        public void LoungeReplacementPreservesOriginalFootprintAndSupportHeight(string name, float height)
        {
            var original = Object.Instantiate(Resources.Load<GameObject>("Models/Furniture/" + name));
            var root = new GameObject("FurnitureTest");
            try
            {
                var renderers = original.GetComponentsInChildren<Renderer>();
                var originalBounds = renderers[0].bounds;
                foreach (var renderer in renderers) originalBounds.Encapsulate(renderer.bounds);
                var expected = originalBounds.size * (height / originalBounds.size.y);
                var prop = PropLibrary.Spawn("Furniture/" + name, root.transform, Vector3.zero, 0, height);
                Assert.That(prop, Is.Not.Null);
                var mesh = prop.GetComponentInChildren<MeshFilter>();
                Assert.That(mesh.sharedMesh.name, Does.StartWith("lounge_"));
                var bounds = mesh.GetComponent<Renderer>().bounds;
                Assert.That(bounds.min.y, Is.EqualTo(0).Within(.0001f));
                Assert.That(bounds.size.y, Is.EqualTo(height).Within(.0001f));
                var size = prop.GetComponent<BoxCollider>().size;
                Assert.That(size.x, Is.EqualTo(expected.x).Within(.001f));
                Assert.That(size.y, Is.EqualTo(expected.y).Within(.001f));
                Assert.That(size.z, Is.EqualTo(expected.z).Within(.001f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(original); }
        }

        [TestCase("Industrial/shipping-container-a", 1.5f)]
        [TestCase("Industrial/shipping-container-c", 1.5f)]
        [TestCase("Survival/chest", .55f)]
        [TestCase("Factory/cone", .55f)]
        [TestCase("Factory/warning-traffic", 1.2f)]
        public void IndustrialReplacementPreservesOriginalFootprint(string path, float height)
        {
            var original = Object.Instantiate(Resources.Load<GameObject>("Models/" + path));
            var root = new GameObject("IndustrialTest");
            try
            {
                var renderers = original.GetComponentsInChildren<Renderer>();
                var originalBounds = renderers[0].bounds;
                foreach (var renderer in renderers) originalBounds.Encapsulate(renderer.bounds);
                var expected = originalBounds.size * (height / originalBounds.size.y);
                var prop = PropLibrary.Spawn(path, root.transform, Vector3.zero, 0, height);
                Assert.That(prop, Is.Not.Null);
                var mesh = prop.GetComponentInChildren<MeshFilter>();
                Assert.That(mesh.sharedMesh.name, Does.StartWith("industrial_"));
                Assert.That(mesh.GetComponent<Renderer>().bounds.min.y, Is.EqualTo(0).Within(.0001f));
                var size = prop.GetComponent<BoxCollider>().size;
                Assert.That(size.x, Is.EqualTo(expected.x).Within(.001f));
                Assert.That(size.y, Is.EqualTo(expected.y).Within(.001f));
                Assert.That(size.z, Is.EqualTo(expected.z).Within(.001f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(original); }
        }

        [Test]
        public void ImportedMaterialIsCopiedAndOriginalRemainsUnchanged()
        {
            var source = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            VisualFactory.ApplyStyle(2);
            source.SetColor("_BaseColor",Color.magenta);
            var scene = VisualFactory.SceneMaterial(source);
            try
            {
                Assert.That(scene, Is.Not.SameAs(source));
                Assert.That(scene.shader.name, Is.EqualTo("FriendSlop/Cel Lit"));
                Assert.That(source.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                Assert.That(scene.GetColor("_BaseColor"), Is.EqualTo(Color.magenta));
                Assert.That(VisualFactory.SceneMaterial(source), Is.SameAs(scene));
                scene.SetColor("_BaseColor",Color.green);
                Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(Color.magenta));
            }
            finally { Object.DestroyImmediate(scene); Object.DestroyImmediate(source); VisualFactory.ApplyStyle(0); }
        }
    }
}
