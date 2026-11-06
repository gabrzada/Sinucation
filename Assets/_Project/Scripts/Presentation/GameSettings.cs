using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>Preferências locais (conforto, áudio, rato, perfil). Não afectam regras nem física.</summary>
    public sealed class GameSettings
    {
        // Automated runs must never replace the player's name, career or preferences.
        static string Prefix => AutoPilot.HasArguments() ? "FriendSlop.QA." : "FriendSlop.";

        /// <summary>Movimento da câmara (cinetose): 0 nenhum (câmara fixa), 1 reduzido (padrão: balanço dos passos
        /// suave, sem abanões nem quedas), 2 completo.</summary>
        public int CameraMotion = 1;

        /// <summary>Modo conforto (sem abanões, quedas nem rolamento; debuffs de câmara suavizados): tudo menos "completo".</summary>
        public bool ComfortMode => CameraMotion < 2;

        /// <summary>Escala do balanço da cabeça ao andar e da deriva da câmara do menu.</summary>
        public float CameraBobScale => CameraMotion >= 2 ? 1f : CameraMotion == 1 ? 0.35f : 0f;
        public float MasterVolume = 0.9f;
        public float MusicVolume = 0.6f;
        public float JukeboxVolume = 0.6f;
        public bool JukeboxMuted;
        /// <summary>O jogador já aceitou ligar o player oficial do YouTube (liga aos serviços da Google).</summary>
        public bool YouTubeConsent;
        public float SfxVolume = 0.9f;
        public float UiVolume = 0.8f;
        public float MouseSensitivity = 1f;
        public float FieldOfView = 72f;
        public bool ShowHelp = true;
        public float UiScale = 1f;
        /// <summary>Dificuldade dos bots nas salas que este jogador cria (0 Fácil, 1 Médio, 2 Difícil).</summary>
        public int BotDifficulty = 1;

        public string PlayerName = "Jogador";
        public int Archetype;
        public int ColorIndex;
        public int HatId;
        public int AccessoryId;
        /// <summary>Partidas ganhas neste computador (histórico mostrado no cartão do jogador).</summary>
        public int CareerWins;
        public int CareerMatches;
        public string PlayerKey;
        public string LastAddress = "127.0.0.1";
        public string LastCode = "";
        public string LastRoomName = "";
        public GraphicsOptions Graphics = new GraphicsOptions();

        public static GameSettings Load()
        {
            var s = new GameSettings();
            try
            {
                // Chave nova: o movimento reduzido passou a ser o padrão também para quem já tinha guardado o antigo "Comfort".
                s.CameraMotion = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "CameraMotion", 1), 0, 2);
                s.MasterVolume = PlayerPrefs.GetFloat(Prefix + "Master", s.MasterVolume);
                s.MusicVolume = PlayerPrefs.GetFloat(Prefix + "Music", s.MusicVolume);
                s.JukeboxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Jukebox", s.JukeboxVolume));
                s.JukeboxMuted = PlayerPrefs.GetInt(Prefix + "JukeboxMuted", 0) != 0;
                s.YouTubeConsent = PlayerPrefs.GetInt(Prefix + "YouTube", 0) != 0;
                s.SfxVolume = PlayerPrefs.GetFloat(Prefix + "Sfx", s.SfxVolume);
                s.UiVolume = PlayerPrefs.GetFloat(Prefix + "Ui", s.UiVolume);
                s.MouseSensitivity = PlayerPrefs.GetFloat(Prefix + "Sensitivity", s.MouseSensitivity);
                s.FieldOfView = PlayerPrefs.GetFloat(Prefix + "Fov", s.FieldOfView);
                s.ShowHelp = PlayerPrefs.GetInt(Prefix + "Help", 1) == 1;
                s.UiScale = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "UiScale", 1f), HudView.UiScaleMin, HudView.UiScaleMax);
                s.BotDifficulty = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "BotDifficulty", 1), 0, 2);
                s.PlayerName = PlayerPrefs.GetString(Prefix + "Name", "Jogador " + Random.Range(10, 99));
                s.Archetype = PlayerPrefs.GetInt(Prefix + "Archetype", Random.Range(0, 3));
                s.ColorIndex = PlayerPrefs.GetInt(Prefix + "Color", Random.Range(0, 8));
                s.HatId = PlayerPrefs.GetInt(Prefix + "Hat", 0);
                s.AccessoryId = PlayerPrefs.GetInt(Prefix + "Accessory", 0);
                s.CareerWins = Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "CareerWins", 0));
                s.CareerMatches = Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "CareerMatches", 0));
                s.PlayerKey = PlayerPrefs.GetString(Prefix + "Key", "");
                s.LastAddress = PlayerPrefs.GetString(Prefix + "Address", s.LastAddress);
                s.LastCode = PlayerPrefs.GetString(Prefix + "Code", "");
                s.LastRoomName = PlayerPrefs.GetString(Prefix + "RoomName", "");
                s.Graphics = GraphicsOptions.FromJson(PlayerPrefs.GetString(Prefix + "Graphics", ""));
            }
            catch (System.Exception)
            {
                // Preferências indisponíveis: fica com os valores por omissão.
            }
            if (string.IsNullOrEmpty(s.PlayerKey)) s.PlayerKey = System.Guid.NewGuid().ToString("N");
            s.HatId = Mathf.Clamp(s.HatId, 0, CosmeticIds.HatCount - 1);
            s.ColorIndex = Mathf.Clamp(s.ColorIndex, 0, ArtPalette.Heads.Length - 1);
            s.AccessoryId = Mathf.Clamp(s.AccessoryId, 0, CosmeticIds.AccessoryCount - 1);
            return s;
        }

        public void Save()
        {
            try
            {
                PlayerPrefs.SetInt(Prefix + "CameraMotion", CameraMotion);
                PlayerPrefs.SetFloat(Prefix + "Master", MasterVolume);
                PlayerPrefs.SetFloat(Prefix + "Music", MusicVolume);
                PlayerPrefs.SetFloat(Prefix + "Jukebox", JukeboxVolume);
                PlayerPrefs.SetInt(Prefix + "JukeboxMuted", JukeboxMuted ? 1 : 0);
                PlayerPrefs.SetInt(Prefix + "YouTube", YouTubeConsent ? 1 : 0);
                PlayerPrefs.SetFloat(Prefix + "Sfx", SfxVolume);
                PlayerPrefs.SetFloat(Prefix + "Ui", UiVolume);
                PlayerPrefs.SetFloat(Prefix + "Sensitivity", MouseSensitivity);
                PlayerPrefs.SetFloat(Prefix + "Fov", FieldOfView);
                PlayerPrefs.SetInt(Prefix + "Help", ShowHelp ? 1 : 0);
                PlayerPrefs.SetFloat(Prefix + "UiScale", UiScale);
                PlayerPrefs.SetInt(Prefix + "BotDifficulty", BotDifficulty);
                PlayerPrefs.SetString(Prefix + "Name", PlayerName);
                PlayerPrefs.SetInt(Prefix + "Archetype", Archetype);
                PlayerPrefs.SetInt(Prefix + "Color", ColorIndex);
                PlayerPrefs.SetInt(Prefix + "Hat", HatId);
                PlayerPrefs.SetInt(Prefix + "Accessory", AccessoryId);
                PlayerPrefs.SetInt(Prefix + "CareerWins", CareerWins);
                PlayerPrefs.SetInt(Prefix + "CareerMatches", CareerMatches);
                PlayerPrefs.SetString(Prefix + "Key", PlayerKey);
                PlayerPrefs.SetString(Prefix + "Address", LastAddress);
                PlayerPrefs.SetString(Prefix + "Code", LastCode);
                PlayerPrefs.SetString(Prefix + "RoomName", LastRoomName ?? "");
                PlayerPrefs.SetString(Prefix + "Graphics", Graphics.ToJson());
                PlayerPrefs.Save();
            }
            catch (System.Exception)
            {
                // Sem persistência disponível: as preferências valem só para esta sessão.
            }
        }
    }
}
