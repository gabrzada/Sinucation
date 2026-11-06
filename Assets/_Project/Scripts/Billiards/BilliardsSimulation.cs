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

    /// <summary>Resultado de BilliardsSimulation.PredictCue (linha branca e amarela da mira).</summary>
    public struct AimPrediction
    {
        public bool HitBall;
        public int Ball;
        /// <summary>Centro da branca no instante do toque (a "bola fantasma").</summary>
        public Vector2 CueAtContact;
        /// <summary>Direcção de saída da bola tocada (linha dos centros no toque).</summary>
        public Vector2 ObjectDirection;
        public float ObjectSpeed;
        /// <summary>Velocidade da branca logo depois do toque (inclui o follow/draw).</summary>
        public Vector2 CueAfter;
        public bool CuePocketed;
        public int Cushions;
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

        public bool IsValidCuePlacement(Vector2 p, bool kitchenOnly) => DescribePlacement(p, kitchenOnly) == null;

        /// <summary>Null se a branca pode ficar em <paramref name="p"/>; senão o motivo (o mesmo no cliente e no servidor).</summary>
        public string DescribePlacement(Vector2 p, bool kitchenOnly)
        {
            if (!Table.IsInsidePlayfield(p, _s.BallRadius)) return "Fora da área de jogo.";
            if (kitchenOnly && p.x > Table.HeadStringX + 1e-5f) return "Na abertura a branca fica atrás da linha de cabeceira.";
            if (!IsFreeSpot(p, 0)) return "Encostada a outra bola.";
            return null;
        }

        /// <summary>
        /// Sítio válido mais próximo de <paramref name="wanted"/> (até <paramref name="maxDistance"/>), para a pré-visualização
        /// "encaixar" quando o jogador aponta para cima de outra bola ou da tabela.
        /// </summary>
        public bool TryFindPlacementNear(Vector2 wanted, bool kitchenOnly, float maxDistance, out Vector2 result)
        {
            float r = _s.BallRadius;
            float maxX = kitchenOnly ? Table.HeadStringX : Table.HalfLength - r;
            wanted.x = Mathf.Clamp(wanted.x, -Table.HalfLength + r, maxX);
            wanted.y = Mathf.Clamp(wanted.y, -Table.HalfWidth + r, Table.HalfWidth - r);
            result = wanted;
            if (IsValidCuePlacement(wanted, kitchenOnly)) return true;
            float step = r * 0.25f;
            int rings = Mathf.CeilToInt(maxDistance / step);
            for (int ring = 1; ring <= rings; ring++)
            {
                int samples = 8 + ring * 4;
                float bestD = float.MaxValue;
                bool found = false;
                for (int i = 0; i < samples; i++)
                {
                    float a = i * Mathf.PI * 2f / samples;
                    var c = wanted + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ring * step);
                    if (!IsValidCuePlacement(c, kitchenOnly)) continue;
                    float d = (c - wanted).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        result = c;
                        found = true;
                    }
                }
                if (found) return true;
            }
            return false;
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
                if (b.OnTable) Integrate(b, h, OnPocketShelf(b.Position));
            ApplyPocketShelf(h);

            // Colisões com o instante exacto de contacto (recua o par até se tocarem, resolve e volta a avançar o
            // resto do passo): o ângulo de saída não depende da penetração, nem nas tacadas mais fortes.
            for (int pass = 0; pass < 3; pass++)
                if (!ResolveBallCollisions(h)) break;
            ResolveCushions(h);
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

        /// <param name="onShelf">
        /// Na base inclinada de uma caçapa a bola não "pára" abaixo de StopSpeed: a inclinação vence o atrito de rolamento
        /// e a bola rola para dentro (antes ficava congelada na boca e a física corria até ao limite de 25 s).
        /// </param>
        void Integrate(SimBall b, float h, bool onShelf = false)
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
                if (newSpeed <= (onShelf ? 0f : _s.StopSpeed)) b.Velocity = Vector2.zero;
                else b.Velocity *= newSpeed / speed;

                b.Position += b.Velocity * h;
            }

            float decay = Mathf.Exp(-_s.SpinDecayPerSecond * h);
            b.Follow *= decay;
            b.English *= decay;
        }

        /// <summary>
        /// Quanto tempo atrás (0..h) o ponto relativo <paramref name="d"/>, a mover-se com <paramref name="w"/>, estava à
        /// distância <paramref name="contact"/>. Devolve −1 se o toque não aconteceu neste passo (já estavam sobrepostos).
        /// </summary>
        static float TimeSinceContact(Vector2 d, Vector2 w, float contact, float h)
        {
            float ww = Vector2.Dot(w, w);
            if (ww < 1e-12f) return -1f;
            float dw = Vector2.Dot(d, w);
            float c = Vector2.Dot(d, d) - contact * contact;
            float disc = dw * dw - ww * c;
            if (disc < 0f) return -1f;
            float tau = (dw + Mathf.Sqrt(disc)) / ww;
            return tau >= 0f && tau <= h * 1.001f ? tau : -1f;
        }

        bool ResolveBallCollisions(float h)
        {
            float r = _s.BallRadius;
            float min = 2f * r;
            bool any = false;
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

                    float approachNow = Vector2.Dot(a.Velocity - b.Velocity, dist2 > 1e-12f ? d / Mathf.Sqrt(dist2) : Vector2.right);
                    float tau = approachNow > 0f ? TimeSinceContact(d, b.Velocity - a.Velocity, min, h) : -1f;
                    if (tau > 0f)
                    {
                        // Recua os dois até ao instante do toque: a normal é a da linha dos centros nesse instante.
                        a.Position -= a.Velocity * tau;
                        b.Position -= b.Velocity * tau;
                        d = b.Position - a.Position;
                    }

                    float dist = d.magnitude;
                    Vector2 n = dist > 1e-6f ? d / dist : Vector2.right;
                    float approach = Vector2.Dot(a.Velocity - b.Velocity, n);
                    if (approach > 0f)
                    {
                        any = true;
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

                    if (tau > 0f)
                    {
                        a.Position += a.Velocity * tau;
                        b.Position += b.Velocity * tau;
                    }

                    // Nunca deixa sobreposição residual (bolas encostadas ou empurradas por uma terceira).
                    d = b.Position - a.Position;
                    dist = d.magnitude;
                    if (dist < min)
                    {
                        n = dist > 1e-6f ? d / dist : n;
                        float overlap = min - dist;
                        a.Position -= n * (overlap * 0.5f);
                        b.Position += n * (overlap * 0.5f);
                    }
                }
            }
            return any;
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

        void ResolveCushions(float h)
        {
            foreach (var b in _balls)
                if (b.OnTable) CollideCushions(b, h, _record, true);
        }

        /// <summary>Tabelas (segmentos) e as pontas arredondadas junto às caçapas, com o instante exacto do toque.</summary>
        void CollideCushions(SimBall b, float h, ShotRecord record, bool emit)
        {
            float r = _s.BallRadius;
            foreach (var seg in Table.Walls)
            {
                Vector2 cp = seg.ClosestPoint(b.Position);
                Vector2 d = b.Position - cp;
                float dist2 = d.sqrMagnitude;
                if (dist2 >= r * r) continue;

                float dist = Mathf.Sqrt(dist2);
                Vector2 n = dist > 1e-6f ? d / dist : (-cp).normalized;
                float vn = Vector2.Dot(b.Velocity, n);
                float tau = -1f;
                if (vn < 0f)
                {
                    bool endpoint = (cp - seg.A).sqrMagnitude < 1e-10f || (cp - seg.B).sqrMagnitude < 1e-10f;
                    tau = endpoint ? TimeSinceContact(d, b.Velocity, r, h) : Mathf.Min((r - dist) / -vn, h);
                }
                if (tau > 0f)
                {
                    b.Position -= b.Velocity * tau;
                    cp = seg.ClosestPoint(b.Position);
                    d = b.Position - cp;
                    dist = d.magnitude;
                    n = dist > 1e-6f ? d / dist : n;
                    vn = Vector2.Dot(b.Velocity, n);
                }

                if (vn < 0f)
                {
                    Vector2 vt = b.Velocity - n * vn;
                    float restitution = seg.Restitution >= 0f ? seg.Restitution : _s.CushionRestitution;
                    b.Velocity = vt * (1f - _s.CushionFriction) - n * (vn * restitution);

                    if (b.English != 0f)
                    {
                        // Efeito lateral (+ direita = rotação anti-horária vista de cima) empurra ao longo da tabela.
                        b.Velocity -= new Vector2(n.y, -n.x) * (b.English * _s.EnglishCushionTransfer);
                        b.English *= 0.4f;
                    }

                    if (b.IsCue && record != null) record.CushionHits++;
                    if (emit) Contact?.Invoke(new SimContact(ContactType.Cushion, b.Number, -1, -vn, cp));
                }

                if (tau > 0f) b.Position += b.Velocity * tau;
                cp = seg.ClosestPoint(b.Position);
                d = b.Position - cp;
                if (d.sqrMagnitude < r * r)
                {
                    dist = d.magnitude;
                    n = dist > 1e-6f ? d / dist : n;
                    b.Position = cp + n * r;
                }
            }
        }

        /// <summary>
        /// Caçapas. Na física 2D as tabelas e as bocas são contínuas: o centro de uma bola só sai da mesa pela garganta
        /// de uma caçapa, e aí a bola cai nela. A mesa é plana e nada salta: não existe "bola fora da mesa".
        /// </summary>
        /// <summary>
        /// A base da caçapa é inclinada para o buraco: uma bola que já passou o nariz das tabelas (está entre as bocas)
        /// rola para dentro em vez de ficar parada na entrada. Actua fora da linha das tabelas e, nas caçapas de canto,
        /// também no cantinho do pano que fica depois da garganta: antes uma bola lenta parava ali, com o centro já por
        /// cima do buraco, e nunca caía. Depois da garganta puxa para o centro da caçapa (até a bola sair do pano e cair).
        /// No resto do pano nada puxa — uma bola parada entre os narizes e a garganta fica "pendurada" na boca.
        /// </summary>
        void ApplyPocketShelf(float h)
        {
            if (_s.ShelfAcceleration <= 0f) return;
            foreach (var b in _balls)
            {
                if (!b.OnTable || !OnPocketShelf(b.Position)) continue;
                var pocket = Table.Pockets[Table.NearestPocket(b.Position)];
                Vector2 to = (pocket.PastThroat(b.Position) ? pocket.Center : (pocket.ThroatA + pocket.ThroatB) * 0.5f) - b.Position;
                if (to.sqrMagnitude > 1e-8f) b.Velocity += to.normalized * (_s.ShelfAcceleration * h);
            }
        }

        bool OnPocketShelf(Vector2 p)
        {
            if (_s.ShelfAcceleration <= 0f) return false;
            if (Mathf.Abs(p.x) > Table.HalfLength || Mathf.Abs(p.y) > Table.HalfWidth) return true;
            // Atalho: só junto aos cantos a garganta entra no pano.
            if (Mathf.Abs(p.x) < Table.HalfLength - 0.1f || Mathf.Abs(p.y) < Table.HalfWidth - 0.1f) return false;
            return Table.Pockets[Table.NearestPocket(p)].PastThroat(p);
        }

        void ResolvePockets()
        {
            foreach (var b in _balls)
            {
                if (!b.OnTable) continue;
                int pocket = PocketAt(b.Position);
                if (pocket >= 0) PocketBall(b, pocket);
            }
        }

        /// <summary>Índice da caçapa onde cai uma bola com o centro em <paramref name="p"/>, ou −1.</summary>
        public int PocketAt(Vector2 p)
        {
            // Só fora da linha das tabelas (dentro das bocas): no pano nenhuma bola cai nem é puxada para a caçapa.
            // Nas de canto a garganta atravessa o canto da área de jogo; aí a base inclinada (ApplyPocketShelf) leva a
            // bola até sair do pano, para nunca ficar parada entre a garganta e o canto.
            if (Mathf.Abs(p.x) <= Table.HalfLength && Mathf.Abs(p.y) <= Table.HalfWidth) return -1;
            // Uma posição arbitrária fora da mesa nunca é caçapa; uma bola que escapasse seria apanhada pelo vigilante
            // (HasInvalidState repõe a mesa) — com tabelas e bocas contínuas não acontece.
            for (int i = 0; i < Table.Pockets.Count; i++)
                if (Table.Pockets[i].PastThroat(p)) return i;
            return -1;
        }

        void PocketBall(SimBall b, int pocketIndex)
        {
            float speed = b.Velocity.magnitude;
            b.Pocketed = true;
            b.PocketIndex = pocketIndex;
            ResetMotion(b);
            if (_record != null)
            {
                _record.Pocketed.Add(b.Number);
                if (b.IsCue) _record.CueScratched = true;
            }
            Contact?.Invoke(new SimContact(ContactType.Pocket, b.Number, pocketIndex, speed, Table.Pockets[pocketIndex].Center));
        }

        bool HasInvalidState()
        {
            foreach (var b in _balls)
            {
                if (float.IsNaN(b.Position.x) || float.IsNaN(b.Position.y) || float.IsInfinity(b.Position.x) || float.IsInfinity(b.Position.y))
                    return true;
                if (b.OnTable && !Table.IsInsidePlayfield(b.Position, -_s.BallRadius * 3f)) return true;
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
        /// Previsão da tacada com o MESMO integrador da simulação autoritativa: atrito, curva dos modificadores, tabelas
        /// (e efeito lateral nelas) e caçapas. Até ao primeiro toque noutra bola só a branca se mexe, por isso a linha é
        /// exacta para tacadas sem erro aleatório; depois do toque dá a direcção de saída da bola tocada (linha dos
        /// centros) e a da branca (com o follow/draw). Devolve true se a branca toca numa bola.
        /// </summary>
        public bool PredictCue(ShotParameters shot, List<Vector2> path, out AimPrediction prediction, float maxSeconds = 8f)
        {
            prediction = new AimPrediction { Ball = -1 };
            path.Clear();
            var cue = Cue;
            if (cue == null || cue.Pocketed) return false;

            var b = cue.Clone();
            float speed = Mathf.Min(_s.CueSpeedForPower(shot.Power) * shot.ImpulseScale, _s.MaxBallSpeed);
            Vector2 dir = shot.Direction.sqrMagnitude > 1e-8f ? shot.Direction.normalized : Vector2.right;
            b.Velocity = dir * speed;
            b.Follow = shot.SpinY * speed * _s.FollowFactor;
            b.English = shot.SpinX * speed * _s.EnglishFactor;
            b.Curve = shot.CurveAcceleration;
            b.CurveTime = shot.CurveDuration;
            path.Add(b.Position);

            float h = _s.FixedStep;
            float min = 2f * _s.BallRadius;
            var record = new ShotRecord();
            int steps = Mathf.CeilToInt(maxSeconds / h);
            for (int step = 1; step <= steps && b.IsMoving; step++)
            {
                Vector2 start = b.Position;
                Integrate(b, h);
                Vector2 move = b.Position - start;
                float len = move.magnitude;
                SimBall hit = null;
                float best = float.MaxValue;
                if (len > 1e-9f)
                {
                    Vector2 md = move / len;
                    foreach (var other in _balls)
                    {
                        if (other.IsCue || !other.OnTable) continue;
                        Vector2 oc = start - other.Position;
                        float c = oc.sqrMagnitude - min * min;
                        if (c <= 0f)
                        {
                            if (Vector2.Dot(-oc, md) <= 0f) continue;
                            hit = other;
                            best = 0f;
                            break;
                        }
                        float bq = Vector2.Dot(oc, md);
                        float disc = bq * bq - c;
                        if (disc < 0f) continue;
                        float t = -bq - Mathf.Sqrt(disc);
                        if (t >= 0f && t <= len && t < best)
                        {
                            best = t;
                            hit = other;
                        }
                    }
                    if (hit != null)
                    {
                        Vector2 contact = start + md * best;
                        Vector2 n = (hit.Position - contact).normalized;
                        float approach = Mathf.Max(0f, Vector2.Dot(b.Velocity, n));
                        float impulse = (1f + _s.BallRestitution) * 0.5f * approach;
                        Vector2 after = b.Velocity - n * impulse;
                        if (b.Follow != 0f && b.Velocity.sqrMagnitude > 1e-6f) after += b.Velocity.normalized * b.Follow;
                        path.Add(contact);
                        prediction = new AimPrediction
                        {
                            HitBall = true,
                            Ball = hit.Number,
                            CueAtContact = contact,
                            ObjectDirection = n,
                            ObjectSpeed = impulse,
                            CueAfter = after,
                            Cushions = record.CushionHits
                        };
                        return true;
                    }
                }

                CollideCushions(b, h, record, false);
                if (PocketAt(b.Position) >= 0)
                {
                    path.Add(b.Position);
                    prediction.CuePocketed = true;
                    prediction.Cushions = record.CushionHits;
                    return false;
                }
                if (step % 12 == 0) path.Add(b.Position);
            }
            path.Add(b.Position);
            prediction.Cushions = record.CushionHits;
            return false;
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
