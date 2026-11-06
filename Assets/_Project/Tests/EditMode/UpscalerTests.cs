using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEditor;

namespace FriendSlop.Tests
{
    public class UpscalerTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void SavedUpscalerAndScaleSurviveReload(int id)
        {
            var options = GraphicsOptions.FromJson(new GraphicsOptions { Upscaler = id, RenderScale = .65f }.ToJson());
            Assert.That(options.Upscaler, Is.EqualTo(id));
            Assert.That(options.RenderScale, Is.EqualTo(.65f).Within(.001f));
        }

        [Test]
        public void BuildIncludesEveryRuntimeUpscaler()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/PC_RPAsset.asset");
            var serialized = new SerializedObject(asset);
            Assert.That(serialized.FindProperty("m_ScalingMode").enumValueIndex, Is.EqualTo(1));
            var priority = serialized.FindProperty("m_UpscalerPriority");
            var ids = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < priority.arraySize; i++) ids.Add(priority.GetArrayElementAtIndex(i).FindPropertyRelative("upscalerId").stringValue);
            foreach (string id in UpscalerController.Ids) Assert.That(ids, Does.Contain(id), "A runtime option would be stripped from the player build.");
        }
    }
}
