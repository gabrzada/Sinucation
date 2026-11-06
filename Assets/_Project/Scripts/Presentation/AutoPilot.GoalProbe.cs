using System.Collections;
using System.IO;
using System.Linq;
using FriendSlop.Core.Modifiers;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FriendSlop.Presentation
{
    public sealed partial class AutoPilot
    {
        bool _goalProbeStarted;
        int _goalFailures;

        void GoalCheck(bool ok, string message)
        {
            if (!ok) _goalFailures++;
            Log("[GoalQA] " + (ok ? "PASS " : "FAIL ") + message);
        }

        void CaptureGoalCamera(string path)
        {
            // Hidden/headless QA windows have no capturable swapchain; render the real camera offscreen.
            var camera = _game.Rig.Camera;
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                Destroy(texture);
            }
        }

        IEnumerator GoalProbe(NetGame net)
        {
            string directory = Arg("-fsGoalProbe");
            Directory.CreateDirectory(directory);
            var d = net.ServerDirector;
            var me = net.LocalPlayer;
            var buyer = d.GetPlayer(me.Id);
            var target = d.Players.First(p => p.Id != me.Id);
            buyer.Coins = d.Sabotage.CostFor(buyer, target, 1);
            yield return new WaitForSeconds(0.3f);
            bool replied = false, accepted = false;
            net.PurchaseCompleted += (ok, reason, cost) => { replied = true; accepted = ok; Log("[GoalQA] purchase reply " + ok + " cost=" + cost + " reason=" + reason); };
            net.RequestPurchase(target.Id, 1);
            yield return new WaitForSeconds(0.4f);
            GoalCheck(replied && accepted && buyer.Coins == 0 && target.Modifiers.Active.Count == 1, "Caos RPC with exact balance debits and applies");

            if (net.View.BallInHand)
            {
                _game.Shot.DebugPlace(net.Mirror.Table.HeadSpot);
                yield return new WaitForSeconds(0.5f);
            }
            _game.Hud.PanelCursor = false;
            _game.Interaction.Close();
            _game.Shot.DebugAim(Vector2.right, 0f);
            yield return new WaitForSeconds(1f);
            var shot = _game.Shot;
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
                yield return null;
                yield return null;
                GoalCheck(shot.Pulling, "mouse press begins pull");
                Vector2 initial = shot.AimDirection;
                for (int i = 0; i < 12; i++)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1, delta = new Vector2(8f, -8f) });
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
                    yield return null;
                }
                GoalCheck(Vector2.Angle(initial, shot.AimDirection) > 0.5f && shot.Pull01 > 0.05f, "horizontal aim changes while power is pulled");
                GoalCheck(shot.Spin.x > 0f && shot.Spin.y > 0f && NetPlayer.Local.MovementLocked, "W+D applies diagonal spin while walking stays locked");
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 3 });
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return null;
                GoalCheck(!shot.Pulling && shot.Pull01 == 0f, "right-click cancels without shooting");
                InputSystem.QueueStateEvent(mouse, new MouseState());
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
                yield return null;
                yield return null;
                int beforePause = d.ShotSequence;
                _game.TogglePause();
                yield return null;
                yield return null;
                GoalCheck(!shot.Pulling && shot.Pull01 == 0f, "pause cancels the pull");
                InputSystem.QueueStateEvent(mouse, new MouseState());
                _game.TogglePause();
                yield return null;
                yield return null;
                GoalCheck(d.ShotSequence == beforePause, "releasing during pause does not shoot on resume");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }

            foreach (string id in new[] { "neutral", ModifierCatalog.TunnelVision, ModifierCatalog.Myopia, ModifierCatalog.ColorBlindness, "restored" })
            {
                buyer.Modifiers.Clear();
                if (id != "neutral" && id != "restored")
                    buyer.Modifiers.Apply(NetGame.SharedCatalog.First(def => def.Id == id), me.Id, target.Id, "qa", d.RoundIndex, 900, 2);
                yield return new WaitForSeconds(1.5f);
                var profile = _game.Rig.ActiveProfile;
                GoalCheck(id == ModifierCatalog.TunnelVision ? profile.Vignette > 0f : id == ModifierCatalog.Myopia ? profile.Myopia > 0f :
                    id == ModifierCatalog.ColorBlindness ? profile.Deuteranopia > 0f : profile.IsNeutral, "replicated perception " + id);
                CaptureGoalCamera(Path.Combine(directory, id + ".png"));
                yield return new WaitForEndOfFrame();
            }

            // An actor teleported above the tabletop must be moved outside; a prop must land on it.
            NetPlayer.Local.MovementLocked = false;
            NetPlayer.Local.LookLocked = false;
            // Exit the aiming context using the actual Q input.
            var cancelKeyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(cancelKeyboard, new KeyboardState(Key.Q));
            yield return null;
            yield return null;
            InputSystem.RemoveDevice(cancelKeyboard);
            NetPlayer.Local.TeleportTo(new Vector3(0f, TableView.TopY + 0.2f, 0f), 0f);
            yield return new WaitForSeconds(0.5f);
            Vector3 actor = NetPlayer.Local.transform.position;
            GoalCheck(Mathf.Abs(actor.x) > net.Mirror.Table.HalfLength || Mathf.Abs(actor.z) > net.Mirror.Table.HalfWidth, "player cannot remain over table");
            var prop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            prop.layer = GameplayCollision.Throwable;
            prop.transform.position = new Vector3(0f, TableView.TopY + 0.8f, 0f);
            prop.transform.localScale = Vector3.one * 0.1f;
            prop.AddComponent<Rigidbody>();
            Vector2 cueBefore = net.Mirror.Cue.Position;
            yield return new WaitForSeconds(1.5f);
            GoalCheck(prop.transform.position.y > TableView.TopY && prop.transform.position.y < TableView.TopY + 0.12f, "throwable lands on table through player barrier");
            GoalCheck((net.Mirror.Cue.Position - cueBefore).sqrMagnitude < 0.000001f, "throwable does not move billiard balls");
            Destroy(prop);

            _game.Shot.DebugAim(Vector2.right, 0f);
            yield return null;
            var releaseMouse = InputSystem.AddDevice<Mouse>();
            InputSystem.QueueStateEvent(releaseMouse, new MouseState { buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(releaseMouse, new MouseState { buttons = 1, delta = new Vector2(0f, -100f) });
            yield return null;
            yield return null;
            int sequence = d.ShotSequence;
            InputSystem.QueueStateEvent(releaseMouse, new MouseState { delta = new Vector2(120f, 0f) });
            yield return null;
            yield return null;
            Vector2 releasedDirection = shot.AimDirection;
            float shotDeadline = Time.realtimeSinceStartup + 1f;
            while (d.Phase != FriendSlop.Match.MatchPhase.Simulating && Time.realtimeSinceStartup < shotDeadline) yield return null;
            GoalCheck(d.ShotSequence == sequence + 1 && Vector2.Angle(releasedDirection, Vector2.right) > 1f &&
                Vector2.Angle(d.Sim.Cue.Velocity, releasedDirection) < 0.5f, "release uses direction from the final input frame");
            InputSystem.RemoveDevice(releaseMouse);

            _game.Leave();
            while (_game.Network.IsListening || _game.Session.Leaving) yield return null;
            _game.Settings.HatId = 6;
            _game.Hud.DebugShow("customize");
            for (int archetype = 0; archetype < 3; archetype++)
            {
                _game.Settings.Archetype = archetype;
                yield return new WaitForSeconds(1f);
                CaptureGoalCamera(Path.Combine(directory, "straw_hat_" + archetype + ".png"));
                yield return new WaitForEndOfFrame();
            }
            File.WriteAllText(Path.Combine(directory, "result.json"), "{\"failures\":" + _goalFailures + "}");
            Quit("goal probe finished: failures=" + _goalFailures);
        }
    }
}
