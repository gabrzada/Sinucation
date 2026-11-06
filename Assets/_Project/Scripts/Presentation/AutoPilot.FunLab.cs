using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// -fsFunLab victim|shooter|watcher: laboratório do modo de brincadeira com três processos (anfitrião = alvo, primeiro
    /// cliente = vencedor que dispara, segundo cliente = observador). O anfitrião elimina toda a gente menos o vencedor;
    /// cada um põe-se num sítio fixo e o vencedor dispara contra o alvo e o observador. Mede em todos os processos para
    /// onde aponta o cano do revólver em relação ao corpo do vencedor e, durante cada ragdoll, saltos da câmara do
    /// atingido, se o próprio corpo lhe aparece à frente e saltos da bacia/posição vistos por cada um. -fsFunLegacy repõe a
    /// mira antiga (comparação antes/depois na mesma build).
    /// </summary>
    public sealed partial class AutoPilot
    {
        static readonly Vector3 LabShooterAt = new Vector3(-1.2f, 0.05f, -2.3f);
        static readonly Vector3 LabVictimAt = new Vector3(1.0f, 0.05f, -2.3f);
        static readonly Vector3 LabWatcherAt = new Vector3(0.4f, 0.05f, -3.9f);

        sealed class LabTrack
        {
            public int Target;
            public float Start;
            public Vector3 LastHips, LastBody, LastCam;
            public float MaxHipsJump, MaxBodyJump, MaxCamJump, MaxCamHead;
            public float HipsJumpAt, CamJumpAt;
            public int VisibleFrames, Frames;
            public float NextSample;
            public bool Init;
            public StringBuilder Samples = new StringBuilder();
        }

        float _labOver = -1f, _labMatchAt = -1f, _labGunLogAt;
        bool _labForfeited, _labHooked;
        readonly List<LabTrack> _labTracks = new List<LabTrack>();
        readonly List<KeyValuePair<float, string>> _labShots = new List<KeyValuePair<float, string>>();

        void UpdateFunLab(NetGame net, MatchView view)
        {
            string role = Arg("-fsFunLab");
            FunModeController.Legacy = Has("-fsFunLegacy");
            if (!_labHooked)
            {
                _labHooked = true;
                net.FunShot += (shooter, target, origin, hit) => OnLabShot(net, role, shooter, target);
                // -fsCameraMotion 0|1|2: "Movimento da câmara" só em memória (não grava as definições do jogador).
                if (Has("-fsCameraMotion")) _game.Settings.CameraMotion = Mathf.Clamp(IntArg("-fsCameraMotion", 1), 0, 2);
                Log("funlab: papel=" + role + " comportamento=" + (FunModeController.Legacy ? "ANTIGO" : "novo") + " movimento_câmara=" + _game.Settings.CameraMotion);
            }
            if (_labMatchAt < 0f) _labMatchAt = _time;

            // Anfitrião: deixa só o vencedor (o cliente com o menor id que não é o anfitrião).
            if (net.IsServer && !_labForfeited && view.Phase == MatchPhase.AwaitShot && _time - _labMatchAt > 3f)
            {
                var d = net.ServerDirector;
                var winner = view.Players.Where(p => !p.Profile.IsBot && p.ClientId != view.HostClientId).OrderBy(p => p.ClientId).FirstOrDefault();
                if (d != null && winner != null)
                {
                    _labForfeited = true;
                    foreach (var p in d.Players.ToList())
                        if (p.Id != winner.Id) d.Forfeit(p.Id, "laboratório do modo livre");
                    Log("funlab: anfitrião deixou só o vencedor " + winner.Profile.Name + " (jogador " + winner.Id + ")");
                }
            }

            for (int i = _labShots.Count - 1; i >= 0; i--)
                if (_time >= _labShots[i].Key) { Capture(_labShots[i].Value); _labShots.RemoveAt(i); }

            if (view.Phase != MatchPhase.MatchOver || view.MatchWinner < 0) return;
            if (_labOver < 0f) _labOver = _time;
            float t = _time - _labOver;
            var local = NetPlayer.Local;
            var shooter = view.GetPlayer(view.MatchWinner);
            var victim = view.Players.FirstOrDefault(p => p.ClientId == view.HostClientId);
            var watcher = view.Players.FirstOrDefault(p => p.Id != view.MatchWinner && p.ClientId != view.HostClientId && !p.Profile.IsBot);

            if (t > 3f && _game.Hud.ResultsOpen && _shots.Add("lab_close")) _game.Hud.HandleTab();
            if (t > 5f && local != null && _shots.Add("lab_place"))
            {
                Vector3 at = role == "shooter" ? LabShooterAt : role == "victim" ? LabVictimAt : LabWatcherAt;
                Vector3 look = role == "watcher" ? LabShooterAt : role == "victim" ? LabShooterAt : LabVictimAt;
                float yaw = Mathf.Atan2(look.x - at.x, look.z - at.z) * Mathf.Rad2Deg;
                local.TeleportTo(at, yaw);
                local.SetLook(yaw, role == "watcher" ? 4f : 2f);
                Log("funlab: " + role + " em " + at.ToString("0.00") + " virado a " + yaw.ToString("0") + "°");
            }
            if (t > 7.5f && t < 9.5f && _time >= _labGunLogAt)
            {
                _labGunLogAt = _time + 0.5f;
                LogGun(net, view.MatchWinner, role);
            }
            if (t > 8f && _shots.Add("lab_gun_shot")) Capture(role + "_revolver");

            if (role == "shooter" && local != null)
            {
                if (t > 10f && _shots.Add("lab_fire1") && victim != null) LabFire(net, local, victim.Id);
                if (t > 16f && _shots.Add("lab_fire2") && watcher != null) LabFire(net, local, watcher.Id);
                if (t > 22f && _shots.Add("lab_fire3") && victim != null) LabFire(net, local, victim.Id);
            }
        }

        void LogGun(NetGame net, int winnerId, string role)
        {
            var avatar = AvatarLocator.Find(net, winnerId);
            var p = net.View.GetPlayer(winnerId);
            var np = p != null ? NetPlayer.ForClient(p.ClientId) : null;
            if (avatar == null || np == null) return;
            if (!avatar.TryGetHeldGun(out var pos, out var rot))
            {
                Log("funlab[" + role + "] revólver: o vencedor não está a segurá-lo");
                return;
            }
            Vector3 barrel = Vector3.ProjectOnPlane(rot * Vector3.forward, Vector3.up).normalized;
            Vector3 body = Vector3.ProjectOnPlane(np.transform.forward, Vector3.up).normalized;
            float off = Vector3.SignedAngle(body, barrel, Vector3.up);
            Log("funlab[" + role + "] revólver: cano vs corpo do vencedor = " + off.ToString("+0;-0", CultureInfo.InvariantCulture) + "° (yaw corpo " +
                np.transform.eulerAngles.y.ToString("0") + "°, Yaw " + np.Yaw.ToString("0") + "°, BodyYaw " + np.BodyYaw.ToString("0") + "°, dono=" + np.IsOwner + ")");
        }

        void LabFire(NetGame net, NetPlayer local, int target)
        {
            var avatar = AvatarLocator.Find(net, target);
            if (avatar == null) return;
            Vector3 center = avatar.transform.position + Vector3.up * 0.7f;
            Vector3 eye = local.EyePosition;
            Vector3 dir = center - eye;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
            local.SetLook(yaw, pitch);
            net.RequestFunShot(eye, dir, target, center);
            Log("funlab: vencedor dispara contra o jogador " + target);
        }

        void OnLabShot(NetGame net, string role, int shooter, int target)
        {
            Log("funlab[" + role + "] tiro " + shooter + " -> " + target + " t=" + (_time - _labOver).ToString("0.00", CultureInfo.InvariantCulture));
            if (target < 0) return;
            _labTracks.Add(new LabTrack { Target = target, Start = _time });
            bool me = net.LocalPlayer != null && net.LocalPlayer.Id == target;
            string who = me ? "eu" : "j" + target;
            foreach (float k in me ? new[] { 0.15f, 0.5f, 0.9f, 1.15f, 1.35f, 1.7f } : new[] { 0.4f, 1.0f, 1.15f, 1.35f, 1.8f })
                _labShots.Add(new KeyValuePair<float, string>(_time + k, role + "_atingido_" + who + "_" + _labTracks.Count + "_" + k.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        /// <summary>Por frame, durante 2,5 s depois de cada tiro: saltos da bacia/posição do alvo e, no atingido, da câmara.</summary>
        void UpdateLabTracks(NetGame net)
        {
            for (int i = _labTracks.Count - 1; i >= 0; i--)
            {
                var tr = _labTracks[i];
                var avatar = AvatarLocator.Find(net, tr.Target);
                if (avatar == null || avatar.Hips == null) { _labTracks.RemoveAt(i); continue; }
                bool me = net.LocalPlayer != null && net.LocalPlayer.Id == tr.Target;
                float t = _time - tr.Start;
                Vector3 hips = avatar.Hips.position, body = avatar.transform.position;
                Vector3 cam = _game.Rig.Camera.transform.position;
                if (tr.Init)
                {
                    float hj = Vector3.Distance(hips, tr.LastHips), bj = Vector3.Distance(body, tr.LastBody), cj = Vector3.Distance(cam, tr.LastCam);
                    if (hj > tr.MaxHipsJump) { tr.MaxHipsJump = hj; tr.HipsJumpAt = t; }
                    tr.MaxBodyJump = Mathf.Max(tr.MaxBodyJump, bj);
                    if (me && cj > tr.MaxCamJump) { tr.MaxCamJump = cj; tr.CamJumpAt = t; }
                }
                tr.Init = true;
                tr.LastHips = hips; tr.LastBody = body; tr.LastCam = cam;
                if (me)
                {
                    tr.Frames++;
                    if (!avatar.HiddenFromCamera) tr.VisibleFrames++;
                    tr.MaxCamHead = Mathf.Max(tr.MaxCamHead, Vector3.Distance(cam, avatar.Head.position));
                }
                if (_time >= tr.NextSample)
                {
                    tr.NextSample = _time + 0.1f;
                    tr.Samples.Append(' ').Append(t.ToString("0.0", CultureInfo.InvariantCulture)).Append(":bacia_y=").Append(hips.y.ToString("0.00", CultureInfo.InvariantCulture))
                      .Append(avatar.InRagdoll ? "R" : avatar.Recovering ? "L" : "");
                    if (me) tr.Samples.Append(avatar.HiddenFromCamera ? "" : "[corpo visível]").Append(" cam_y=").Append(cam.y.ToString("0.00", CultureInfo.InvariantCulture));
                }
                if (t < 2.5f) continue;
                Log("funlab alvo=" + tr.Target + (me ? " (EU)" : "") + ": salto máx. bacia/frame=" + tr.MaxHipsJump.ToString("0.000", CultureInfo.InvariantCulture) + " m em t=" +
                    tr.HipsJumpAt.ToString("0.00", CultureInfo.InvariantCulture) + " s; posição/frame=" + tr.MaxBodyJump.ToString("0.000", CultureInfo.InvariantCulture) + " m" +
                    (me ? "; câmara/frame=" + tr.MaxCamJump.ToString("0.000", CultureInfo.InvariantCulture) + " m em t=" + tr.CamJumpAt.ToString("0.00", CultureInfo.InvariantCulture) +
                          " s; corpo próprio visível em " + tr.VisibleFrames + "/" + tr.Frames + " frames; câmara↔cabeça máx. " + tr.MaxCamHead.ToString("0.00", CultureInfo.InvariantCulture) + " m" : ""));
                Log("funlab alvo=" + tr.Target + " amostras:" + tr.Samples);
                _labTracks.RemoveAt(i);
            }
        }
    }
}
