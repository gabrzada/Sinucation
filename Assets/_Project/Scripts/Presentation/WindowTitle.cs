using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Título da janela com o nome do jogo ("Caçapa Russa"). O productName do Unity continua "FriendSlop" porque é
    /// ele que decide onde ficam as preferências e a pasta LocalLow (definições, chave do YouTube, logs das partidas).
    /// </summary>
    public static class WindowTitle
    {
        public const string GameName = "Caçapa Russa";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SetWindowTextW(IntPtr hWnd, string text);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
            try
            {
                var hwnd = GetActiveWindow();
                if (hwnd != IntPtr.Zero) SetWindowTextW(hwnd, GameName + "  v" + Application.version);
            }
            catch (Exception) { /* Sem título personalizado: fica o do executável. */ }
        }
#endif
    }
}
