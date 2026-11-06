using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Público decorativo na varanda (concept da arena). Reage aos eventos replicados: festeja vitórias,
    /// põe as mãos na cabeça no disparo e ri-se das sabotagens. Local a cada cliente; nunca afecta o jogo.
    /// </summary>
    public sealed class SpectatorCrowd : MonoBehaviour
    {
        readonly List<AvatarVisual> _crowd = new List<AvatarVisual>();
        readonly List<float> _cheer = new List<float>();
        // Reacções escalonadas: cada fã reage com um pequeno atraso próprio (não todos no mesmo frame).
        readonly List<float> _emoteDelay = new List<float>();
        readonly List<int> _emoteId = new List<int>();
        NetGame _bound;

        public void Build(IReadOnlyList<Vector3> spots, int count)
        {
            var rng = new System.Random(77);
            string[] names = { "Fã" };
            for (int i = 0; i < Mathf.Min(count, spots.Count); i++)
            {
                var holder = new GameObject("Spectator" + i).transform;
                holder.SetParent(transform, false);
                holder.position = spots[i];
                Vector3 look = -spots[i];
                look.y = 0f;
                holder.rotation = Quaternion.LookRotation(look.normalized);
                var profile = new PlayerProfile(names[0], (Archetype)rng.Next(3), ArtPalette.Heads[rng.Next(ArtPalette.Heads.Length)])
                {
                    HatId = rng.NextDouble() < 0.7 ? 0 : rng.Next(CosmeticIds.HatCount),
                    AccessoryId = rng.Next(CosmeticIds.AccessoryCount)
                };
                var avatar = AvatarVisual.Create(holder, profile);
                _crowd.Add(avatar);
                _cheer.Add((float)rng.NextDouble() * 3f);
                _emoteDelay.Add(-1f);
                _emoteId.Add(0);
            }
        }

        void Bind(NetGame net)
        {
            _bound = net;
            net.RoundWonEvent += id => Wave(AvatarPose.Celebrate, 2.5f);
            net.MatchEnded += () => Wave(AvatarPose.Celebrate, 6f);
            net.RiskResolved += (id, outcome, def, eliminated) => Emote(outcome == RiskOutcome.Discharge ? 2 : 0);
            net.SabotageApplied += (b, t, d) => Emote(0);
            net.ShotResolved += (shooter, foul, keep, won) =>
            {
                if (foul != FriendSlop.Billiards.FoulType.None) Emote(2, 0.35f);
                else if (keep) Emote(1, 0.3f);
            };
        }

        void Wave(AvatarPose pose, float seconds)
        {
            for (int i = 0; i < _cheer.Count; i++) _cheer[i] = seconds + Random.Range(0f, 0.6f);
        }

        void Emote(int emote, float chance = 0.7f)
        {
            for (int i = 0; i < _crowd.Count; i++)
            {
                if (Random.value >= chance) continue;
                _emoteDelay[i] = Random.Range(0.05f, 0.6f);
                _emoteId[i] = emote;
            }
        }

        void Update()
        {
            var net = NetGame.Instance;
            if (net != null && net != _bound && net.IsSpawned) Bind(net);

            float dt = Time.deltaTime;
            var view = net != null && net.HasSnapshot ? net.View : null;
            Vector3 focus = NpcAvatars.ActionFocus(view, -1) ?? new Vector3(0f, TableView.TopY, 0f);
            for (int i = 0; i < _crowd.Count; i++)
            {
                if (_emoteDelay[i] >= 0f)
                {
                    _emoteDelay[i] -= dt;
                    if (_emoteDelay[i] < 0f) _crowd[i].PlayEmote(_emoteId[i]);
                }
                _crowd[i].LookTarget = focus;
                _cheer[i] -= dt;
                var pose = _cheer[i] > 0f ? AvatarPose.Celebrate : AvatarPose.Idle;
                _crowd[i].Animate(dt, pose, 10f, false);
            }
        }
    }
}
