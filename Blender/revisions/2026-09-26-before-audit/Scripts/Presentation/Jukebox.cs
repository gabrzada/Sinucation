using System.Collections.Generic;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>Faixas da jukebox (Resources/Audio/Music, CC0) e respectivos nomes para a UI.</summary>
    public static class MusicLibrary
    {
        public struct Track
        {
            public string Title;
            public string Author;
            public AudioClip Clip;
        }

        static readonly (string file, string title, string author)[] Entries =
        {
            ("lofi_jam", "Funky Hip Hop Lofi Jam", "omfgdude"),
            ("funked_up", "Funked Up", "joth"),
            ("chill_fever", "A Chill Fever", "pro-sensory"),
            ("two_left_socks", "Two Left Socks", "congusbongus"),
            ("mad_bossa", "Mad Bossa", "spring-spring"),
            ("coffee_house", "Coffee House Bump", "oatcog"),
        };

        static List<Track> s_tracks;

        public static IReadOnlyList<Track> Tracks
        {
            get
            {
                if (s_tracks != null) return s_tracks;
                s_tracks = new List<Track>();
                foreach (var e in Entries)
                {
                    var clip = Resources.Load<AudioClip>("Audio/Music/" + e.file);
                    if (clip != null) s_tracks.Add(new Track { Title = e.title, Author = e.author, Clip = clip });
                }
                return s_tracks;
            }
        }

        /// <summary>Liga a biblioteca ao servidor (duração das faixas para avançar a fila).</summary>
        public static void Register()
        {
            NetGame.TrackCount = Tracks.Count;
            NetGame.TrackLength = i => i >= 0 && i < Tracks.Count ? Tracks[i].Clip.length : 1f;
        }
    }

    /// <summary>
    /// Jukebox física: toca a faixa actual sincronizada pelo tempo do servidor (todos ouvem o mesmo ponto),
    /// e com [E] abre a fila partilhada estilo "bot de música": pôr na fila, votar para saltar.
    /// Frente em −Z local.
    /// </summary>
    public sealed class JukeboxStation : Interactable
    {
        AudioSource _source;
        GameBootstrap _game;
        Transform _focus;
        readonly List<Renderer> _lights = new List<Renderer>();
        Material _lightOn;
        Material _lightOff;
        int _playingTrack = -2;
        double _playingStart;

        public override Vector3 FocusPoint => _focus.position;
        public override string Prompt => "<b>[E]</b> Jukebox — pôr música na fila";
        public override bool Zooms => false;
        public override bool CanUse(NetGame net) => net != null && net.HasSnapshot && MusicLibrary.Tracks.Count > 0;

        public void Build(GameBootstrap game)
        {
            _game = game;
            var wood = VisualFactory.Lit(new Color(0.45f, 0.22f, 0.12f), 0.6f);
            var woodDark = VisualFactory.Lit(new Color(0.25f, 0.12f, 0.07f), 0.5f);
            var chrome = VisualFactory.Lit(new Color(0.85f, 0.85f, 0.9f), 0.92f, 1f);
            var navy = VisualFactory.Lit(ArtPalette.NavyDark, 0.4f);
            var root = transform;

            // Corpo: base, tronco e arco arredondado no topo (frente em −Z).
            LowPoly.Part(ToyMeshes.RoundedBox(0.95f, 0.12f, 0.62f, 0.04f), "Plinth", root, new Vector3(0f, 0.06f, 0f), Vector3.one, woodDark);
            LowPoly.Part(ToyMeshes.RoundedBox(0.9f, 1.35f, 0.56f, 0.06f), "Body", root, new Vector3(0f, 0.8f, 0f), Vector3.one, wood);
            var arch = new List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.PI * i / 16f;
                arch.Add(new Vector2(Mathf.Cos(a) * 0.45f, Mathf.Sin(a) * 0.45f));
            }
            var archMesh = ToyMeshes.RoundedExtrusion(arch.ToArray(), 0.56f, 0.05f, 3);
            LowPoly.Part(archMesh, "Arch", root, new Vector3(0f, 1.46f, 0f), Vector3.one, wood);

            // Vitrine iluminada (discos), grelha das colunas e botões.
            LowPoly.Part(ToyMeshes.RoundedBox(0.66f, 0.42f, 0.03f, 0.02f), "Window", root, new Vector3(0f, 1.24f, -0.28f), Vector3.one, VisualFactory.Emissive(new Color(1f, 0.72f, 0.38f), 1.6f));
            for (int i = 0; i < 5; i++)
                LowPoly.Part(ToyMeshes.RoundedBox(0.02f, 0.3f, 0.02f, 0.006f), "Record", root, new Vector3(-0.2f + i * 0.1f, 1.22f, -0.3f), Vector3.one, navy);
            LowPoly.Part(ToyMeshes.RoundedBox(0.7f, 0.5f, 0.02f, 0.02f), "Grill", root, new Vector3(0f, 0.5f, -0.285f), Vector3.one, navy);
            for (int i = 0; i < 6; i++)
                LowPoly.Part(ToyMeshes.RoundedBox(0.6f, 0.025f, 0.02f, 0.008f), "GrillBar", root, new Vector3(0f, 0.3f + i * 0.08f, -0.3f), Vector3.one, chrome);
            for (int i = 0; i < 6; i++)
                LowPoly.Part(ToyMeshes.Sphere, "Button", root, new Vector3(-0.25f + i * 0.1f, 0.93f, -0.29f), new Vector3(0.06f, 0.06f, 0.03f), VisualFactory.Lit(ArtPalette.Heads[i], 0.7f));
            foreach (float x in new[] { -0.47f, 0.47f })
                LowPoly.Part(ToyMeshes.Capsule(0.035f, 1.35f), "Pillar", root, new Vector3(x, 1.48f, -0.25f), Vector3.one, chrome);
            VisualFactory.Text(root, "JUKEBOX", new Vector3(0f, 1.6f, -0.29f), 0.12f, ArtPalette.Mustard);

            // Néon do arco com lâmpadas que piscam.
            _lightOn = VisualFactory.Emissive(new Color(1f, 0.35f, 0.7f), 4f);
            _lightOff = VisualFactory.Emissive(new Color(0.3f, 0.6f, 1f), 3f);
            var neon = LowPoly.Part(LowPoly.Ring(32, 0.47f, 0.03f, 0.03f, 180f), "ArchNeon", root, new Vector3(0f, 1.46f, -0.29f), Vector3.one, VisualFactory.Emissive(new Color(1f, 0.55f, 0.2f), 3f));
            neon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 12; i++)
            {
                float a = Mathf.Lerp(8f, 172f, i / 11f) * Mathf.Deg2Rad;
                var l = LowPoly.Part(ToyMeshes.Sphere, "Light", root, new Vector3(Mathf.Cos(a) * 0.52f, 1.46f + Mathf.Sin(a) * 0.52f, -0.27f), Vector3.one * 0.06f, _lightOn);
                l.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _lights.Add(l.GetComponent<Renderer>());
            }
            _focus = LowPoly.Pivot("Focus", root, new Vector3(0f, 1.2f, -0.3f));

            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 0.55f;
            _source.minDistance = 4f;
            _source.maxDistance = 30f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.playOnAwake = false;

            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.size = new Vector3(1f, 2f, 0.7f);
            InteractionSystem.All.Add(this);
        }

        void OnDestroy() => InteractionSystem.All.Remove(this);

        void Update()
        {
            float t = Time.time;
            for (int i = 0; i < _lights.Count; i++) _lights[i].sharedMaterial = Mathf.Repeat(t * 4f + i, 3f) < 1.5f ? _lightOn : _lightOff;

            var net = NetGame.Instance;
            var tracks = MusicLibrary.Tracks;
            if (net == null || !net.HasSnapshot || !net.IsSpawned || tracks.Count == 0)
            {
                if (_source.isPlaying) _source.Stop();
                _playingTrack = -2;
                return;
            }

            var music = net.View.Music;
            _source.volume = _game.Settings.MusicVolume * _game.Settings.MasterVolume * 0.8f;
            if (music.Track < 0 || music.Track >= tracks.Count) return;

            double now = net.NetworkManager.ServerTime.Time;
            float offset = (float)(now - music.StartedAt);
            if (music.Track != _playingTrack || System.Math.Abs(music.StartedAt - _playingStart) > 0.01)
            {
                _playingTrack = music.Track;
                _playingStart = music.StartedAt;
                _source.clip = tracks[music.Track].Clip;
                _source.time = Mathf.Clamp(offset, 0f, _source.clip.length - 0.1f);
                _source.Play();
            }
            else if (_source.isPlaying && Mathf.Abs(_source.time - offset) > 0.6f && offset < _source.clip.length - 0.5f)
            {
                _source.time = Mathf.Clamp(offset, 0f, _source.clip.length - 0.1f);
            }
        }
    }
}
