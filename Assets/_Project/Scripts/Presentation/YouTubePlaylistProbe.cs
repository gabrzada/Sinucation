using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using Gree.UnityWebView;
#endif

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Lista os vídeos de uma playlist sem chave da API, quando o feed público não chega (mixes, listas grandes): um
    /// player oficial (IFrame API) que nunca toca, criado só para esse pedido e destruído logo a seguir — nunca fica
    /// uma segunda instância do navegador ligada durante a partida. Só o anfitrião o usa (é quem resolve os links).
    /// </summary>
    public sealed class YouTubePlaylistProbe : MonoBehaviour
    {
        [Serializable] sealed class ProbeEvent { public string type, list; public string[] ids; public int code; }

        TcpListener _server;
        bool _ready;
        string _list;
        List<string> _ids;
        bool _answered;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        WebViewObject _browser;
#endif

        public IEnumerator Expand(string list, int max, Action<List<string>, string> done)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string error = null;
            try { Open(); }
            catch (Exception e) { error = "Player indisponível para ler a playlist (" + e.GetType().Name + ")."; }
            if (error != null) { Close(); done(null, error); yield break; }

            float until = Time.unscaledTime + 10f;
            while (!_ready && Time.unscaledTime < until) yield return null;
            if (!_ready) { Close(); done(null, "O YouTube não respondeu a tempo (rede)."); yield break; }

            _list = list;
            // O ID já foi validado por YouTubeLink (só letras, dígitos, "-" e "_"): seguro dentro de aspas.
            _browser.EvaluateJS("window.fsExpand && fsExpand('" + list + "'," + max + ")");
            until = Time.unscaledTime + 12f;
            while (!_answered && Time.unscaledTime < until) yield return null;
            var ids = _ids;
            Close();
            if (!_answered) done(null, "O YouTube não respondeu a tempo (rede).");
            else done(ids, ids == null || ids.Count == 0 ? "Playlist vazia, privada ou indisponível." : null);
#else
            done(null, "O player integrado está disponível na versão Windows.");
            yield break;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        void Open()
        {
            Close();
            _ready = _answered = false;
            _ids = null;
            string html = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Jukebox/probe.html"));
            // Página servida por HTTP local (origem válida para a IFrame API; file:// é recusado pelo YouTube).
            string path = "/" + Guid.NewGuid().ToString("N") + "/probe.html";
            _server = new TcpListener(IPAddress.Loopback, 0);
            _server.Start(4);
            string url = "http://127.0.0.1:" + ((IPEndPoint)_server.LocalEndpoint).Port + path;
            _ = ServePage(_server, path, html);
            _browser = new GameObject("YouTubePlaylistProbe").AddComponent<WebViewObject>();
            _browser.transform.SetParent(transform, false);
            _browser.Init(cb: OnMessage);
            _browser.SetURLPattern("", "", @"^(?!http://127\.0\.0\.1:\d+/|https://www\.youtube\.com/)");
            _browser.captureTexture = false;
            _browser.drawTexture = false;
            _browser.SetMargins(0, 0, Mathf.Max(0, Screen.width - 220), Mathf.Max(0, Screen.height - 220));
            _browser.SetVisibility(true);
            _browser.LoadURL(url);
        }

        void OnMessage(string json)
        {
            ProbeEvent e;
            try { e = JsonUtility.FromJson<ProbeEvent>(json); } catch (Exception) { return; }
            if (e == null) return;
            if (e.type == "probeReady") _ready = true;
            else if (e.type == "expanded" && e.list == _list)
            {
                _ids = new List<string>();
                if (e.ids != null) _ids.AddRange(e.ids);
                _answered = true;
            }
        }
#endif

        void Close()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser != null) Destroy(_browser.gameObject);
            _browser = null;
#endif
            _server?.Stop();
            _server = null;
        }

        static async Task ServePage(TcpListener listener, string path, string html)
        {
            try
            {
                while (true)
                {
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        var buffer = new byte[4096];
                        var read = stream.ReadAsync(buffer, 0, buffer.Length);
                        if (await Task.WhenAny(read, Task.Delay(2000)) != read) continue;
                        int count = await read;
                        string request = Encoding.ASCII.GetString(buffer, 0, count);
                        bool valid = request.StartsWith("GET " + path + " HTTP/", StringComparison.Ordinal);
                        byte[] body = Encoding.UTF8.GetBytes(valid ? html : "Not found");
                        byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + (valid ? "200 OK" : "404 Not Found") +
                            "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(head, 0, head.Length);
                        await stream.WriteAsync(body, 0, body.Length);
                    }
                }
            }
            catch (Exception) { /* O listener fecha com o pedido; a música nunca bloqueia o jogo. */ }
        }

        void OnDestroy() => Close();
    }
}
