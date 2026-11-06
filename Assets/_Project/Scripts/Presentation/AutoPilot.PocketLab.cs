using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FriendSlop.Billiards;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Laboratório das bocas das caçapas (-fsPractice 1 -fsPocketLab &lt;pasta&gt;): no turno do jogador local
    /// lança bolas lentas para a boca de uma caçapa de canto e de uma lateral (os casos que ficavam "presas na boca"),
    /// deixa a física autoritativa e a apresentação correrem em tempo real e fotografa cada caso de dois pontos de vista.
    /// </summary>
    public sealed partial class AutoPilot
    {
        bool _pocketLabStarted;

        void UpdatePocketLab(NetGame net, MatchView view)
        {
            if (!net.IsLocalHost) return;
            // Turnos longos: o laboratório corre todo dentro do turno do jogador local.
            net.ServerDirector.Config.TurnTimeLimit = 900f;
            if (_pocketLabStarted || net.LocalPlayer == null || view.CurrentPlayer != net.LocalPlayer.Id || view.Phase != MatchPhase.AwaitShot) return;
            _pocketLabStarted = true;
            StartCoroutine(PocketLab(net));
        }

        static float PowerForSpeed(BilliardsPhysicsSettings s, float speed)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (s.CueSpeedForPower(mid) < speed) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        void CapturePocketView(string path, Vector3 eye, Vector3 target)
        {
            var camera = _game.Rig.Camera;
            var t = camera.transform;
            Vector3 position = t.position;
            Quaternion rotation = t.rotation;
            float fov = camera.fieldOfView;
            t.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            camera.fieldOfView = 50f;
            try { CaptureGoalCamera(path); }
            finally
            {
                t.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
        }

        IEnumerator PocketLab(NetGame net)
        {
            string directory = Arg("-fsPocketLab");
            Directory.CreateDirectory(directory);
            var sim = net.ServerDirector.Sim;
            var s = sim.Settings;
            var table = sim.Table;
            NetPlayer.Local.TeleportTo(new Vector3(-table.HalfLength - 0.9f, 0f, 0f), 90f);
            yield return new WaitForSeconds(1f);

            // (nome, caçapa, ângulo de entrada em graus, desvio lateral na garganta em m, velocidade inicial em m/s)
            var cases = new List<(string, int, float, float, float)>
            {
                ("corner_slow_past_throat", 2, -20f, 0.005f, 0.512f),
                ("corner_slow_mouth", 2, -40f, -0.03f, 0.497f),
                ("side_slow_lip", 1, -40f, -0.03f, 0.465f),
                ("side_slow_nose", 1, -25f, -0.01f, 0.477f),
                ("corner_medium", 2, 0f, 0f, 1.5f),
                ("side_medium", 1, 0f, 0f, 1.5f),
            };
            var report = new List<string>();
            foreach (var (name, pocketIndex, angle, offset, speed) in cases)
            {
                var pocket = table.Pockets[pocketIndex];
                Vector2 axis = pocket.IsCorner ? new Vector2(Mathf.Sign(pocket.Center.x), Mathf.Sign(pocket.Center.y)).normalized : new Vector2(0f, Mathf.Sign(pocket.Center.y));
                float a = angle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(axis.x * Mathf.Cos(a) - axis.y * Mathf.Sin(a), axis.x * Mathf.Sin(a) + axis.y * Mathf.Cos(a));
                Vector2 mouth = (pocket.ThroatA + pocket.ThroatB) * 0.5f + new Vector2(-dir.y, dir.x) * offset;
                Vector2 start = mouth - dir * 0.35f;

                var balls = new List<KeyValuePair<int, Vector2>> { new KeyValuePair<int, Vector2>(0, start) };
                for (int n = 1; n <= sim.FinalBall; n++)
                    balls.Add(new KeyValuePair<int, Vector2>(n, new Vector2(-0.75f + n * 0.075f, -0.35f)));
                sim.SetupCustom(balls, sim.FinalBall);
                yield return new WaitForSeconds(0.4f);

                Vector3 hole = TableView.ToWorld(pocket.Center, 0f);
                Vector3 inward = new Vector3(-axis.x, 0f, -axis.y);
                sim.StrikeCue(ShotParameters.FromInput(dir, PowerForSpeed(s, speed), 0f, 0f), new ShotRecord());
                float elapsed = 0f;
                while (sim.IsSimulating && elapsed < 8f)
                {
                    float dt = Time.deltaTime;
                    sim.Advance(dt);
                    elapsed += dt;
                    yield return null;
                }
                bool stillMoving = sim.IsSimulating;
                // Deixa acabar a animação de queda e fotografa.
                yield return new WaitForSeconds(1.3f);
                var cue = sim.Cue;
                float fromCenter = (cue.Position - pocket.Center).magnitude;
                string line = name + ": " + (cue.Pocketed ? "CAIU na caçapa " + cue.PocketIndex : "ficou em (" + cue.Position.x.ToString("0.0000", CultureInfo.InvariantCulture) + ", " +
                    cue.Position.y.ToString("0.0000", CultureInfo.InvariantCulture) + "), " + (fromCenter * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " cm do centro da caçapa") +
                    ", física " + sim.SimulatedTime.ToString("0.00", CultureInfo.InvariantCulture) + " s" + (stillMoving ? " (AINDA A SIMULAR)" : "");
                report.Add(line);
                Log("[PocketLab] " + line);
                CapturePocketView(Path.Combine(directory, name + "_eye.png"), hole + inward * 0.75f + Vector3.up * 0.42f, hole);
                yield return new WaitForEndOfFrame();
                CapturePocketView(Path.Combine(directory, name + "_close.png"), hole + inward * 0.22f + Vector3.up * 0.2f, hole);
                yield return new WaitForEndOfFrame();
            }
            File.WriteAllLines(Path.Combine(directory, "result.txt"), report);
            Quit("pocket lab finished");
        }
    }
}
