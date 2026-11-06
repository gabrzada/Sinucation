using System;
using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Match
{
    /// <summary>
    /// Estado de mira visível para os outros (taco do atirador, bola-na-mão). Puramente apresentação:
    /// nunca é usado para decidir a tacada, que chega sempre como ShotRequest.
    /// </summary>
    public struct AimState
    {
        public int PlayerId;
        public float Yaw;
        public float Pull;
        public Vector2 Spin;
        public bool Placing;
        public Vector2 PlacePosition;

        public bool IsValid => PlayerId >= 0;
        public static AimState None => new AimState { PlayerId = -1 };

        public Vector2 Direction => new Vector2(Mathf.Cos(Yaw * Mathf.Deg2Rad), Mathf.Sin(Yaw * Mathf.Deg2Rad));
    }

    /// <summary>Nível dos bots da sala (regra do anfitrião).</summary>
    public enum BotDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2
    }

    /// <summary>Decisão de tacada de um bot/autoplay, calculada sobre o estado da mesa.</summary>
    public struct BotShot
    {
        public Vector2 Direction;
        public float Power;
        public bool WantsPlacement;
        public Vector2 Placement;
    }

    /// <summary>
    /// Bots do lado do servidor (treino e salas incompletas). Enviam exactamente os mesmos comandos que um
    /// humano, com pausas para parecerem pessoas; a autoridade valida tudo da mesma forma.
    /// </summary>
    public sealed class BotBrain
    {
        readonly System.Random _rng;
        readonly Dictionary<int, float> _shopTimers = new Dictionary<int, float>();
        int _turnKey = int.MinValue;
        float _turnTimer;
        float _turnDuration;
        float _readyTimer = -1f;
        float _aimDuration;
        float _windUpDuration;
        float _aimSweep;
        const float SettleSeconds = 0.35f;
        const float HoldSeconds = 0.12f;
        const float MaxWalkSeconds = 12f;
        BotShot _plannedShot;
        bool _placed;
        string _riskKey;
        float _riskTimer;

        public BotBrain(int seed)
        {
            _rng = new System.Random(seed);
        }

        /// <summary>Mira actual do bot que está a jogar, para os clientes desenharem o taco.</summary>
        public AimState Aim { get; private set; } = AimState.None;

        public float Skill { get; set; } = 1f;
        BotDifficulty _difficulty = BotDifficulty.Medium;

        /// <summary>Nível dos bots: mira, escolha da tacada, força, bola na mão e o tempo que demoram a decidir.</summary>
        public BotDifficulty Difficulty
        {
            get => _difficulty;
            set
            {
                _difficulty = value;
                Skill = SkillFor(value);
            }
        }
        // Optional presentation gate; headless simulations keep the same deterministic rules.
        public Func<int, bool> ReadyToShoot { get; set; }

        public void Update(MatchDirector d, float dt)
        {
            // Pausa: os temporizadores do bot param e a mira fica como estava (o taco não salta nem desaparece).
            if (d.Paused) return;
            Aim = AimState.None;
            if (d.SabotageContinuous) UpdateContinuousSabotage(d, dt);
            switch (d.Phase)
            {
                case MatchPhase.AwaitShot:
                    UpdateShot(d, dt);
                    break;
                case MatchPhase.AwaitTrigger:
                    UpdateTrigger(d, dt);
                    break;
                case MatchPhase.Shop:
                    UpdateShop(d, dt);
                    break;
                default:
                    if (d.Phase != MatchPhase.Shop && !d.SabotageContinuous) _shopTimers.Clear();
                    break;
            }
        }

        /// <summary>
        /// Sequência de um turno de bot: pensar (e colocar a branca, se tiver bola na mão) → chegar ao lugar de tacada
        /// (gate de apresentação) → assentar na pose → afinar a mira → puxar o taco → tacada. A preparação conta a
        /// partir da chegada, não do início do turno: antes o taco já estava todo puxado enquanto o boneco ainda
        /// caminhava, e a tacada saía de repente (ou só no último segundo, se nunca chegasse ao sítio).
        /// </summary>
        void UpdateShot(MatchDirector d, float dt)
        {
            var shooter = d.CurrentPlayer;
            if (shooter == null || !shooter.Profile.IsBot) return;

            int key = d.ShotSequence * 16 + d.CurrentPlayerId;
            if (key != _turnKey)
            {
                _turnKey = key;
                _turnTimer = 0f;
                _readyTimer = -1f;
                _turnDuration = 0.8f + (float)_rng.NextDouble() * 0.9f;
                _aimDuration = 0.45f + (float)_rng.NextDouble() * 0.6f;
                _windUpDuration = 0.55f + (float)_rng.NextDouble() * 0.35f;
                _aimSweep = ((float)_rng.NextDouble() * 2f - 1f) * 3f;
                _plannedShot = PlanShot(d.Sim, d.BallInHand, d.BallInHandKitchenOnly, _rng, _difficulty, Skill);
                _placed = false;
            }

            _turnTimer += dt;
            if (!_placed && _plannedShot.WantsPlacement && _turnTimer > _turnDuration * 0.5f)
            {
                d.RequestPlaceCueBall(shooter.Id, _plannedShot.Placement);
                _placed = true;
                _plannedShot = PlanShot(d.Sim, false, false, _rng, _difficulty, Skill);
            }

            float yaw = Mathf.Atan2(_plannedShot.Direction.y, _plannedShot.Direction.x) * Mathf.Rad2Deg;
            Aim = new AimState { PlayerId = shooter.Id, Yaw = yaw, Pull = 0f };
            if (_turnTimer < _turnDuration) return;

            // Chegou ao lugar? Sem gate (testes/servidor sem apresentação) conta como pronto logo. Rede de segurança:
            // se o caminho estiver bloqueado, joga na mesma antes de o relógio do turno acabar.
            if (_readyTimer < 0f)
            {
                bool ready = ReadyToShoot == null || ReadyToShoot(shooter.Id);
                bool late = _turnTimer > _turnDuration + MaxWalkSeconds ||
                            (d.PhaseDuration > 0f && d.PhaseTimeRemaining < SettleSeconds + _aimDuration + _windUpDuration + 1f);
                if (!ready && !late) return;
                _readyTimer = 0f;
            }
            _readyTimer += dt;

            float windUpStart = SettleSeconds + _aimDuration;
            float windUp = Mathf.Clamp01((_readyTimer - windUpStart) / _windUpDuration);
            // Afina a mira: o taco chega à linha final com um pequeno varrimento (só visual; a direcção enviada é a planeada).
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_readyTimer - SettleSeconds * 0.5f) / (_aimDuration + SettleSeconds * 0.5f)));
            // Puxa com aceleração suave e segura um instante no fim antes de soltar (como uma pessoa).
            Aim = new AimState { PlayerId = shooter.Id, Yaw = yaw + _aimSweep * (1f - settle), Pull = Mathf.SmoothStep(0f, 1f, windUp) * _plannedShot.Power };
            if (_readyTimer < windUpStart + _windUpDuration + HoldSeconds) return;

            d.SubmitShot(new ShotRequest
            {
                PlayerId = shooter.Id,
                ShotSequence = d.ShotSequence,
                Direction = _plannedShot.Direction,
                Power = _plannedShot.Power
            });
        }

        void UpdateTrigger(MatchDirector d, float dt)
        {
            var selected = d.GetPlayer(d.SelectedPlayerId);
            if (selected == null || !selected.Profile.IsBot) return;
            if (_riskKey != d.RiskEventId)
            {
                _riskKey = d.RiskEventId;
                _riskTimer = 1.5f + (float)_rng.NextDouble() * 2f;
            }
            _riskTimer -= dt;
            if (_riskTimer <= 0f) d.PullTrigger(selected.Id, d.RiskEventId);
        }

        /// <summary>Máquina sempre aberta: cada bot vai lá de vez em quando, se tiver dinheiro e a recarga passou.</summary>
        void UpdateContinuousSabotage(MatchDirector d, float dt)
        {
            if (!d.Sabotage.IsOpen) return;
            foreach (var bot in d.Players)
            {
                if (!bot.Profile.IsBot || !bot.IsActive || d.Sabotage.CooldownFor(bot.Id) > 0f) continue;
                if (bot.Coins < d.Config.SabotageCost) continue;
                if (!_shopTimers.TryGetValue(bot.Id, out float timer))
                    _shopTimers[bot.Id] = timer = 6f + (float)_rng.NextDouble() * 18f;
                timer -= dt;
                _shopTimers[bot.Id] = timer;
                if (timer > 0f) continue;
                _shopTimers.Remove(bot.Id);
                if (_rng.NextDouble() < 0.35) continue; // às vezes decide poupar
                TryBotPurchase(d, bot, "bot-" + bot.Id + "-" + Mathf.RoundToInt(d.Sabotage.Now * 10f));
            }
        }

        bool TryBotPurchase(MatchDirector d, PlayerState bot, string requestId)
        {
            int tier = _rng.NextDouble() < 0.45 ? 1 : 0;
            var targets = new List<PlayerState>();
            foreach (var t in d.Players)
                if (d.Sabotage.ValidateTargetCode(bot, t, tier) == SabotageBlock.None)
                    targets.Add(t);
            if (targets.Count == 0 && tier == 1)
            {
                tier = 0;
                foreach (var t in d.Players)
                    if (d.Sabotage.ValidateTargetCode(bot, t, tier) == SabotageBlock.None)
                        targets.Add(t);
            }
            if (targets.Count == 0) return false;
            // Tende a atacar quem vai à frente no placar.
            var target = targets[_rng.Next(targets.Count)];
            foreach (var t in targets)
                if (t.RoundWins > target.RoundWins && _rng.NextDouble() < 0.5) target = t;
            return d.SubmitPurchase(new PurchaseRequest { ClientRequestId = requestId, BuyerId = bot.Id, TargetId = target.Id, Tier = tier }).Accepted;
        }

        void UpdateShop(MatchDirector d, float dt)
        {
            foreach (var bot in d.Players)
            {
                if (!bot.Profile.IsBot || !bot.IsActive || d.HasShopPassed(bot.Id)) continue;
                if (d.Sabotage.PurchasesThisWindow(bot.Id) >= d.Config.MaxPurchasesPerWindow) continue;

                if (!_shopTimers.TryGetValue(bot.Id, out float timer))
                    _shopTimers[bot.Id] = timer = 1f + (float)_rng.NextDouble() * 4f;
                timer -= dt;
                _shopTimers[bot.Id] = timer;
                if (timer > 0f) continue;

                int tier = _rng.NextDouble() < 0.45 ? 1 : 0;
                var targets = new List<PlayerState>();
                foreach (var t in d.Players)
                    if (d.Sabotage.ValidateTargetCode(bot, t, tier) == SabotageBlock.None)
                        targets.Add(t);
                if (targets.Count == 0 && tier == 1)
                {
                    tier = 0;
                    foreach (var t in d.Players)
                        if (d.Sabotage.ValidateTargetCode(bot, t, tier) == SabotageBlock.None)
                            targets.Add(t);
                }

                if (targets.Count > 0 && _rng.NextDouble() < 0.7)
                {
                    // Tende a atacar quem vai à frente no placar.
                    var target = targets[_rng.Next(targets.Count)];
                    foreach (var t in targets)
                        if (t.RoundWins > target.RoundWins && _rng.NextDouble() < 0.5) target = t;
                    var result = d.SubmitPurchase(new PurchaseRequest
                    {
                        ClientRequestId = "bot-" + bot.Id + "-" + d.ShopWindowIndex,
                        BuyerId = bot.Id,
                        TargetId = target.Id,
                        Tier = tier
                    });
                    if (!result.Accepted && d.Phase == MatchPhase.Shop) d.PassShop(bot.Id);
                }
                else
                {
                    d.PassShop(bot.Id);
                }
                if (d.Phase != MatchPhase.Shop) return;
            }
        }

        /// <summary>
        /// Escolhe caçapa pela bola fantasma com menor corte, com ruído proporcional à perícia (nível Médio).
        /// Estático para poder ser usado também pelo autoplay de clientes (testes de rede).
        /// </summary>
        public static BotShot PlanShot(BilliardsSimulation sim, bool ballInHand, bool kitchenOnly, System.Random rng, float skill) =>
            PlanShot(sim, ballInHand, kitchenOnly, rng, BotDifficulty.Medium, skill);

        /// <summary>
        /// Plano de tacada por nível. Fácil: erra bastante a mira e a força, às vezes escolhe mal a caçapa ou joga
        /// directo à bola, e pousa a branca ao calhas. Médio: o bot de sempre. Difícil: mira quase exacta, só escolhe
        /// tacadas com o caminho livre (branca até à bola fantasma e bola até à caçapa), acerta a força pela distância
        /// e, com bola na mão, pousa a branca no melhor sítio que encontrar.
        /// </summary>
        public static BotShot PlanShot(BilliardsSimulation sim, bool ballInHand, bool kitchenOnly, System.Random rng, BotDifficulty level, float skill = -1f)
        {
            if (skill <= 0f) skill = SkillFor(level);
            var shot = new BotShot { Power = 0.6f };
            var target = sim.GetBall(sim.LowestBallOnTable());
            var cue = sim.Cue;
            if (target == null || cue == null)
            {
                shot.Direction = Vector2.right;
                return shot;
            }

            float r = sim.Settings.BallRadius;
            bool careful = level == BotDifficulty.Hard;
            Vector2 cuePos = cue.Position;

            if (ballInHand && !kitchenOnly && (level != BotDifficulty.Easy || rng.NextDouble() < 0.5))
            {
                // Com bola na mão: procura (à volta da bola legal) o sítio com a tacada mais fácil e o caminho livre.
                float bestPlace = float.MaxValue;
                foreach (var pocket in sim.Table.Pockets)
                {
                    Vector2 pocketDir = (pocket.Center - target.Position).normalized;
                    if (careful && !Clear(sim, target.Position, pocket.Center, target.Number, -1, r)) continue;
                    foreach (float back in careful ? new[] { 0.22f, 0.3f, 0.4f, 0.5f, 0.65f } : new[] { 0.32f, 0.22f, 0.45f, 0.6f })
                    {
                        Vector2 candidate = target.Position - pocketDir * (2f * r + back);
                        if (!sim.IsValidCuePlacement(candidate, false)) continue;
                        if (careful && !Clear(sim, candidate, target.Position - pocketDir * 2f * r, 0, target.Number, r)) continue;
                        float score = (pocket.Center - target.Position).magnitude + back * 0.5f;
                        if (score < bestPlace)
                        {
                            bestPlace = score;
                            shot.WantsPlacement = true;
                            shot.Placement = candidate;
                        }
                        if (!careful) break;
                    }
                    if (shot.WantsPlacement && !careful) break;
                }
                if (shot.WantsPlacement) cuePos = shot.Placement;
            }

            float bestScore = float.MaxValue;
            Vector2 bestAim = target.Position - cuePos;
            float bestTravel = bestAim.magnitude;
            var options = new List<(Vector2 aim, float travel, float score)>();
            foreach (var pocket in sim.Table.Pockets)
            {
                Vector2 toPocket = pocket.Center - target.Position;
                Vector2 pocketDir = toPocket.normalized;
                Vector2 ghost = target.Position - pocketDir * (2f * r);
                Vector2 aim = ghost - cuePos;
                if (aim.sqrMagnitude < 1e-6f) continue;
                float cut = Vector2.Angle(aim, pocketDir);
                if (cut > (careful ? 65f : 72f)) continue;
                // Médio evita as tacadas em que a branca bate noutra bola antes; Difícil também exige a bola livre até à caçapa.
                if (level != BotDifficulty.Easy && !Clear(sim, cuePos, ghost, 0, target.Number, r)) continue;
                if (careful && !Clear(sim, target.Position, pocket.Center, target.Number, -1, r)) continue;
                float score = cut + 12f * (aim.magnitude + toPocket.magnitude);
                options.Add((aim, aim.magnitude + toPocket.magnitude, score));
                if (score < bestScore)
                {
                    bestScore = score;
                    bestAim = aim;
                    bestTravel = aim.magnitude + toPocket.magnitude;
                }
            }
            // Fácil: às vezes escolhe uma caçapa qualquer das possíveis, ou joga directo ao centro da bola.
            if (level == BotDifficulty.Easy && options.Count > 0 && rng.NextDouble() < 0.35)
            {
                var pick = options[rng.Next(options.Count)];
                bestAim = pick.aim;
                bestTravel = pick.travel;
            }
            if (level == BotDifficulty.Easy && rng.NextDouble() < 0.15) bestAim = target.Position - cuePos;

            float noise = (float)(Gaussian(rng) * 1.6 / Math.Max(0.2f, skill));
            shot.Direction = Rotate(bestAim.normalized, noise);
            float power = 0.3f + 0.22f * bestTravel;
            float powerError = level == BotDifficulty.Easy ? 0.25f : level == BotDifficulty.Hard ? 0.03f : 0.1f;
            power += (float)(rng.NextDouble() * 2.0 - 1.0) * powerError * (level == BotDifficulty.Medium ? 0.5f : 1f) + (level == BotDifficulty.Medium ? 0.05f : 0f);
            shot.Power = Mathf.Clamp(power, 0.25f, 0.95f);
            return shot;
        }

        /// <summary>Perícia de mira de cada nível (desvio-padrão do erro = 1,6° / perícia).</summary>
        public static float SkillFor(BotDifficulty level) => level == BotDifficulty.Easy ? 0.4f : level == BotDifficulty.Hard ? 5f : 1.8f;

        /// <summary>Nenhuma outra bola no caminho de uma bola (de <paramref name="from"/> a <paramref name="to"/>).</summary>
        static bool Clear(BilliardsSimulation sim, Vector2 from, Vector2 to, int ignoreA, int ignoreB, float r)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 1e-5f) return true;
            d /= len;
            foreach (var b in sim.Balls)
            {
                if (!b.OnTable || b.Number == ignoreA || b.Number == ignoreB) continue;
                float t = Mathf.Clamp(Vector2.Dot(b.Position - from, d), 0f, len);
                if ((from + d * t - b.Position).sqrMagnitude < (2f * r - 0.002f) * (2f * r - 0.002f)) return false;
            }
            return true;
        }

        static double Gaussian(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
