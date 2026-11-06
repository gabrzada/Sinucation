namespace FriendSlop.Core
{
    /// <summary>
    /// Identificadores dos slots cosméticos (COS-001). Só visuais: nenhum sistema de regras os lê (COS-004).
    /// </summary>
    public static class CosmeticIds
    {
        public static readonly string[] Hats = { "Boné da casa", "Sem chapéu", "Cartola", "Chapéu de festa", "Coroa", "Gorro" };
        public static readonly string[] Accessories = { "Sem acessório", "Óculos", "Laço", "Medalha", "Cachecol", "Mochila" };

        public static int HatCount => Hats.Length;
        public static int AccessoryCount => Accessories.Length;
    }
}
