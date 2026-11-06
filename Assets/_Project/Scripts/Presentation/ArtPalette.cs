using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>Paleta única do projecto (character sheet): usada em personagens, arena, máquinas e UI.</summary>
    public static class ArtPalette
    {
        public static readonly Color Navy = Hex(0x1C2A3A);
        public static readonly Color NavyDark = Hex(0x111A25);
        public static readonly Color NavyLight = Hex(0x2B3D52);
        public static readonly Color Ivory = Hex(0xEFE5CF);
        public static readonly Color IvoryShade = Hex(0xD9CBAE);
        public static readonly Color Mustard = Hex(0xF2B233);
        public static readonly Color MustardDark = Hex(0xC98A17);
        public static readonly Color Coral = Hex(0xEE5B4B);
        public static readonly Color Teal = Hex(0x25B3A6);
        public static readonly Color Violet = Hex(0x6C3FA8);
        public static readonly Color Leather = Hex(0x7A4A2A);
        public static readonly Color Felt = Hex(0x1E9C8E);
        // "Morte" da roleta: vermelho vivo (antes gosma verde).
        public static readonly Color Goo = new Color(0.86f, 0.06f, 0.08f, 0.92f);
        public static readonly Color Red = Hex(0xE23B30);
        public static readonly Color Green = Hex(0x39D07A);

        /// <summary>Cores de cabeça dos jogadores (as primeiras quatro são as do character sheet).</summary>
        public static readonly Color[] Heads =
        {
            Mustard, Coral, Teal, Violet, Hex(0x5FD6B4), Hex(0xF27AA0), Hex(0xF28C28), Hex(0x4BA3E3)
        };

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
