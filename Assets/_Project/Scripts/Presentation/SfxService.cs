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
        AudioSource _machineHum;
        AudioSource _roll;
        NetGame _boundNet;
        float _crowdGain;
        MatchPhase _phase;
        bool _breakPending;
        int _pocketsThisShot;
        float _lastPocketCheer;

        bool _gameplayLoaded;
        public void Initialize(GameBootstrap game, bool menuOnly = false)
        {
            _game = game;
            if (_pool2D == null) _pool2D = CreatePool(16, 0f);
            if (_fallbackClick == null) _fallbackClick = Make("click", 0.06f, t => Sin(2400f, t) * Env(t, 60f) * 0.5f + Noise() * Env(t, 140f) * 0.3f);
            if (menuOnly || _gameplayLoaded) return;
            _gameplayLoaded = true;
            _pool3D = CreatePool(24, 1f);
            _tension = Loop("Suspense", null, Vector3.zero, 0f, 1f, 30f);

            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Sfx"))
            {
                string prefix = clip.name;
                int underscore = prefix.LastIndexOf('_');
                if (underscore > 0) prefix = prefix.Substring(0, underscore);
                if (!_bank.TryGetValue(prefix, out var list)) _bank[prefix] = list = new List<AudioClip>();
                list.Add(clip);
            }

            _fallbackDrone = Make("drone", 2f, t => (Sin(55f, t) * 0.5f + Sin(82.5f, t) * 0.3f) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t)) * 0.5f);

            // Keep electrical ambience quiet and local; the suspense clip shares the same source recording.
            _murmur = Loop("CrowdMurmur", Pick("walla"), new Vector3(0f, 3.4f, 3.5f), 0.5f, 6f, 30f);
            _roomTone = Loop("RoomTone", Pick("hum") ?? _fallbackDrone, Vector3.zero, 0f, 1f, 30f);
            FilterAmbience(_roomTone, 100f, 450f);
            FilterAmbience(_tension, 100f, 900f);
            _tension.clip = Pick("tension") ?? _fallbackDrone;
            _roll = gameObject.AddComponent<AudioSource>();
            _roll.playOnAwake = false;
            _roll.clip = Pick("drumroll");
            if (game != null && game.SabotageMachine != null)
            {
                _machineHum = Loop("SabotageHum", Pick("hum") ?? _fallbackDrone, game.SabotageMachine.transform.position + Vector3.up, 1f, 0.5f, 3f);
                FilterAmbience(_machineHum, 150f, 1200f);
            }
        }

        static void FilterAmbience(AudioSource source, float highPass, float lowPass)
        {
            source.gameObject.AddComponent<AudioHighPassFilter>().cutoffFrequency = highPass;
            source.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = lowPass;
        }

        AudioSource Loop(string name, AudioClip clip, Vector3 position, float spatial, float minDistance, float maxDistance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = spatial;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            if (clip != null)
            {
                s.time = UnityEngine.Random.Range(0f, clip.length * 0.9f);
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

        readonly Dictionary<string, int> _lastPick = new Dictionary<string, int>();

        /// <summary>Amostra aleatória do grupo, nunca a mesma duas vezes seguidas (menos "metralhadora").</summary>
        AudioClip Pick(string prefix)
        {
            if (!_bank.TryGetValue(prefix, out var list) || list.Count == 0) return null;
            int index = UnityEngine.Random.Range(0, list.Count);
            if (list.Count > 1 && _lastPick.TryGetValue(prefix, out int last) && index == last) index = (index + 1 + UnityEngine.Random.Range(0, list.Count - 1)) % list.Count;
            _lastPick[prefix] = index;
            return list[index];
        }

        // Limitador por tipo de contacto: no máximo N sons por janela curta; o volume de cada um baixa com a
        // densidade (1/√n) para uma abertura com 15 choques não saturar nem soar a estalido digital.
        readonly float[] _contactTimes = new float[32];
        int _contactHead;

        float ContactGain(float now, float window, int maxVoices, out bool drop)
        {
            int recent = 0;
            foreach (float t in _contactTimes)
                if (now - t < window) recent++;
            drop = recent >= maxVoices;
            if (!drop)
            {
                _contactTimes[_contactHead] = now;
                _contactHead = (_contactHead + 1) % _contactTimes.Length;
            }
            return 1f / Mathf.Sqrt(1f + recent);
        }

        /// <summary>Curva perceptiva da força do impacto (m/s) para volume 0–1.</summary>
        static float Impact(float speed, float full, float floor) => Mathf.Clamp01(floor + Mathf.Pow(Mathf.Clamp01(speed / full), 0.7f) * (1f - floor));

        /// <summary>Liga-se aos eventos replicados: host e clientes ouvem exactamente o mesmo.</summary>
        public void Bind(NetGame net)
        {
            if (_boundNet == net) return;
            _boundNet = net;
            _phase = net.HasSnapshot ? net.View.Phase : default;
            _breakPending = false;
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
            net.Thrown += t => Play3D("cloth", t.Origin, 0.5f, UnityEngine.Random.Range(0.9f, 1.2f));
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
            float now = Time.unscaledTime;
            bool drop;
            switch (type)
            {
                case ContactType.CueStrike:
                    // Tacada: mais forte = mais volume e um pouco mais grave (peso); a abertura soma o estalo da rack.
                    Play3D(Has("poolcue") ? "poolcue" : "cue", table, Impact(speed, 5f, 0.3f), Mathf.Lerp(1.06f, 0.94f, Mathf.Clamp01(speed / 5f)) * UnityEngine.Random.Range(0.98f, 1.02f));
                    _pocketsThisShot = 0;
                    // Primeira tacada da ronda com força: o estalo da abertura da rack.
                    if (_breakPending && speed > 2.5f && Has("poolbreak")) Play3D("poolbreak", table, 0.9f, UnityEngine.Random.Range(0.97f, 1.03f));
                    _breakPending = false;
                    break;
                case ContactType.BallBall:
                {
                    if (speed < 0.04f) break;
                    float gain = ContactGain(now, 0.07f, 6, out drop);
                    if (drop) break;
                    // Toques leves soam mais agudos e secos; os fortes mais cheios e graves (bolas pesadas de resina).
                    string prefix = Has("poolclack") ? "poolclack" : "ball";
                    Play3D(prefix, table, Impact(speed, 3.2f, 0.05f) * gain, Mathf.Lerp(1.1f, 0.95f, Mathf.Clamp01(speed / 3f)) * UnityEngine.Random.Range(0.97f, 1.03f));
                    break;
                }
                case ContactType.Cushion:
                {
                    if (speed < 0.08f) break;
                    float gain = ContactGain(now, 0.07f, 5, out drop);
                    if (drop) break;
                    Play3D("cushion", table, Impact(speed, 3f, 0.08f) * 0.8f * gain, Mathf.Lerp(1.08f, 0.92f, Mathf.Clamp01(speed / 3f)) * UnityEngine.Random.Range(0.97f, 1.03f));
                    break;
                }
                case ContactType.Pocket:
                    // A bola bate no fundo do copo de couro; um pouco depois, o "clac" contra as outras lá dentro.
                    Play3D("pocket", table, Impact(speed, 2.5f, 0.45f), UnityEngine.Random.Range(0.92f, 1.05f));
                    if (Has("thud")) StartCoroutine(Delayed(0.16f, () => Play3D("thud", table + Vector3.down * 0.3f, 0.35f, UnityEngine.Random.Range(1.2f, 1.4f))));
                    // Festa quando entra uma bola (não a branca): arpejo que sobe um tom a cada bola na mesma tacada.
                    var mirror = NetGame.Instance != null ? NetGame.Instance.Mirror : null;
                    bool scratch = mirror != null && mirror.Cue != null && mirror.Cue.Pocketed;
                    if (!scratch && Has("pocketjoy"))
                    {
                        Play2D("pocketjoy", 0.6f, Mathf.Pow(2f, 2f * Mathf.Min(_pocketsThisShot, 5) / 12f), AudioCategory.Sfx);
                        _pocketsThisShot++;
                    }
                    break;
            }
        }

        System.Collections.IEnumerator Delayed(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        void OnPhase(MatchPhase phase)
        {
            _phase = phase;
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
                Play2D("roundstart", 0.35f, 1f, AudioCategory.Sfx);
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
            if (_game == null || !_gameplayLoaded) return;
            var net = NetGame.Instance;
            bool inMatch = net != null && net.HasSnapshot && net.View.InMatch;
            ApplyMix(_game.Settings, _game.Screen == AppScreen.Session, inMatch,
                inMatch ? net.View.Phase : default, Time.unscaledDeltaTime);
        }

        void ApplyMix(GameSettings settings, bool inArena, bool inMatch, MatchPhase phase, float dt)
        {
            float sfx = Mathf.Clamp01(settings.MasterVolume) * Mathf.Clamp01(settings.SfxVolume);
            float music = Mathf.Clamp01(settings.MasterVolume) * Mathf.Clamp01(settings.MusicVolume);
            bool suspense = inArena && inMatch && (phase == MatchPhase.PunishmentDraw || phase == MatchPhase.AwaitTrigger);
            float crowd = !inMatch ? .035f : suspense ? .015f : phase == MatchPhase.AwaitShot ? .03f : .05f;
            // Fade the ambience gain, not the user's volume: muting must take effect immediately.
            _crowdGain = inArena ? Mathf.MoveTowards(_crowdGain, crowd, dt * .2f) : 0f;
            MixLoop(_murmur, inArena, _crowdGain * sfx);
            MixLoop(_roomTone, inArena, .008f * sfx);
            MixLoop(_machineHum, inArena, .025f * sfx);
            MixLoop(_tension, suspense, .06f * music);
            if (_roll != null)
            {
                _roll.volume = inArena && inMatch ? .35f * sfx : 0f;
                if (!inArena || !inMatch) _roll.Stop();
            }
        }

        static void MixLoop(AudioSource source, bool active, float volume)
        {
            if (source == null) return;
            source.volume = active ? volume : 0f;
            if (active && volume > 0f && source.clip != null)
            {
                if (!source.isPlaying) source.Play();
            }
            else if (source.isPlaying) source.Stop();
        }

        void OnDestroy()
        {
            if (_fallbackClick != null) Destroy(_fallbackClick);
            if (_fallbackDrone != null) Destroy(_fallbackDrone);
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
