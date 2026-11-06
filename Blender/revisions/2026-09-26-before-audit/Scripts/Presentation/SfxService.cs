using System;
using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    public enum AudioCategory
    {
        Sfx,
        Ui,
        Music
    }

    /// <summary>
    /// Efeitos sonoros (AUD-001..005) com sons CC0 do Kenney (Resources/Audio/Sfx), agrupados por prefixo:
    /// bolas, tabelas, caçapas e taco saem da mesa em 3D; UI, jingles e tensão em 2D. Volumes independentes
    /// por categoria. Se algum som faltar, usa uma alternativa sintetizada.
    /// </summary>
    public sealed class SfxService : MonoBehaviour
    {
        const int SampleRate = 44100;

        readonly Dictionary<string, List<AudioClip>> _bank = new Dictionary<string, List<AudioClip>>();
        GameBootstrap _game;
        AudioSource[] _pool2D;
        AudioSource[] _pool3D;
        int _next2D;
        int _next3D;
        AudioSource _tension;
        System.Random _noise = new System.Random(1234);
        AudioClip _fallbackClick;
        AudioClip _fallbackDrone;
        AudioSource _murmur;
        AudioSource _roomTone;
        AudioSource _roll;
        MatchPhase _phase;
        bool _breakPending;
        float _lastPocketCheer;

        public void Initialize(GameBootstrap game)
        {
            _game = game;
            _pool2D = CreatePool(16, 0f);
            _pool3D = CreatePool(24, 1f);
            _tension = gameObject.AddComponent<AudioSource>();
            _tension.loop = true;
            _tension.playOnAwake = false;

            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Sfx"))
            {
                string prefix = clip.name;
                int underscore = prefix.LastIndexOf('_');
                if (underscore > 0) prefix = prefix.Substring(0, underscore);
                if (!_bank.TryGetValue(prefix, out var list)) _bank[prefix] = list = new List<AudioClip>();
                list.Add(clip);
            }

            _fallbackClick = Make("click", 0.06f, t => Sin(2400f, t) * Env(t, 60f) * 0.5f + Noise() * Env(t, 140f) * 0.3f);
            _fallbackDrone = Make("drone", 2f, t => (Sin(55f, t) * 0.5f + Sin(82.5f, t) * 0.3f) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t)) * 0.5f);

            // Ambiente: plateia a murmurar na varanda, zumbido da sala, néons das máquinas.
            _murmur = Loop("CrowdMurmur", Pick("walla"), new Vector3(0f, 3.4f, 3.5f), 0.5f, 6f, 30f);
            _roomTone = Loop("RoomTone", Pick("hum") ?? _fallbackDrone, Vector3.zero, 0f, 1f, 30f);
            _roll = gameObject.AddComponent<AudioSource>();
            _roll.playOnAwake = false;
            _roll.clip = Pick("drumroll");
            if (game.SabotageMachine != null)
            {
                var hum = Loop("SabotageHum", Pick("hum") ?? _fallbackDrone, game.SabotageMachine.transform.position + Vector3.up, 1f, 0.8f, 5f);
                hum.pitch = 1.6f;
                hum.volume = 0.12f;
            }
        }

        AudioSource Loop(string name, AudioClip clip, Vector3 position, float spatial, float minDistance, float maxDistance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.spatialBlend = spatial;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            if (clip != null)
            {
                s.time = UnityEngine.Random.Range(0f, clip.length * 0.9f);
                s.Play();
            }
            return s;
        }

        AudioSource[] CreatePool(int count, float spatial)
        {
            var pool = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject(spatial > 0f ? "Sfx3D" : "Sfx2D");
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = spatial;
                s.minDistance = 1.2f;
                s.maxDistance = 25f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.dopplerLevel = 0f;
                pool[i] = s;
            }
            return pool;
        }

        bool Has(string prefix) => _bank.ContainsKey(prefix);

        AudioClip Pick(string prefix)
        {
            if (_bank.TryGetValue(prefix, out var list) && list.Count > 0) return list[UnityEngine.Random.Range(0, list.Count)];
            return null;
        }

        /// <summary>Liga-se aos eventos replicados: host e clientes ouvem exactamente o mesmo.</summary>
        public void Bind(NetGame net)
        {
            net.Contact += OnContact;
            net.PhaseChanged += OnPhase;
            net.ShotResolved += (shooter, foul, keep, won) =>
            {
                if (foul != FoulType.None)
                {
                    Play2D("uierror", 0.8f, 0.9f, AudioCategory.Sfx);
                    PlayCrowd("crowdaww", 0.22f);
                }
                else if (keep)
                {
                    Play2D("uiconfirm", 0.55f, 1f, AudioCategory.Ui);
                    if (Time.time - _lastPocketCheer > 6f && UnityEngine.Random.value < 0.6f)
                    {
                        _lastPocketCheer = Time.time;
                        PlayCrowd("crowdclap", 0.16f);
                    }
                }
            };
            net.RiskResolved += OnRisk;
            net.SabotageApplied += (buyer, target, def) =>
            {
                bool againstMe = net.LocalPlayer != null && net.LocalPlayer.Id == target;
                Play2D("sabotage", againstMe ? 1f : 0.55f, againstMe ? 0.85f : 1f, AudioCategory.Sfx);
                Play2D("glitch", 0.5f, 1f, AudioCategory.Sfx);
                if (_game.SabotageMachine != null) Play3D("card", _game.SabotageMachine.transform.position + Vector3.up, 0.8f, 1f);
                PlayCrowd("crowdlaugh", 0.14f);
            };
            net.TurnStarted += id => { if (net.LocalPlayer != null && net.LocalPlayer.Id == id) Play2D("turn", 0.7f, 1f, AudioCategory.Ui); };
            net.RoundWonEvent += id =>
            {
                Play2D("jingleround", 0.8f, 1f, AudioCategory.Music);
                PlayCrowd("crowdcheer", 0.3f);
            };
            net.MatchEnded += () =>
            {
                Play2D("jinglewin", 0.9f, 1f, AudioCategory.Music);
                PlayCrowd("crowdcheer", 0.38f);
                PlayCrowd("crowdclap", 0.3f);
            };
            net.PlayerEliminated += id => Play2D("jinglefail", 0.7f, 1f, AudioCategory.Music);
            net.Emote += (client, emote) =>
            {
                Play2D("uitick", 0.35f, 1.3f + emote * 0.1f, AudioCategory.Ui);
            };
            net.Thrown += (client, origin, velocity) => Play3D("cloth", origin, 0.5f, UnityEngine.Random.Range(0.9f, 1.2f));
            net.CoinsChanged += (id, delta, reason) => { if (delta > 0 && net.LocalPlayer != null && net.LocalPlayer.Id == id) PlayCoin(); };
        }

        public void PlayTick() => Play2D("uitick", 0.6f, UnityEngine.Random.Range(0.95f, 1.1f), AudioCategory.Ui);
        public void PlayUi() => Play2D("uiclick", 0.5f, 1f, AudioCategory.Ui);
        public void PlayUi(string prefix) => Play2D(prefix, 0.55f, 1f, AudioCategory.Ui);
        public void PlayCoin() => Play2D(UnityEngine.Random.value < 0.5f ? "coins" : "chips", 0.6f, 1f, AudioCategory.Ui);

        /// <summary>Passos com o som do chão: alcatifa no lounge, betão no resto da arena.</summary>
        public void PlayStep(Vector3 position, bool local)
        {
            bool carpet = position.x > -1.1f && position.x < 2.3f && position.z < -3.8f && position.z > -6.1f;
            string wanted = carpet ? "stepcarpet" : "stepconcrete";
            string prefix = _bank.ContainsKey(wanted) ? wanted : "step";
            if (local) Play2D(prefix, carpet ? 0.22f : 0.16f, UnityEngine.Random.Range(0.92f, 1.08f), AudioCategory.Sfx);
            else Play3D(prefix, position, 0.35f, UnityEngine.Random.Range(0.92f, 1.08f));
        }

        public void PlayGoo(Vector3 position, bool big)
        {
            Play3D("bang", position, big ? 1f : 0.7f, 1.1f);
            Play3D("splat", position, 1f, big ? 0.85f : 1.1f);
            Play3D("slime", position, 1f, big ? 0.8f : 1f);
            if (big) Play3D("crunch", position, 0.8f, 1.2f);
            Play3D("goo", position, 1f, big ? 0.9f : 1.1f);
            PlayCrowd(big ? "crowdgasp" : "crowdaww", big ? 0.32f : 0.22f);
        }

        /// <summary>Uma reacção da plateia (sons CC0 do Freesound), 2D e baixinha: a plateia é fundo, não protagonista.</summary>
        public void PlayCrowd(string prefix, float volume)
        {
            var clip = Pick(prefix);
            if (clip == null) return;
            var source = _pool2D[_next2D];
            _next2D = (_next2D + 1) % _pool2D.Length;
            source.pitch = UnityEngine.Random.Range(0.97f, 1.03f);
            source.volume = Mathf.Clamp01(volume) * VolumeFor(AudioCategory.Sfx);
            source.PlayOneShot(clip);
        }

        void OnContact(ContactType type, float speed, Vector2 point)
        {
            Vector3 table = TableView.ToWorld(point, 0.03f);
            switch (type)
            {
                case ContactType.CueStrike:
                    Play3D(Has("poolcue") ? "poolcue" : "cue", table, Mathf.Clamp01(0.35f + speed / 5f), UnityEngine.Random.Range(0.95f, 1.05f));
                    // Primeira tacada da ronda com força: o estalo da abertura da rack.
                    if (_breakPending && speed > 2.5f && Has("poolbreak")) Play3D("poolbreak", table, 0.9f, UnityEngine.Random.Range(0.97f, 1.03f));
                    _breakPending = false;
                    break;
                case ContactType.BallBall when Has("poolclack"):
                    Play3D("poolclack", table, Mathf.Clamp01(0.06f + speed / 2.4f), Mathf.Lerp(1.12f, 0.96f, Mathf.Clamp01(speed / 3f)) * UnityEngine.Random.Range(0.97f, 1.03f));
                    break;
                case ContactType.BallBall:
                    // Toques leves soam mais agudos e secos; os fortes mais cheios.
                    Play3D("ball", table, Mathf.Clamp01(0.08f + speed / 2.2f), Mathf.Lerp(1.3f, 1.0f, Mathf.Clamp01(speed / 3f)) * UnityEngine.Random.Range(0.97f, 1.03f));
                    break;
                case ContactType.Cushion:
                    if (speed > 0.05f) Play3D("cushion", table, Mathf.Clamp01(speed / 3f) * 0.8f, UnityEngine.Random.Range(0.9f, 1.1f));
                    break;
                case ContactType.Pocket:
                    Play3D("pocket", table, 0.8f, UnityEngine.Random.Range(0.9f, 1.05f));
                    break;
            }
        }

        void OnPhase(MatchPhase phase)
        {
            bool tension = phase == MatchPhase.PunishmentDraw || phase == MatchPhase.AwaitTrigger;
            if (tension && !_tension.isPlaying)
            {
                _tension.clip = Pick("tension") ?? _fallbackDrone;
                _tension.Play();
            }
            else if (!tension && _tension.isPlaying)
            {
                _tension.Stop();
            }
            if (phase == MatchPhase.Shop) Play2D("laptop", 0.6f, 1f, AudioCategory.Ui);
            if (phase == MatchPhase.PunishmentDraw)
            {
                Play2D("spin", 0.7f, 1f, AudioCategory.Sfx);
                if (_game.Randomizer != null) Play3D("rattle", _game.Randomizer.transform.position + Vector3.up * 2.8f, 0.8f, 1f);
                _roll.volume = 0.35f * VolumeFor(AudioCategory.Sfx);
                _roll.Play();
                PlayCrowd("crowdgasp", 0.16f);
            }
            else if (_roll.isPlaying && phase != MatchPhase.AwaitTrigger)
            {
                _roll.Stop();
            }
            if (phase == MatchPhase.AwaitTrigger)
            {
                _roll.Stop();
                Play2D("cock", 0.8f, 1f, AudioCategory.Sfx);
                Play2D("latch", 0.6f, 0.9f, AudioCategory.Sfx);
            }
            if (phase == MatchPhase.RoundIntro)
            {
                Play2D("whirr", 0.25f, 1.2f, AudioCategory.Sfx);
                _breakPending = true;
            }
            _phase = phase;
        }

        void OnRisk(int playerId, RiskOutcome outcome, int modifierDef, bool eliminated)
        {
            Play2D("click", 1f, 1f, AudioCategory.Sfx);
            if (_game.Randomizer != null) Play3D("whirr", _game.Randomizer.transform.position + Vector3.up * 1.3f, 0.7f, 1.4f);
            if (outcome != RiskOutcome.Discharge)
            {
                Play2D("forcefield", 0.6f, 1.2f, AudioCategory.Sfx);
                PlayCrowd("crowdclap", 0.18f);
            }
        }

        public void Play2D(string prefix, float volume, float pitch, AudioCategory category)
        {
            var clip = Pick(prefix) ?? _fallbackClick;
            var source = _pool2D[_next2D];
            _next2D = (_next2D + 1) % _pool2D.Length;
            source.pitch = pitch;
            source.volume = Mathf.Clamp01(volume) * VolumeFor(category);
            source.PlayOneShot(clip);
        }

        public void Play3D(string prefix, Vector3 position, float volume, float pitch)
        {
            var clip = Pick(prefix) ?? _fallbackClick;
            var source = _pool3D[_next3D];
            _next3D = (_next3D + 1) % _pool3D.Length;
            source.transform.position = position;
            source.pitch = pitch;
            source.volume = Mathf.Clamp01(volume) * VolumeFor(AudioCategory.Sfx);
            source.PlayOneShot(clip);
        }

        float VolumeFor(AudioCategory category)
        {
            var s = _game.Settings;
            float v = category == AudioCategory.Music ? s.MusicVolume : category == AudioCategory.Ui ? s.UiVolume : s.SfxVolume;
            return v * s.MasterVolume;
        }

        void Update()
        {
            if (_tension != null) _tension.volume = 0.5f * VolumeFor(AudioCategory.Music);
            var net = NetGame.Instance;
            bool inMatch = net != null && net.HasSnapshot && net.View.InMatch;
            // A plateia fala mais baixo durante a roleta (suspense) e mais alto entre rondas.
            float crowd = !inMatch ? 0.035f : _phase == MatchPhase.AwaitTrigger || _phase == MatchPhase.PunishmentDraw ? 0.015f : _phase == MatchPhase.AwaitShot ? 0.03f : 0.05f;
            if (_murmur != null) _murmur.volume = Mathf.MoveTowards(_murmur.volume, crowd * VolumeFor(AudioCategory.Sfx), Time.deltaTime * 0.2f);
            if (_roomTone != null) _roomTone.volume = 0.05f * VolumeFor(AudioCategory.Sfx);
        }

        static float Sin(float freq, float t) => Mathf.Sin(2f * Mathf.PI * freq * t);
        static float Env(float t, float decay) => Mathf.Exp(-t * decay);
        float Noise() => (float)(_noise.NextDouble() * 2.0 - 1.0);

        AudioClip Make(string name, float duration, Func<float, float> wave)
        {
            int samples = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = Mathf.Clamp(wave(t) * Mathf.Clamp01((duration - t) / 0.01f), -1f, 1f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
