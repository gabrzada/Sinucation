using System.Reflection;
using System.Collections;
using UnityEngine.TestTools;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class ThrowableModelTests
    {
        [UnityTest]
        public IEnumerator ThrownModelsLandOnTheFloorInPlayMode()
        {
            yield return new EnterPlayMode();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, -.05f, 0);
            floor.transform.localScale = new Vector3(3, .1f, 3);
            var root = new GameObject("ThrowablesPhysicsTest");
            var view = root.AddComponent<ThrowablesView>();
            view.Initialize(null);
            var spawn = typeof(ThrowablesView).GetMethod("OnThrown", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 3; i++)
                spawn.Invoke(view, new object[] { new FriendSlop.Net.ThrowInfo { Id = i + 1, Origin = new Vector3((i - 1) * .4f, .5f, 0) } });
            // Batch mode can run many rendered frames before the fixed clock advances.
            // Step PhysX explicitly after deferred primitive collider removal.
            yield return null;
            var previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Physics.SyncTransforms();
            for (int step = 0; step < 150; step++) Physics.Simulate(.02f);
            Physics.simulationMode = previousMode;
            for (int i = 0; i < 3; i++)
            {
                var prop = root.transform.GetChild(i);
                Assert.That(prop.position.y, Is.InRange(.02f, .15f), prop.name + " should rest on the floor");
                Assert.That(prop.GetComponent<Rigidbody>().linearVelocity.magnitude, Is.LessThan(.2f));
            }
            Object.Destroy(root); Object.Destroy(floor);
            yield return null;
            yield return new ExitPlayMode();
        }

        [Test]
        public void NewMeshesKeepTheThreeExistingPhysicsShapes()
        {
            var root = new GameObject("ThrowablesTest");
            try
            {
                var view = root.AddComponent<ThrowablesView>();
                view.Initialize(null);
                var onThrown = typeof(ThrowablesView).GetMethod("OnThrown", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onThrown, Is.Not.Null);
                for (int i = 0; i < 3; i++) onThrown.Invoke(view, new object[] { new FriendSlop.Net.ThrowInfo { Id = i + 1 } });
                Assert.That(root.transform.childCount, Is.EqualTo(3));
                var paper = root.transform.GetChild(0);
                var can = root.transform.GetChild(1);
                var duck = root.transform.GetChild(2);
                Assert.That(paper.GetComponent<SphereCollider>().radius, Is.EqualTo(.5f).Within(.00001f));
                Assert.That(paper.localScale.x, Is.EqualTo(.09f).Within(.00001f));
                Assert.That(can.GetComponent<CapsuleCollider>().radius, Is.EqualTo(.5f).Within(.00001f));
                Assert.That(can.GetComponent<CapsuleCollider>().height, Is.EqualTo(2f).Within(.00001f));
                Assert.That(can.localScale, Is.EqualTo(new Vector3(.07f,.06f,.07f)));
                Assert.That(duck.GetComponent<SphereCollider>().radius, Is.EqualTo(.07f));
                for (int i = 0; i < 3; i++)
                {
                    var prop = root.transform.GetChild(i);
                    Assert.That(prop.gameObject.layer, Is.EqualTo(GameplayCollision.Throwable));
                    Assert.That(prop.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1));
                    Assert.That(prop.GetComponent<Rigidbody>().mass, Is.EqualTo(.15f));
                    var mesh = prop.Find(i == 0 ? "PaperBall" : i == 1 ? "Can" : "Duck");
                    Assert.That(mesh, Is.Not.Null);
                    Assert.That(mesh.GetComponent<MeshRenderer>().enabled, Is.True);
                    Assert.That(mesh.lossyScale.x, Is.EqualTo(1).Within(.00001f));
                    Assert.That(mesh.lossyScale.y, Is.EqualTo(1).Within(.00001f));
                    Assert.That(mesh.lossyScale.z, Is.EqualTo(1).Within(.00001f));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}

