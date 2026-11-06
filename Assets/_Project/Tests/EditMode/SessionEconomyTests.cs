using System.Collections.Generic;
using System.Reflection;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using FriendSlop.Presentation;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class SessionEconomyTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ClosedNetworkSessionCannotExposeOrReuseItsOldWallet()
        {
            var go = new GameObject("Session economy regression");
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            var catalog = ModifierCatalog.CreateDefaults();
            var previousConfig = NetGame.SharedConfig;
            try
            {
                NetGame.SharedConfig = config;
                go.AddComponent<NetworkObject>();
                var net = go.AddComponent<NetGame>();
                net.OnNetworkSpawn();
                var profiles = new[] { new PlayerProfile("A", Archetype.Circle, Color.white), new PlayerProfile("B", Archetype.Square, Color.red) };
                var oldMatch = new MatchDirector(config, catalog, profiles, 3);
                oldMatch.Economy.Credit(oldMatch.GetPlayer(0), 240, "previous session");
                typeof(NetGame).GetField("_director", Fields).SetValue(net, oldMatch);
                typeof(NetGame).GetField("<HasSnapshot>k__BackingField", Fields).SetValue(net, true);
                net.View.InMatch = true;
                net.View.ShopOpen = true;
                net.View.Players.Add(new PlayerView { Id = 0, ClientId = 0, Coins = 240 });

                net.OnNetworkDespawn();
                Assert.IsNull(net.ServerDirector, "A stopped session must release its authoritative economy.");
                Assert.IsFalse(net.HasSnapshot);
                Assert.IsFalse(net.View.ShopOpen);
                Assert.IsEmpty(net.View.Players);
                net.OnNetworkSpawn();
                Assert.IsFalse(net.HasSnapshot, "A reused network object must wait for the NEW server snapshot.");
                Assert.IsNull(net.LocalPlayer);
                net.OnNetworkDespawn();
            }
            finally
            {
                NetGame.SharedConfig = previousConfig;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
                foreach (var def in catalog) Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void BindingAnotherSessionShowsItsBalanceImmediately()
        {
            var go = new GameObject("Wallet presentation regression");
            try
            {
                go.AddComponent<NetworkObject>();
                var net = go.AddComponent<NetGame>();
                var hud = go.AddComponent<HudView>();
                typeof(HudView).GetField("_walletShown", Fields).SetValue(hud, 240f);
                hud.Bind(net);
                int shown = (int)typeof(HudView).GetMethod("WalletDisplay", Fields).Invoke(hud, new object[] { 0 });
                Assert.AreEqual(0, shown, "The previous wallet must not animate down as if it belonged to the new session.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NewMatchWithSameProfilesCannotSpendAbortedMatchCoins()
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            var catalog = ModifierCatalog.CreateDefaults();
            try
            {
                var profiles = new List<PlayerProfile> { new PlayerProfile("A", Archetype.Circle, Color.white), new PlayerProfile("B", Archetype.Square, Color.red) };
                var oldMatch = new MatchDirector(config, catalog, profiles, 4);
                oldMatch.Start();
                oldMatch.Economy.Credit(oldMatch.GetPlayer(0), 240, "old session");
                var newMatch = new MatchDirector(config, catalog, profiles, 5);
                newMatch.Start();
                Assert.AreNotEqual(oldMatch.MatchId, newMatch.MatchId);
                Assert.AreEqual(0, newMatch.GetPlayer(0).Coins);
                var purchase = newMatch.SubmitPurchase(new PurchaseRequest { BuyerId = 0, TargetId = 1, Tier = 1, ClientRequestId = "new-session" });
                Assert.IsFalse(purchase.Accepted);
                Assert.AreEqual("Saldo insuficiente", purchase.Reason);
                Assert.AreEqual(240, oldMatch.GetPlayer(0).Coins, "Keeping a live match for reconnection must not copy its wallet into another match.");
            }
            finally { Object.DestroyImmediate(config); foreach (var def in catalog) Object.DestroyImmediate(def); }
        }
    }
}
