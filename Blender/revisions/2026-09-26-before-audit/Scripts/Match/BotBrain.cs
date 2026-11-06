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

        public void Update(MatchDirector d, float dt)
        {
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

        void UpdateShot(MatchDirector d, float dt)
        {
            var shooter = d.CurrentPlayer;
            if (shooter == null || !shooter.Profile.IsBot) return;

            int key = d.ShotSequence * 16 + d.CurrentPlayerId;
            if (key != _turnKey)
            {
                _turnKey = key;
                _turnTimer = 0f;
                _turnDuration = 1.4f + (float)_rng.NextDouble() * 1.6f;
                _plannedShot = PlanShot(d.Sim, d.BallInHand, d.BallInHandKitchenOnly, _rng, Skill);
                _placed = false;
            }

            _turnTimer += dt;
            if (!_placed && _plannedShot.WantsPlacement && _turnTimer > _turnDuration * 0.35f)
            {
                d.RequestPlaceCueBall(shooter.Id, _plannedShot.Placement);
                _placed = true;
                _plannedShot = PlanShot(d.Sim, false, false, _rng, Skill);
            }

            float yaw = Mathf.Atan2(_plannedShot.Direction.y, _plannedShot.Direction.x) * Mathf.Rad2Deg;
            float windUp = Mathf.Clamp01((_turnTimer - _turnDuration * 0.55f) / (_turnDuration * 0.45f));
            Aim = new AimState { PlayerId = shooter.Id, Yaw = yaw, Pull = windUp * _plannedShot.Power };

            if (_turnTimer < _turnDuration) return;
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
        /// Escolhe caçapa pela bola fantasma com menor corte, com ruído proporcional à perícia.
        /// Estático para poder ser usado também pelo autoplay de clientes (testes de rede).
        /// </summary>
        public static BotShot PlanShot(BilliardsSimulation sim, bool ballInHand, bool kitchenOnly, System.Random rng, float skill)
        {
            var shot = new BotShot { Power = 0.6f };
            var target = sim.GetBall(sim.LowestBallOnTable());
            var cue = sim.Cue;
            if (target == null || cue == null)
            {
                shot.Direction = Vector2.right;
                return shot;
            }

            float r = sim.Settings.BallRadius;
            Vector2 cuePos = cue.Position;
            float bestScore = float.MaxValue;
            Vector2 bestAim = target.Position - cuePos;
            float bestTravel = bestAim.magnitude;
            Vector2 bestPocketDir = bestAim.normalized;

            foreach (var pocket in sim.Table.Pockets)
            {
                Vector2 toPocket = pocket.Center - target.Position;
                Vector2 pocketDir = toPocket.normalized;
                Vector2 ghost = target.Position - pocketDir * (2f * r);
                Vector2 aim = ghost - cuePos;
                if (aim.sqrMagnitude < 1e-6f) continue;
                float cut = Vector2.Angle(aim, pocketDir);
                if (cut > 72f) continue;
                float score = cut + 12f * (aim.magnitude + toPocket.magnitude);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestAim = aim;
                    bestTravel = aim.magnitude + toPocket.magnitude;
                    bestPocketDir = pocketDir;
                }
            }

            if (ballInHand && !kitchenOnly)
            {
                foreach (float back in new[] { 0.32f, 0.22f, 0.45f, 0.6f })
                {
                    Vector2 candidate = target.Position - bestPocketDir * (2f * r + back);
                    if (!sim.IsValidCuePlacement(candidate, false)) continue;
                    shot.WantsPlacement = true;
                    shot.Placement = candidate;
                    break;
                }
            }

            float noise = (float)(Gaussian(rng) * 1.6 / Math.Max(0.2f, skill));
            shot.Direction = Rotate(bestAim.normalized, noise);
            shot.Power = Mathf.Clamp(0.3f + 0.22f * bestTravel + (float)rng.NextDouble() * 0.1f, 0.3f, 0.92f);
            return shot;
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
