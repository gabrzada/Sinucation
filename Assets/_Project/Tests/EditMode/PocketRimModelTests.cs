using System;
using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Presentation;
using NUnit.Framework;
namespace FriendSlop.Tests
{
    public class PocketRimModelTests
    {
        [Test]
        public void RaisedSteelTrimDoesNotOverhangPlayingCloth()
        {
            var model=FsModels.Get("table","TableFrame");
            Assert.That(model,Is.Not.Null);
            int steel=Array.IndexOf(model.Materials,"steel");
            Assert.That(steel,Is.GreaterThanOrEqualTo(0),"test must inspect the steel trim");
            var settings=new BilliardsPhysicsSettings();
            float hl=settings.TableLength*.5f, hw=settings.TableWidth*.5f;
            var positions=model.Mesh.vertices; var tested=new HashSet<int>(); int overhang=0;
            foreach(int index in model.Mesh.GetTriangles(steel))
            {
                if(!tested.Add(index)) continue;
                var p=positions[index];
                if(p.y>TableView.TopY+.04f && Math.Abs(p.x)<hl-.0001f && Math.Abs(p.z)<hw-.0001f) overhang++;
            }
            Assert.That(overhang,Is.Zero,"raised pocket trim must leave the playing cloth clear for the cue and bridge hand");
        }
    }
}
