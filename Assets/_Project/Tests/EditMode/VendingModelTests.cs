using System.Linq;
using System.Reflection;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class VendingModelTests
    {
        [Test]
        public void CabinetsPreserveDisplaysAndColliders()
        {
            var root = new GameObject("RoomTest");
            try
            {
                var room = root.AddComponent<RoomBuilder>();
                foreach (var method in new[] { "BuildGumballMachine", "BuildBallVending" })
                    typeof(RoomBuilder).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(room, new object[] { root.transform, Vector3.zero });
                var gum = root.transform.Find("GumballMachine");
                var vending = root.transform.Find("BallVending");
                Assert.That(gum.Find("GumballBase"), Is.Not.Null);
                Assert.That(vending.Find("VendingBody"), Is.Not.Null);
                Assert.That(gum.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(.62f,1.7f,.62f)));
                Assert.That(vending.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(1.12f,2.1f,.82f)));
                Assert.That(gum.Find("Globe").GetComponent<Renderer>().enabled, Is.True);
                var balls = gum.Cast<Transform>().Where(t => t.name == "Gumball").ToArray();
                Assert.That(balls.Length, Is.EqualTo(26));
                for (int i = 0; i < balls.Length; i++)
                {
                    Assert.That((balls[i].localPosition - new Vector3(0,1.24f,0)).magnitude, Is.LessThanOrEqualTo(.280f));
                    for (int j = 0; j < i; j++)
                        Assert.That(Vector3.Distance(balls[i].localPosition, balls[j].localPosition), Is.GreaterThan(.099f));
                }
                Assert.That(vending.Cast<Transform>().Count(t => t.name == "Ball"), Is.EqualTo(9));
                Assert.That(vending.Find("DisplayBack").GetComponent<Renderer>().enabled, Is.True);
                Assert.That(vending.Find("Tray"), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
