using System.Reflection;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;
namespace FriendSlop.Tests
{
    public class CueModelTests
    {
        [TestCase(.75f)] [TestCase(1.1f)] [TestCase(2.4f)]
        public void TaperedCueKeepsTipAndButtAtExpectedPositions(float length)
        {
            var root = new GameObject("CueTest");
            try
            {
                var view = root.AddComponent<CueView>(); view.Initialize(null);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var cue=(Transform)typeof(CueView).GetField("_cue",flags).GetValue(view);
                typeof(CueView).GetMethod("SetCueLength",flags).Invoke(view,new object[]{length});
                var shaft=cue.Find("Shaft"); var butt=cue.Find("Butt"); var tip=cue.Find("Tip");
                Assert.That(shaft.Find("Shaft"),Is.Not.Null); Assert.That(butt.Find("Butt"),Is.Not.Null);
                Assert.That(shaft.GetComponent<Renderer>().enabled,Is.False);
                Assert.That(butt.GetComponent<Renderer>().enabled,Is.False);
                Vector3 Point(Transform rod,float end) => cue.InverseTransformPoint(rod.TransformPoint(new Vector3(0,end,0)));
                float shaftLength=length-.3f;
                Assert.That(Point(shaft,1).z,Is.EqualTo(0).Within(.00001f));
                Assert.That(Point(shaft,-1).z,Is.EqualTo(-shaftLength).Within(.00001f));
                Assert.That(Point(butt,1).z,Is.EqualTo(-shaftLength).Within(.00001f));
                Assert.That(Point(butt,-1).z,Is.EqualTo(-length).Within(.00001f));
                Assert.That(Point(tip,1).z,Is.EqualTo(0).Within(.00001f));
                Assert.That(cue.GetComponentsInChildren<Collider>(true).Length,Is.Zero);
            }
            finally {Object.DestroyImmediate(root);}
        }
    }
}
