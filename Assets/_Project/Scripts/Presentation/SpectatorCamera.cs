using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Modo espectador de quem levou gosma (ou entrou a meio): câmara em terceira pessoa que segue um jogador,
    /// vista aérea da mesa, ou voo livre como fantasma. Por omissão segue automaticamente quem está a jogar
    /// (ou quem está na roleta). Clique esq./dir. troca de jogador, [V] muda de câmara, [R] volta ao automático,
    /// roda do rato aproxima.
    /// </summary>
    public sealed class SpectatorCamera : MonoBehaviour
    {
        public enum Mode
        {
            Follow,
            Table,
            Free
        }

        static readonly string[] ModeNames = { "SEGUIR JOGADOR", "VISTA DA MESA", "VOO LIVRE" };

        GameBootstrap _game;
        int _manualTarget = -1;
        float _orbitYaw;
        float _orbitPitch = 16f;
        float _distance = 2.4f;
        float _tableYaw = 30f;
        Vector3 _pos;
        Quaternion _rot = Quaternion.identity;
        bool _hasPose;
        bool _wasActive;

        public Mode Current { get; private set; } = Mode.Follow;
        public bool Active { get; private set; }
        /// <summary>A câmara é controlada pelo espectador (e não pela primeira pessoa do fantasma).</summary>
        public bool ControlsCamera => Active && Current != Mode.Free;
        public int TargetId { get; private set; } = -1;
        public bool Automatic => _manualTarget < 0;
        public string ModeName => ModeNames[(int)Current];

        /// <summary>Testes automáticos: ângulo e distância da câmara que segue o jogador.</summary>
        public void DebugOrbit(float yaw, float pitch, float distance)
        {
            _orbitYaw = yaw;
            _orbitPitch = pitch;
            _distance = distance;
        }

        public void Initialize(GameBootstrap game) => _game = game;

        void Update()
        {
            var net = NetGame.Instance;
            var view = net != null && net.HasSnapshot ? net.View : null;
            var me = net != null ? net.LocalPlayer : null;
            Active = view != null && view.InMatch && view.Phase != MatchPhase.MatchOver && NetPlayer.Local != null &&
                     (me == null || me.Eliminated) && !_game.Hud.EliminatedOverlayOpen;
            if (!Active)
            {
                _wasActive = false;
                _hasPose = false;
                return;
            }
            if (!_wasActive)
            {
                _wasActive = true;
                _manualTarget = -1;
                Current = Mode.Follow;
            }

            bool input = !_game.CursorFree && !_game.Paused && !_game.Hud.ChatOpen && _game.Interaction.Active == null;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (input && keyboard != null)
            {
                if (keyboard.vKey.wasPressedThisFrame)
                {
                    Current = (Mode)(((int)Current + 1) % 3);
                    _game.Sfx.PlayUi();
                    if (Current == Mode.Free && _hasPose && NetPlayer.Local != null)
                    {
                        // O fantasma aparece onde estava a câmara, a olhar na mesma direcção.
                        var e = _rot.eulerAngles;
                        NetPlayer.Local.TeleportTo(RoomBuilder.ClampInside(_pos - Vector3.up * AvatarVisual.EyeHeight, 0.2f, 3.2f), e.y);
                        NetPlayer.Local.SetLook(e.y, e.x > 180f ? e.x - 360f : e.x);
                    }
                }
                if (keyboard.rKey.wasPressedThisFrame && _manualTarget >= 0)
                {
                    _manualTarget = -1;
                    _game.Sfx.PlayUi();
                }
            }
            if (input && mouse != null && Current != Mode.Free)
            {
                if (mouse.leftButton.wasPressedThisFrame) Cycle(view, 1);
                if (mouse.rightButton.wasPressedThisFrame) Cycle(view, -1);
                Vector2 delta = mouse.delta.ReadValue() * (0.075f * _game.Settings.MouseSensitivity);
                float scroll = mouse.scroll.ReadValue().y;
                if (Current == Mode.Follow)
                {
                    _orbitYaw += delta.x;
                    _orbitPitch = Mathf.Clamp(_orbitPitch - delta.y, -10f, 70f);
                    if (Mathf.Abs(scroll) > 0.01f) _distance = Mathf.Clamp(_distance - Mathf.Sign(scroll) * 0.25f, 1.2f, 5f);
                }
                else
                {
                    _tableYaw += delta.x;
                }
            }

            if (_manualTarget >= 0)
            {
                var manual = view.GetPlayer(_manualTarget);
                if (manual == null || manual.Eliminated) _manualTarget = -1;
            }
            TargetId = _manualTarget >= 0 ? _manualTarget : AutoTarget(view);
        }

        /// <summary>Quem está no centro da acção: quem vai à roleta, quem ganhou a ronda, ou quem está a jogar.</summary>
        static int AutoTarget(MatchView view)
        {
            switch (view.Phase)
            {
                case MatchPhase.PunishmentDraw:
                    return PunishmentPresentation.HighlightedCandidate(view);
                case MatchPhase.AwaitTrigger:
                case MatchPhase.RiskReveal:
                    return view.Selected;
                case MatchPhase.RoundWon:
                    return view.RoundWinner;
            }
            if (view.CurrentPlayer >= 0) return view.CurrentPlayer;
            foreach (var p in view.Players)
                if (p.IsActive) return p.Id;
            return -1;
        }

        void Cycle(MatchView view, int step)
        {
            int count = view.Players.Count;
            if (count == 0) return;
            int start = TargetId >= 0 ? TargetId : 0;
            int index = 0;
            for (int i = 0; i < count; i++)
                if (view.Players[i].Id == start) index = i;
            for (int k = 1; k <= count; k++)
            {
                var p = view.Players[((index + step * k) % count + count) % count];
                if (p.IsActive)
                {
                    _manualTarget = p.Id;
                    _orbitYaw = 0f;
                    _game.Sfx.PlayUi();
                    return;
                }
            }
        }

        public bool TryGetPose(out Vector3 position, out Quaternion rotation)
        {
            position = _pos;
            rotation = _rot;
            if (!ControlsCamera) return false;

            float dt = Time.unscaledDeltaTime;
            Vector3 wantPos;
            Quaternion wantRot;
            if (Current == Mode.Table)
            {
                _tableYaw += dt * 4f;
                var orbit = Quaternion.Euler(0f, _tableYaw, 0f);
                Vector3 center = new Vector3(0f, TableView.TopY, 0f);
                wantPos = center + orbit * new Vector3(0f, 2.9f, -3.1f);
                wantRot = Quaternion.LookRotation(center - wantPos);
            }
            else
            {
                var net = NetGame.Instance;
                var avatar = AvatarLocator.Find(net, TargetId);
                if (avatar == null)
                {
                    Vector3 center = new Vector3(0f, TableView.TopY, 0f);
                    wantPos = center + new Vector3(0f, 2.6f, -3f);
                    wantRot = Quaternion.LookRotation(center - wantPos);
                }
                else
                {
                    Vector3 pivot = avatar.transform.position + Vector3.up * 1.05f;
                    // Atrás do jogador, a olhar para onde ele olha (a mesa, quando está a jogar).
                    float yaw = avatar.transform.eulerAngles.y + _orbitYaw;
                    var look = Quaternion.Euler(_orbitPitch, yaw, 0f);
                    Vector3 back = look * Vector3.back;
                    float dist = _distance;
                    if (Physics.SphereCast(pivot, 0.18f, back, out var hit, dist, GameplayCollision.WorldQueryMask, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(avatar.transform))
                        dist = Mathf.Max(0.5f, hit.distance - 0.05f);
                    wantPos = pivot + back * dist + look * new Vector3(0.35f, 0f, 0f);
                    wantRot = Quaternion.LookRotation(pivot + look * Vector3.forward * 1.2f - wantPos);
                }
            }

            if (!_hasPose)
            {
                _hasPose = true;
                _pos = wantPos;
                _rot = wantRot;
            }
            else
            {
                float k = 1f - Mathf.Exp(-dt * 7f);
                _pos = Vector3.Lerp(_pos, wantPos, k);
                _rot = Quaternion.Slerp(_rot, wantRot, k);
            }
            position = _pos;
            rotation = _rot;
            return true;
        }
    }
}
