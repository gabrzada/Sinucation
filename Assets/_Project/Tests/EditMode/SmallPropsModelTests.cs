using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;
namespace FriendSlop.Tests
{
    public class SmallPropsModelTests
    {
        [TestCase("Survival/bottle",.26f,false,0f)]
        [TestCase("Survival/bottle-large",.26f,false,0f)]
        [TestCase("Furniture/radio",.22f,false,0f)]
        [TestCase("Furniture/plantSmall2",.3f,false,0f)]
        [TestCase("Furniture/pottedPlant",1.2f,true,.2f)]
        [TestCase("Furniture/lampRoundFloor",1.7f,true,.2f)]
        public void PreserveBoundsAndCollisionPolicy(string path,float height,bool hasCollider,float inset)
        {
            var original=Object.Instantiate(Resources.Load<GameObject>("Models/"+path));
            var root=new GameObject("SmallPropTest");
            try
            {
                var rs=original.GetComponentsInChildren<Renderer>(); var b=rs[0].bounds;
                foreach(var r in rs)b.Encapsulate(r.bounds);
                var expected=b.size*(height/b.size.y);
                var prop=PropLibrary.Spawn(path,root.transform,Vector3.zero,0,height,collider:hasCollider,colliderInset:inset);
                Assert.That(prop,Is.Not.Null);
                var mesh=prop.GetComponentInChildren<MeshFilter>();
                Assert.That(mesh.sharedMesh.name,Does.StartWith("small-props_"));
                var bounds=mesh.GetComponent<Renderer>().bounds;
                Assert.That(bounds.min.y,Is.EqualTo(0).Within(.0001f));
                Assert.That(bounds.size.x,Is.EqualTo(expected.x).Within(.001f));
                Assert.That(bounds.size.y,Is.EqualTo(expected.y).Within(.001f));
                Assert.That(bounds.size.z,Is.EqualTo(expected.z).Within(.001f));
                var collider=prop.GetComponent<BoxCollider>();
                if(hasCollider) {
                    Assert.That(collider,Is.Not.Null);
                    Assert.That(collider.size.x,Is.EqualTo(Mathf.Max(.02f,expected.x-inset)).Within(.001f));
                    Assert.That(collider.size.z,Is.EqualTo(Mathf.Max(.02f,expected.z-inset)).Within(.001f));
                } else Assert.That(collider,Is.Null);
                if(path.EndsWith("lampRoundFloor"))
                    Assert.That(mesh.GetComponent<Renderer>().shadowCastingMode,Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
            }
            finally {Object.DestroyImmediate(root);Object.DestroyImmediate(original);}
        }
    }
}
