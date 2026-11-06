using System.IO;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Dados enviados na aprovação de ligação: versão do protocolo, chave estável do jogador
    /// (para reclamar o lugar ao reconectar — LOB-006) e perfil visual.
    /// </summary>
    public struct JoinPayload
    {
        public int Version;
        public string Key;
        public PlayerProfile Profile;

        public byte[] Encode()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version);
                w.Write(Key ?? "");
                MatchViewCodec.WriteProfile(w, Profile ?? new PlayerProfile("Jogador", Archetype.Circle, Color.white));
                w.Flush();
                return ms.ToArray();
            }
        }

        public static bool TryDecode(byte[] data, out JoinPayload payload)
        {
            payload = default;
            if (data == null || data.Length < 4) return false;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var r = new BinaryReader(ms))
                {
                    payload.Version = r.ReadInt32();
                    payload.Key = r.ReadString();
                    payload.Profile = new PlayerProfile("", Archetype.Circle, Color.white);
                    MatchViewCodec.ReadProfile(r, payload.Profile);
                    payload.Profile.IsBot = false;
                    payload.Profile.Name = SanitizeName(payload.Profile.Name);
                    return true;
                }
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        public static string SanitizeName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 16) name = name.Substring(0, 16);
            return name.Length == 0 ? "Jogador" : name;
        }
    }
}
