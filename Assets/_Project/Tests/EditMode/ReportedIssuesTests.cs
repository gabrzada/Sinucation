using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>Relatos de 2026-11-01: volume do vídeo da jukebox e levantar do ragdoll no modo de brincadeira.</summary>
    public sealed class ReportedIssuesTests
    {
        /// <summary>A rádio da jukebox: AudioSource 55 % 3D, linear de 4 a 30 m (Jukebox.cs).</summary>
        static float RadioGain(float d) => 0.45f + 0.55f * Mathf.Clamp01((30f - d) / 26f);

        [Test]
        public void VideoDistanceGainMatchesTheJukeboxRadio()
        {
            foreach (float d in new[] { 0.5f, 2f, 4f, 5f, 8f, 12f, 20f, 40f })
                Assert.That(OnlineMusicPlayer.DistanceGain(d, false), Is.EqualTo(RadioGain(d)).Within(1e-4f), "a " + d + " m");
            // Junto à mesa (~5 m da jukebox): antes 0,50 (0,28 com paredes); agora quase cheio, como a rádio.
            Assert.That(OnlineMusicPlayer.DistanceGain(5f, false, true), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(OnlineMusicPlayer.DistanceGain(5f, false), Is.GreaterThan(0.95f));
            Assert.That(OnlineMusicPlayer.DistanceGain(5f, true), Is.EqualTo(OnlineMusicPlayer.DistanceGain(5f, false) * OnlineMusicPlayer.OccludedFactor).Within(1e-4f));
            Assert.That(OnlineMusicPlayer.DistanceGain(100f, true), Is.GreaterThan(0.3f), "nunca fica quase mudo dentro da sala");
        }

        [Test]
        public void RagdollGetsUpWhereItFellAfterTheOwnerMovesThere()
        {
            Assert.That(HipsJumpWhenGettingUp(false), Is.LessThan(0.05f), "a bacia não pode saltar quando o dono se muda para onde caiu");
            // O comportamento antigo saltava o deslocamento outra vez (reproduz o relato).
            Assert.That(HipsJumpWhenGettingUp(true), Is.GreaterThan(0.25f));
        }

        /// <summary>Tiro, ragdoll até ao fim e, como o dono faz (NetPlayer), o corpo passa para onde a bacia caiu.</summary>
        static float HipsJumpWhenGettingUp(bool legacy)
        {
            var previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            bool previousLegacy = FunModeController.Legacy;
            FunModeController.Legacy = legacy;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            var holder = new GameObject("RagdollActor");
            try
            {
                var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", Archetype.Square, Color.cyan));
                const float dt = 1f / 60f;
                for (int i = 0; i < 30; i++) avatar.Animate(dt, AvatarPose.Idle, 0f, false);
                avatar.RagdollEnded += at => holder.transform.position = new Vector3(at.x, 0f, at.z);
                avatar.Ragdoll(new Vector3(3.4f, 1.1f, 0f), 1.1f);
                Vector3 last = avatar.Hips.position;
                float jump = 0f;
                for (int i = 0; i < 150; i++)
                {
                    Physics.Simulate(dt);
                    avatar.Animate(dt, AvatarPose.Idle, 0f, false);
                    Vector3 hips = avatar.Hips.position;
                    // Só a componente horizontal: ao levantar-se a bacia sobe (é a animação), não salta para o lado.
                    Vector3 step = hips - last;
                    step.y = 0f;
                    if (!avatar.InRagdoll) jump = Mathf.Max(jump, step.magnitude);
                    last = hips;
                }
                Assert.That(holder.transform.position.magnitude, Is.GreaterThan(0.3f), "o tiro tem de deslocar o corpo para o teste valer");
                return jump;
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(floor);
                foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                    if (go != null && go.name.StartsWith("Ragdoll_")) Object.DestroyImmediate(go);
                FunModeController.Legacy = previousLegacy;
                Physics.simulationMode = previousMode;
            }
        }
    }
}
