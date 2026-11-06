using UnityEngine;

namespace FriendSlop.Presentation
{
    public static class GameplayCollision
    {
        public const int Player = 8;
        public const int TableBarrier = 9;
        public const int Throwable = 10;
        public const int BilliardBall = 11;
        public const int WorldQueryMask = ~(1 << TableBarrier);

        public static void Configure()
        {
            for (int layer = 0; layer < 32; layer++)
                Physics.IgnoreLayerCollision(TableBarrier, layer, layer != Player);
            Physics.IgnoreLayerCollision(Throwable, BilliardBall, true);
        }
    }
}
