using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class GlobalIlluminationTests
    {
        [Test]
        public void RendererRetainsOptionalGiResourcesInPlayerBuilds()
        {
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/PC_Renderer.asset");
            var feature = System.Array.Find(assets, a => a != null && a.GetType().Name == "SurfaceCacheGIRendererFeature");
            Assert.That(feature, Is.Not.Null);
            var serialized = new UnityEditor.SerializedObject(feature);
            Assert.That(serialized.FindProperty("m_Active").boolValue, Is.True,
                "URP strips GI resources and Meta passes when this build-time feature is inactive; runtime settings control whether GI runs.");
        }

        [Test]
        public void GlassProvidesMetaAndTransmissionWithoutReplacingVisiblePass()
        {
            var shader = Resources.Load<Shader>("Shaders/Glass");
            Assert.That(shader, Is.Not.Null);
            var material = (Material)typeof(RoomBuilder).GetMethod("Glass", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
            try
            {
                Assert.That(material.FindPass("Meta"), Is.GreaterThan(0));
                Assert.That(material.renderQueue, Is.EqualTo(3000));
                Assert.That(material.GetTexture("_TransparencyLM"), Is.Not.Null);
                Assert.That(material.GetFloat("_CenterAlpha"), Is.EqualTo(.04f).Within(.001f));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void IncrementalRoomBuildPreservesCompleteRoomAndLights()
        {
            var syncRoot = new GameObject("Synchronous room");
            var stagedRoot = new GameObject("Incremental room");
            try
            {
                syncRoot.AddComponent<RoomBuilder>().Build(1.12f, .56f);
                var staged = stagedRoot.AddComponent<RoomBuilder>();
                var stages = staged.BuildStages(1.12f, .56f);
                Assert.That(stages.MoveNext(), Is.True);
                int firstRenderers = stagedRoot.GetComponentsInChildren<Renderer>().Length;
                while (stages.MoveNext()) { }
                Assert.That(firstRenderers, Is.LessThan(stagedRoot.GetComponentsInChildren<Renderer>().Length));
                Assert.That(stagedRoot.GetComponentsInChildren<Renderer>().Length,
                    Is.EqualTo(syncRoot.GetComponentsInChildren<Renderer>().Length));
                Assert.That(stagedRoot.GetComponentsInChildren<Collider>().Length,
                    Is.EqualTo(syncRoot.GetComponentsInChildren<Collider>().Length));
                Assert.That(stagedRoot.GetComponentsInChildren<Light>().Length,
                    Is.EqualTo(syncRoot.GetComponentsInChildren<Light>().Length));
                Assert.That(staged.Probe, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(syncRoot); Object.DestroyImmediate(stagedRoot); }
        }
    }
}
