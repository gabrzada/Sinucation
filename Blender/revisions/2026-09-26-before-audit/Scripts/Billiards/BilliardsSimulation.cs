using System;
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using UnityEngine;

namespace FriendSlop.Billiards
{
    public enum ContactType
    {
        CueStrike,
        BallBall,
        Cushion,
        Pocket
    }

    public readonly struct SimContact
    {
        public readonly ContactType Type;
        public readonly int A;
        public readonly int B;
        public readonly float Speed;
        public readonly Vector2 Position;

        public SimContact(ContactType type, int a, int b, float speed, Vector2 position)
        {
            Type = type;
            A = a;
            B = b;
            Speed = speed;
            Position = position;
        }
    }

    /// <summary>Estado mínimo replicável de uma bola (PHY-011).</summary>
    public struct BallState
    {
        public int Number;
        public Vector2 Position;
        public bool Pocketed;
        public int PocketIndex;
    }

    public sealed class SimBall
    {
        public int Number;
        public Vector2 Position;
        public Vector2 Velocity;
        /// <summary>Follow (+) / draw (−) armazenado em m/s, aplicado no primeiro impacto com uma bola.</summary>
        public float Follow;
        /// <summary>Efeito lateral (+ direita) em m/s, transferido nas tabelas.</summary>
        public float English;
        /// <summary>Aceleração lateral repetível (modificadores de curva), até ao primeiro impacto.</summary>
        public float Curve;
        public float CurveTime;
        public bool Pocketed;
        public int PocketIndex = -1;

        public bool IsCue => Number == 0;
        public bool OnTable => !Pocketed;
        public bool IsMoving => Velocity.x != 0f || Velocity.y != 0f;

        public SimBall Clone() => (SimBall)MemberwiseClone();
    }

    /// <summary>
    /// Simulação de bilhar 2D de passo fixo (NFR-003), independente do framerate e sem rendering (NFR-012).
    /// Só a instância autoritativa corre isto; os clientes apresentam snapshots (MULT-002).
    /// </summary>
    public sealed class BilliardsSimulation
    {
        readonly BilliardsPhysicsSettings _s;
        readonly List<SimBall> _balls = new List<SimBall>();
        List<SimBall> _preShotSnapshot;
        ShotRecord _record;
        float _accumulator;

        public BilliardsSimulation(BilliardsPhysicsSettings settings)
        {
            _s = settings;
            Table = new TableGeometry(settings);
        }

        public event Action<SimContact> Contact;

        public TableGeometry Table { get; }
        public BilliardsPhysicsSettings Settings => _s;
        public IReadOnlyList<SimBall> Balls => _balls;
        public int FinalBall { get; private set; }
        public bool IsSimulating { get; private set; }
        public float SimulatedTime { get; private set; }
        public int PhysicsRecoveries { get; private set; }

        public SimBall Cue => GetBall(0);

        public SimBall GetBall(int number)
        {
            foreach (var b in _balls)
                if (b.Number == number)
                    return b;
            return null;
        }

        public int LowestBallOnTable()
        {
            int lowest = -1;
            foreach (var b in _balls)
                if (!b.IsCue && b.OnTable && (lowest < 0 || b.Number < lowest))
                    lowest = b.Number;
            return lowest;
        }

        public int ObjectBallsOnTable()
        {
            int count = 0;
            foreach (var b in _balls)
                if (!b.IsCue && b.OnTable) count++;
            return count;
        }

        // ---------------------------------------------------------------- setup

        /// <summary>GAME-007: monta uma rack nova (9/6/4 ou triangular genérica) com a branca na cabeceira.</summary>
        public void SetupRack(int ballCount, RandomService rng)
        {
            ballCount = Mathf.Clamp(ballCount, 3, 15);
            _balls.Clear();
            IsSimulating = false;
            FinalBall = ballCount;
            _balls.Add(new SimBall { Number = 0, Position = Table.HeadSpot });

            var rows = RackRows(ballCount);
            int finalSlot = RackFinalSlot(ballCount);
            float r = _s.BallRadius;
            float d = 2f * r + 0.0004f;
            float rowDx = d * 0.8660254f;

            var slots = new List<Vector2>();
            for (int row = 0; row < rows.Length; row++)
                for (int j = 0; j < rows[row]; j++)
                    slots.Add(new Vector2(Table.FootSpot.x + row * rowDx, (j - (rows[row] - 1) * 0.5f) * d));

            var middle = new List<int>();
            for (int n = 2; n < ballCount; n++) middle.Add(n);
            rng.Shuffle(middle);

            int mi = 0;
            for (int slot = 0; slot < ballCount; slot++)
            {
                int number = slot == 0 ? 1 : slot == finalSlot ? ballCount : middle[mi++];
                var jitter = new Vector2((float)(rng.NextDouble() - 0.5) * 0.0003f, (float)(rng.NextDouble() - 0.5) * 0.0003f);
                _balls.Add(new SimBall { Number = number, Position = slots[slot] + jitter });
            }
            _balls.Sort((a, b) => a.Number.CompareTo(b.Number));
        }

        /// <summary>Montagem arbitrária, útil para testes e desafios. O número 0 é sempre a branca.</summary>
        public void SetupCustom(IEnumerable<KeyValuePair<int, Vector2>> balls, int finalBall)
        {
            _balls.Clear();
            IsSimulating = false;
            foreach (var kv in balls)
                _balls.Add(new SimBall { Number = kv.Key, Position = kv.Value });
            if (GetBall(0) == null) _balls.Add(new SimBall { Number = 0, Position = Table.HeadSpot });
            _balls.Sort((a, b) => a.Number.CompareTo(b.Number));
            FinalBall = finalBall;
        }

        /// <summary>
        /// Carrega um estado replicado (espelho do cliente). Não simula: serve para render, mira e
        /// validação local de pré-visualização. A autoridade continua a ser o servidor.
        /// </summary>
        public void LoadState(IReadOnlyList<BallState> balls, int finalBall)
        {
            IsSimulating = false;
            FinalBall = finalBall;
            if (_balls.Count != balls.Count)
            {
                _balls.Clear();
                for (int i = 0; i < balls.Count; i++) _balls.Add(new SimBall());
            }
            for (int i = 0; i < balls.Count; i++)
            {
                var b = _balls[i];
                var s = balls[i];
                b.Number = s.Number;
                b.Position = s.Position;
                b.Pocketed = s.Pocketed;
                b.PocketIndex = s.PocketIndex;
                b.Velocity = Vector2.zero;
            }
        }

        public void CopyState(List<BallState> target)
        {
            target.Clear();
            foreach (var b in _balls)
                target.Add(new BallState { Number = b.Number, Position = b.Position, Pocketed = b.Pocketed, PocketIndex = b.PocketIndex });
        }

        static int[] RackRows(int count)
        {
            switch (count)
            {
                case 4: return new[] { 1, 2, 1 };
                case 6: return new[] { 1, 2, 3 };
                case 9: return new[] { 1, 2, 3, 2, 1 };
            }
            var rows = new List<int>();
            int total = 0;
            for (int k = 1; total < count; k++)
            {
                rows.Add(k);
                total += k;
            }
            return rows.ToArray();
        }

        static int RackFinalSlot(int count)
        {
            switch (count)
            {
                case 9: return 4;
                case 6: return 4;
                default: return count - 1;
            }
        }

        // ---------------------------------------------------------------- placement

        public bool IsValidCuePlacement(Vector2 p, bool kitchenOnly)
        {
            return IsFreeSpot(p, 0) && (!kitchenOnly || p.x <= Table.HeadStringX);
        }

        public bool IsFreeSpot(Vector2 p, int ignoreNumber)
        {
            float r = _s.BallRadius;
            if (!Table.IsInsidePlayfield(p, r)) return false;
            float min = 2f * r + 0.001f;
            foreach (var b in _balls)
            {
                if (b.Number == ignoreNumber || !b.OnTable) continue;
                if ((b.Position - p).sqrMagnitude < min * min) return false;
            }
            return true;
        }

        public Vector2 FindFreeSpot(Vector2 preferred, int ignoreNumber, bool kitchenOnly)
        {
            float r = _s.BallRadius;
            for (int ring = 0; ring < 60; ring++)
            {
                int samples = ring == 0 ? 1 : ring * 8;
                for (int i = 0; i < samples; i++)
                {
                    float angle = i * Mathf.PI * 2f / samples;
                    var candidate = preferred + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (ring * r);
                    if (!IsFreeSpot(candidate, ignoreNumber)) continue;
                    if (kitchenOnly && candidate.x > Table.HeadStringX) continue;
                    return candidate;
                }
            }
            return preferred;
        }

        /// <summary>PHY-008: a branca volta à mesa (bola-na-mão / respawn).</summary>
        public void PlaceCueBall(Vector2 position)
        {
            var cue = Cue;
            cue.Position = position;
            cue.Pocketed = false;
            cue.PocketIndex = -1;
            ResetMotion(cue);
        }

        /// <summary>Recoloca uma bola objectivo no ponto de pé (ou no lugar livre mais próximo).</summary>
        public void RespotBall(int number)
        {
            var ball = GetBall(number);
            if (ball == null) return;
            ball.Pocketed = false;
            ball.PocketIndex = -1;
            ResetMotion(ball);
            ball.Position = FindFreeSpot(Table.FootSpot, number, false);
        }

        static void ResetMotion(SimBall b)
        {
            b.Velocity = Vector2.zero;
            b.Follow = 0f;
            b.English = 0f;
            b.Curve = 0f;
            b.CurveTime = 0f;
        }

        // ---------------------------------------------------------------- shot

        public void StrikeCue(ShotParameters shot, ShotRecord record)
        {
            _record = record;
            _preShotSnapshot = TakeSnapshot();

            var cue = Cue;
            float speed = Mathf.Min(_s.CueSpeedForPower(shot.Power) * shot.ImpulseScale, _s.MaxBallSpeed);
            Vector2 dir = shot.Direction.sqrMagnitude > 1e-8f ? shot.Direction.normalized : Vector2.right;

            cue.Velocity = dir * speed;
            cue.Follow = shot.SpinY * speed * _s.FollowFactor;
            cue.English = shot.SpinX * speed * _s.EnglishFactor;
            cue.Curve = shot.CurveAcceleration;
            cue.CurveTime = shot.CurveDuration;

            IsSimulating = true;
            SimulatedTime = 0f;
            _accumulator = 0f;
            Contact?.Invoke(new SimContact(ContactType.CueStrike, 0, -1, speed, cue.Position));
        }

        /// <summary>Avança a simulação em passos fixos; o tempo gráfico só decide quantos passos correm.</summary>
        public void Advance(float deltaTime)
        {
            if (!IsSimulating) return;
            float h = _s.FixedStep;
            _accumulator += deltaTime;
            int steps = 0;
            while (_accumulator >= h && steps < _s.MaxStepsPerFrame && IsSimulating)
            {
                Step(h);
                _accumulator -= h;
                steps++;
            }
            if (steps >= _s.MaxStepsPerFrame) _accumulator = 0f;
        }

        public void Step(float h)
        {
            SimulatedTime += h;

            foreach (var b in _balls)
                if (b.OnTable) Integrate(b, h);

            ResolveBallCollisions();
            ResolveCushions();
            ResolvePockets();

            if (HasInvalidState())
            {
                // PHY-010 / watchdog: restaura o último estado estável antes da tacada.
                PhysicsRecoveries++;
                RestoreSnapshot(_preShotSnapshot);
                if (_record != null) _record.Aborted = true;
                IsSimulating = false;
                return;
            }

            if (SimulatedTime >= _s.MaxSimulationSeconds)
            {
                foreach (var b in _balls) ResetMotion(b);
            }

            bool anyMoving = false;
            foreach (var b in _balls)
                if (b.OnTable && b.IsMoving)
                {
                    anyMoving = true;
                    break;
                }
            if (!anyMoving) IsSimulating = false;
        }

        void Integrate(SimBall b, float h)
        {
            float speed = b.Velocity.magnitude;
            if (speed > 0f)
            {
                if (b.CurveTime > 0f && b.Curve != 0f)
                {
                    Vector2 dir = b.Velocity / speed;
                    b.Velocity += new Vector2(dir.y, -dir.x) * (b.Curve * h);
                    b.CurveTime -= h;
                    speed = b.Velocity.magnitude;
                }

                float newSpeed = speed - (_s.RollingDeceleration + _s.LinearDrag * speed) * h;
                if (newSpeed <= _s.StopSpeed) b.Velocity = Vector2.zero;
                else b.Velocity *= newSpeed / speed;

                b.Position += b.Velocity * h;
            }

            float decay = Mathf.Exp(-_s.SpinDecayPerSecond * h);
            b.Follow *= decay;
            b.English *= decay;
        }

        void ResolveBallCollisions()
        {
            float r = _s.BallRadius;
            float min = 2f * r;
            for (int i = 0; i < _balls.Count; i++)
            {
                var a = _balls[i];
                if (!a.OnTable) continue;
                for (int j = i + 1; j < _balls.Count; j++)
                {
                    var b = _balls[j];
                    if (!b.OnTable) continue;

                    Vector2 d = b.Position - a.Position;
                    float dist2 = d.sqrMagnitude;
                    if (dist2 >= min * min) continue;

                    float dist = Mathf.Sqrt(dist2);
                    Vector2 n = dist > 1e-6f ? d / dist : Vector2.right;
                    float overlap = min - dist;
                    a.Position -= n * (overlap * 0.5f);
                    b.Position += n * (overlap * 0.5f);

                    float approach = Vector2.Dot(a.Velocity - b.Velocity, n);
                    if (approach <= 0f) continue;

                    Vector2 aBefore = a.Velocity;
                    Vector2 bBefore = b.Velocity;
                    float impulse = (1f + _s.BallRestitution) * 0.5f * approach;
                    a.Velocity -= n * impulse;
                    b.Velocity += n * impulse;

                    OnCueImpact(a, aBefore, b);
                    OnCueImpact(b, bBefore, a);
                    a.CurveTime = 0f;
                    b.CurveTime = 0f;

                    Contact?.Invoke(new SimContact(ContactType.BallBall, a.Number, b.Number, approach, (a.Position + b.Position) * 0.5f));
                }
            }
        }

        void OnCueImpact(SimBall cue, Vector2 velocityBefore, SimBall other)
        {
            if (!cue.IsCue) return;
            if (_record != null && _record.FirstContact < 0) _record.FirstContact = other.Number;
            if (cue.Follow != 0f && velocityBefore.sqrMagnitude > 1e-6f)
            {
                cue.Velocity += velocityBefore.normalized * cue.Follow;
                cue.Follow = 0f;
            }
        }

        void ResolveCushions()
        {
            float r = _s.BallRadius;
            foreach (var b in _balls)
            {
                if (!b.OnTable) continue;
                foreach (var seg in Table.Cushions)
                {
                    Vector2 cp = seg.ClosestPoint(b.Position);
                    Vector2 d = b.Position - cp;
                    float dist2 = d.sqrMagnitude;
                    if (dist2 >= r * r) continue;

                    float dist = Mathf.Sqrt(dist2);
                    Vector2 n = dist > 1e-6f ? d / dist : (-cp).normalized;
                    b.Position = cp + n * r;

                    float vn = Vector2.Dot(b.Velocity, n);
                    if (vn >= 0f) continue;

                    Vector2 vt = b.Velocity - n * vn;
                    b.Velocity = vt * (1f - _s.CushionFriction) - n * (vn * _s.CushionRestitution);

                    if (b.English != 0f)
                    {
                        // Efeito lateral (+ direita = rotação anti-horária vista de cima) empurra ao longo da tabela.
                        b.Velocity -= new Vector2(n.y, -n.x) * (b.English * _s.EnglishCushionTransfer);
                        b.English *= 0.4f;
                    }

                    if (b.IsCue && _record != null) _record.CushionHits++;
                    Contact?.Invoke(new SimContact(ContactType.Cushion, b.Number, -1, -vn, cp));
                }
            }
        }

        void ResolvePockets()
        {
            float bx = Table.HalfLength + 0.06f;
            float by = Table.HalfWidth + 0.06f;
            foreach (var b in _balls)
            {
                if (!b.OnTable) continue;

                for (int i = 0; i < Table.Pockets.Count; i++)
                {
                    var pocket = Table.Pockets[i];
                    if ((b.Position - pocket.Center).sqrMagnitude < pocket.Radius * pocket.Radius)
                    {
                        PocketBall(b, i, false);
                        break;
                    }
                }

                if (b.OnTable && (Mathf.Abs(b.Position.x) > bx || Mathf.Abs(b.Position.y) > by))
                    PocketBall(b, Table.NearestPocket(b.Position), true);
            }
        }

        void PocketBall(SimBall b, int pocketIndex, bool offTable)
        {
            float speed = b.Velocity.magnitude;
            b.Pocketed = true;
            b.PocketIndex = pocketIndex;
            ResetMotion(b);
            if (_record != null)
            {
                _record.Pocketed.Add(b.Number);
                if (b.IsCue) _record.CueScratched = true;
                if (offTable) _record.OffTable = true;
            }
            Contact?.Invoke(new SimContact(ContactType.Pocket, b.Number, pocketIndex, speed, Table.Pockets[pocketIndex].Center));
        }

        bool HasInvalidState()
        {
            foreach (var b in _balls)
            {
                if (float.IsNaN(b.Position.x) || float.IsNaN(b.Position.y) || float.IsInfinity(b.Position.x) || float.IsInfinity(b.Position.y))
                    return true;
                if (b.Velocity.sqrMagnitude > _s.MaxBallSpeed * _s.MaxBallSpeed * 4f)
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- snapshots & aim

        /// <summary>PHY-011: snapshot completo (posições, velocidades, spin) para replicação/reconexão.</summary>
        public List<SimBall> TakeSnapshot()
        {
            var snapshot = new List<SimBall>(_balls.Count);
            foreach (var b in _balls) snapshot.Add(b.Clone());
            return snapshot;
        }

        public void RestoreSnapshot(List<SimBall> snapshot)
        {
            if (snapshot == null) return;
            _balls.Clear();
            foreach (var b in snapshot) _balls.Add(b.Clone());
        }

        /// <summary>
        /// Assistência curta de mira: devolve onde a branca tocaria primeiro (bola fantasma), sem projectar
        /// a trajectória pós-colisão.
        /// </summary>
        public bool CastFromCue(Vector2 direction, out Vector2 ghostPosition, out int hitBall)
        {
            var cue = Cue;
            Vector2 origin = cue.Position;
            Vector2 dir = direction.normalized;
            float r = _s.BallRadius;
            float best = float.MaxValue;
            hitBall = -1;

            foreach (var b in _balls)
            {
                if (b.IsCue || !b.OnTable) continue;
                Vector2 oc = origin - b.Position;
                float bq = Vector2.Dot(oc, dir);
                float c = oc.sqrMagnitude - 4f * r * r;
                float disc = bq * bq - c;
                if (disc < 0f) continue;
                float t = -bq - Mathf.Sqrt(disc);
                if (t >= 0f && t < best)
                {
                    best = t;
                    hitBall = b.Number;
                }
            }

            float wall = RayToBox(origin, dir, Table.HalfLength - r, Table.HalfWidth - r);
            if (wall < best)
            {
                best = wall;
                hitBall = -1;
            }

            ghostPosition = origin + dir * best;
            return hitBall >= 0;
        }

        static float RayToBox(Vector2 o, Vector2 d, float hx, float hy)
        {
            float tx = d.x > 1e-6f ? (hx - o.x) / d.x : d.x < -1e-6f ? (-hx - o.x) / d.x : float.MaxValue;
            float ty = d.y > 1e-6f ? (hy - o.y) / d.y : d.y < -1e-6f ? (-hy - o.y) / d.y : float.MaxValue;
            return Mathf.Max(0f, Mathf.Min(tx, ty));
        }
    }
}
