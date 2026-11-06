using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Billiards
{
    public readonly struct CushionSegment
    {
        public readonly Vector2 A;
        public readonly Vector2 B;
        /// <summary>Restituição própria (bocas das caçapas); negativa = a das tabelas.</summary>
        public readonly float Restitution;

        public CushionSegment(Vector2 a, Vector2 b, float restitution = -1f)
        {
            A = a;
            B = b;
            Restitution = restitution;
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
        /// <summary>Garganta: a bola cai quando o centro passa a linha entre as pontas das duas bocas.</summary>
        public readonly Vector2 ThroatA, ThroatB;

        public Pocket(Vector2 center, float radius, bool isCorner, Vector2 throatA, Vector2 throatB)
        {
            Center = center;
            Radius = radius;
            IsCorner = isCorner;
            ThroatA = throatA;
            ThroatB = throatB;
        }

        /// <summary>O centro <paramref name="p"/> já passou a garganta (do lado da caçapa, entre as bocas).</summary>
        public bool PastThroat(Vector2 p)
        {
            Vector2 ab = ThroatB - ThroatA;
            float t = Vector2.Dot(p - ThroatA, ab) / ab.sqrMagnitude;
            if (t < -0.05f || t > 1.05f) return false;
            Vector2 n = new Vector2(-ab.y, ab.x);
            if (Vector2.Dot(Center - ThroatA, n) < 0f) n = -n;
            return Vector2.Dot(p - ThroatA, n) > 0f;
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

            // Bocas (jaws): de cada nariz de tabela junto a uma caçapa sai uma face inclinada para dentro da caçapa, como
            // numa mesa real — a entrada é a distância entre narizes, a garganta é mais estreita. Uma bola só cai depois
            // de passar a garganta; mal apontada (ou rápida e torta) bate nas bocas e treme/sai.
            float co = s.CornerPocketOffset, so = s.SidePocketOffset;
            var jaws = new List<CushionSegment>();
            Pockets = new List<Pocket>
            {
                CornerPocket(-1, 1, hl, hw, cg, co, s, jaws),
                SidePocket(1, hw, sg, so, s, jaws),
                CornerPocket(1, 1, hl, hw, cg, co, s, jaws),
                CornerPocket(-1, -1, hl, hw, cg, co, s, jaws),
                SidePocket(-1, hw, sg, so, s, jaws),
                CornerPocket(1, -1, hl, hw, cg, co, s, jaws),
            };
            Jaws = jaws;
            var walls = new List<CushionSegment>(Cushions);
            walls.AddRange(jaws);
            Walls = walls;
        }

        static Pocket CornerPocket(int sx, int sy, float hl, float hw, float cg, float co, BilliardsPhysicsSettings s, List<CushionSegment> jaws)
        {
            float a = s.CornerJawAngle * Mathf.Deg2Rad, depth = s.CornerJawDepth;
            // Nariz na tabela comprida (corre em X) e na curta (corre em Y); cada boca segue para o canto e dobra para fora.
            var longNose = new Vector2(sx * (hl - cg), sy * hw);
            var shortNose = new Vector2(sx * hl, sy * (hw - cg));
            var longThroat = longNose + new Vector2(sx * Mathf.Cos(a), sy * Mathf.Sin(a)) * depth;
            var shortThroat = shortNose + new Vector2(sx * Mathf.Sin(a), sy * Mathf.Cos(a)) * depth;
            jaws.Add(new CushionSegment(longNose, longThroat, s.JawRestitution));
            jaws.Add(new CushionSegment(shortNose, shortThroat, s.JawRestitution));
            return new Pocket(new Vector2(sx * (hl + co), sy * (hw + co)), s.CornerPocketRadius, true, longThroat, shortThroat);
        }

        static Pocket SidePocket(int sy, float hw, float sg, float so, BilliardsPhysicsSettings s, List<CushionSegment> jaws)
        {
            float b = s.SideJawAngle * Mathf.Deg2Rad, depth = s.SideJawDepth;
            var left = new Vector2(-sg, sy * hw);
            var right = new Vector2(sg, sy * hw);
            var leftThroat = left + new Vector2(Mathf.Sin(b), sy * Mathf.Cos(b)) * depth;
            var rightThroat = right + new Vector2(-Mathf.Sin(b), sy * Mathf.Cos(b)) * depth;
            jaws.Add(new CushionSegment(left, leftThroat, s.JawRestitution));
            jaws.Add(new CushionSegment(right, rightThroat, s.JawRestitution));
            return new Pocket(new Vector2(0f, sy * (hw + so)), s.SidePocketRadius, false, leftThroat, rightThroat);
        }

        public float HalfLength { get; }
        public float HalfWidth { get; }
        public float BallRadius { get; }
        public float HeadStringX { get; }
        public Vector2 HeadSpot { get; }
        public Vector2 FootSpot { get; }
        /// <summary>Tabelas entre caçapas (as que se vêem como borracha comprida).</summary>
        public IReadOnlyList<CushionSegment> Cushions { get; }
        /// <summary>Bocas das caçapas (duas por caçapa).</summary>
        public IReadOnlyList<CushionSegment> Jaws { get; }
        /// <summary>Tudo o que a bola pode tocar: tabelas e bocas.</summary>
        public IReadOnlyList<CushionSegment> Walls { get; }
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
