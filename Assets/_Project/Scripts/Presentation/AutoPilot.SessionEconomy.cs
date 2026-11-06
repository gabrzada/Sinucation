using System.Collections;
using System.IO;
using System.Linq;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    public sealed partial class AutoPilot
    {
        bool _sessionIsolationStarted, _sessionIsolationFunded;

        IEnumerator SessionIsolationProbe(NetGame oldNet)
        {
            string dir = Arg("-fsSessionIsolation");
            Directory.CreateDirectory(dir);
            var oldMatch = oldNet.ServerDirector;
            string oldMatchId = oldMatch.MatchId;
            foreach (var player in oldMatch.Players) oldMatch.Economy.Credit(player, 240, "QA old session");
            yield return new WaitForSeconds(0.4f);
            GoalCheck(oldNet.LocalPlayer.Coins == 240, "old session wallet contains 240 before the connection error");
            _game.Network.Shutdown(); // Host failure, not a completed match or a voluntary return to the lobby.
            while (_game.Network.IsListening || _game.Network.ShutdownInProgress || NetGame.Instance != null) yield return null;
            GoalCheck(oldNet == null || !oldNet.HasSnapshot && oldNet.ServerDirector == null, "closed peer invalidates its old authoritative wallet");

            _game.HostLan((ushort)IntArg("-fsHost", 7807), "127.0.0.1", 1);
            float deadline = Time.realtimeSinceStartup + 25f;
            NetGame next = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                next = NetGame.Instance;
                if (next != null && next.HasSnapshot && next.View.InMatch && next.LocalPlayer != null) break;
                yield return null;
            }
            bool started = next != null && next.HasSnapshot && next.View.InMatch && next.LocalPlayer != null;
            GoalCheck(started, "new session starts on the same NetworkManager and LAN port");
            if (started)
            {
                GoalCheck(next.ServerDirector.MatchId != oldMatchId, "new session has another match identity");
                GoalCheck(next.ServerDirector.Players.All(p => p.Coins == 0) && next.View.Players.All(p => p.Coins == 0), "new authority and replicated wallets start at zero");
                bool replied = false, accepted = true;
                string refusal = null;
                next.PurchaseCompleted += (ok, reason, cost) => { replied = true; accepted = ok; refusal = reason; };
                int target = next.View.Players.First(p => p.Id != next.LocalPlayer.Id).Id;
                next.RequestPurchase(target, 1);
                yield return new WaitForSeconds(0.3f);
                GoalCheck(replied && !accepted && refusal == "Saldo insuficiente", "old coins cannot buy Caos in the new session");
            }
            File.WriteAllText(Path.Combine(dir, "result.json"), "{\"failures\":" + _goalFailures + "}");
            Quit("session isolation finished: failures=" + _goalFailures);
        }

        IEnumerator WalletRecoveryProbe(NetGame initial)
        {
            string dir = Arg("-fsWalletRecovery");
            Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (initial.LocalPlayer != null && initial.LocalPlayer.Coins != 240 && Time.realtimeSinceStartup < deadline) yield return null;
            int coins = initial.LocalPlayer?.Coins ?? -1;
            int playerId = initial.LocalPlayer?.Id ?? -1;
            GoalCheck(coins == 240, "client receives a positive authoritative wallet before disconnect");
            _game.Network.Shutdown();
            deadline = Time.realtimeSinceStartup + 30f;
            NetGame recovered = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                recovered = NetGame.Instance;
                if (recovered != null && recovered != initial && recovered.HasSnapshot && recovered.LocalPlayer != null && _game.Screen == AppScreen.Session) break;
                yield return null;
            }
            bool restored = recovered != null && recovered != initial && recovered.HasSnapshot && recovered.LocalPlayer != null;
            GoalCheck(restored && recovered.LocalPlayer.Id == playerId && recovered.LocalPlayer.Coins == coins,
                "reconnecting to the same live match restores the same player and 240 coins");
            File.WriteAllText(Path.Combine(dir, "result.json"), "{\"failures\":" + _goalFailures + "}");
            Quit("wallet recovery finished: failures=" + _goalFailures);
        }
    }
}
