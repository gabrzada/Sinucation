using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Billiards
{
    public readonly struct CushionSegment
    {
        public readonly Vector2 A;
        public readonly Vector2 B;

        public CushionSegment(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;
        }

        public Vector2 ClosestPoint(Vector2 p)
        {
            Vector2 ab = B - A;
            float t = Vector2.Dot(p - A, ab) / ab.sqrMagnitude;
            return A + ab * Mathf.Clamp01(t);
        }
    }

    public readonly struct Pocket
    {
        public readonly Vector2 Center;
        public readonly float Radius;
        public readonly bool IsCorner;

        public Pocket(Vector2 center, float radius, bool isCorner)
        {
            Center = center;
            Radius = radius;
            IsCorner = isCorner;
        }
    }

    /// <summary>
    /// Geometria 2D da mesa no plano da mesa: X ao longo do comprimento, Y ao longo da largura
    /// (mapeado para Z no mundo). A cabeceira (kitchen) fica em X negativo.
    /// </summary>
    public sealed class TableGeometry
    {
        public TableGeometry(BilliardsPhysicsSettings s)
        {
            HalfLength = s.TableLength * 0.5f;
            HalfWidth = s.TableWidth * 0.5f;
            BallRadius = s.BallRadius;
            HeadStringX = -s.TableLength * 0.25f;
            HeadSpot = new Vector2(HeadStringX, 0f);
            FootSpot = new Vector2(s.TableLength * 0.25f, 0f);

            // Rendered cushion noses are inset 16 mm from each end (Blender/table.py).
            float hl = HalfLength, hw = HalfWidth, cg = s.CornerGap + 0.016f, sg = s.SideGap + 0.016f;
            Cushions = new List<CushionSegment>
            {
                new CushionSegment(new Vector2(-hl + cg, hw), new Vector2(-sg, hw)),
                new CushionSegment(new Vector2(sg, hw), new Vector2(hl - cg, hw)),
                new CushionSegment(new Vector2(-hl + cg, -hw), new Vector2(-sg, -hw)),
                new CushionSegment(new Vector2(sg, -hw), new Vector2(hl - cg, -hw)),
                new CushionSegment(new Vector2(-hl, -hw + cg), new Vector2(-hl, hw - cg)),
                new CushionSegment(new Vector2(hl, -hw + cg), new Vector2(hl, hw - cg)),
            };

            float co = s.CornerPocketOffset, so = s.SidePocketOffset;
            Pockets = new List<Pocket>
            {
                new Pocket(new Vector2(-hl - co, hw + co), s.CornerPocketRadius, true),
                new Pocket(new Vector2(0f, hw + so), s.SidePocketRadius, false),
                new Pocket(new Vector2(hl + co, hw + co), s.CornerPocketRadius, true),
                new Pocket(new Vector2(-hl - co, -hw - co), s.CornerPocketRadius, true),
                new Pocket(new Vector2(0f, -hw - so), s.SidePocketRadius, false),
                new Pocket(new Vector2(hl + co, -hw - co), s.CornerPocketRadius, true),
            };
        }

        public float HalfLength { get; }
        public float HalfWidth { get; }
        public float BallRadius { get; }
        public float HeadStringX { get; }
        public Vector2 HeadSpot { get; }
        public Vector2 FootSpot { get; }
        public IReadOnlyList<CushionSegment> Cushions { get; }
        public IReadOnlyList<Pocket> Pockets { get; }

        public bool IsInsidePlayfield(Vector2 p, float margin)
        {
            return Mathf.Abs(p.x) <= HalfLength - margin && Mathf.Abs(p.y) <= HalfWidth - margin;
        }

        public int NearestPocket(Vector2 p)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Pockets.Count; i++)
            {
                float d = (Pockets[i].Center - p).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
