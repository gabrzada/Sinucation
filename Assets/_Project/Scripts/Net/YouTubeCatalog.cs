using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using FriendSlop.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace FriendSlop.Net
{
    /// <summary>
    /// Resolve links do YouTube em itens da fila (título, canal, duração), só com metadados públicos — nunca descarrega
    /// vídeo nem áudio. Com chave da YouTube Data API (opcional, só no anfitrião) usa a API; sem chave usa o oEmbed
    /// público para vídeos e o player oficial do anfitrião para listar playlists. A duração que faltar é informada
    /// pelo player oficial quando o vídeo carrega.
    /// </summary>
    public static class YouTubeCatalog
    {
        [Serializable] sealed class Config { public string ApiKey = ""; }
        [Serializable] sealed class Response { public Item[] items; public string nextPageToken; }
        [Serializable] sealed class Item { public string id; public Snippet snippet; public Details contentDetails; public Status status; }
        [Serializable] sealed class Snippet { public string title, channelTitle; public Resource resourceId; }
        [Serializable] sealed class Resource { public string videoId; }
        [Serializable] sealed class Details { public string duration; }
        [Serializable] sealed class Status { public bool embeddable, madeForKids; public string privacyStatus; }
        [Serializable] sealed class OEmbed { public string title, author_name; }
        [Serializable] sealed class SearchResponse { public SearchItem[] items; }
        [Serializable] sealed class SearchItem { public SearchId id; }
        [Serializable] sealed class SearchId { public string videoId; }

        static readonly System.Text.RegularExpressions.Regex Noise = new System.Text.RegularExpressions.Regex(
            @"[\(\[][^\)\]]*(official|video|audio|remaster|lyric|visuali[sz]er|hd|4k|mv|clipe)[^\)\]]*[\)\]]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Outras versões da mesma música, para quando o vídeo pedido não toca no jogo (pede login por idade ou
        /// verificação, indisponível aqui). Precisa da chave da Data API. Filtra pela duração (±35%) para não trocar a
        /// música por outra coisa. Não filtra por "incorporável": o jogo usa a página oficial, não o player incorporado.
        /// </summary>
        public static IEnumerator FindAlternatives(OnlineTrack original, ICollection<string> exclude, int max, Action<List<OnlineTrack>, string> done)
        {
            var found = new List<OnlineTrack>();
            string key = ReadKey();
            if (key.Length == 0) { done(found, "Sem chave da YouTube Data API no anfitrião para procurar outra versão."); yield break; }
            string query = Noise.Replace(original.Title ?? "", " ").Replace("\"", " ").Trim();
            string channel = (original.Channel ?? "").Replace("VEVO", "").Replace(" - Topic", "").Trim();
            if (channel.Length > 0 && query.IndexOf(channel, StringComparison.OrdinalIgnoreCase) < 0) query = channel + " " + query;
            if (query.Length == 0) { done(found, "Sem título para procurar outra versão."); yield break; }

            SearchResponse search = null;
            using (var request = UnityWebRequest.Get("https://www.googleapis.com/youtube/v3/search?part=id&type=video&maxResults=8&q=" +
                                                     Uri.EscapeDataString(query) + "&key=" + Uri.EscapeDataString(key)))
            {
                request.timeout = 12;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    done(found, request.responseCode == 403 ? "YouTube recusou a pesquisa (quota ou chave da Data API)." : "Sem ligação ao YouTube para procurar outra versão.");
                    yield break;
                }
                try { search = JsonUtility.FromJson<SearchResponse>(request.downloadHandler.text); } catch (Exception) { }
            }
            var ids = new List<string>();
            if (search?.items != null)
                foreach (var item in search.items)
                {
                    string id = item?.id?.videoId;
                    if (!string.IsNullOrEmpty(id) && id != original.VideoId && !exclude.Contains(id) && !ids.Contains(id)) ids.Add(id);
                }
            if (ids.Count == 0) { done(found, "Não há outra versão desta música que toque no jogo."); yield break; }

            var candidates = new List<OnlineTrack>();
            yield return ResolveWithApi(key, "", "", original.AddedBy, original.AddedByName, ids.Count, candidates, _ => { }, ids);
            foreach (var track in candidates)
            {
                if (found.Count >= max) break;
                if (original.Duration > 0 && track.Duration > 0 && Math.Abs(track.Duration - original.Duration) > original.Duration * 0.35) continue;
                found.Add(track);
            }
            done(found, found.Count == 0 ? "Não há outra versão desta música que toque no jogo." : null);
        }

        /// <summary>
        /// Lista os IDs de uma playlist através do player oficial (fornecido pela apresentação quando o player do
        /// anfitrião está activo): (playlistId, máximo, concluído(ids, erro)).
        /// </summary>
        public static Func<string, int, Action<List<string>, string>, IEnumerator> PlaylistExpander;

        public static string ConfigPath => Path.Combine(Application.persistentDataPath, "youtube.local.json");

        public static string ReadKey()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("FRIENDSLOP_YOUTUBE_API_KEY");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
                if (!File.Exists(ConfigPath)) { File.WriteAllText(ConfigPath, "{\"ApiKey\":\"\"}"); return ""; }
                return JsonUtility.FromJson<Config>(File.ReadAllText(ConfigPath))?.ApiKey?.Trim() ?? "";
            }
            catch (Exception) { return ""; }
        }

        public static IEnumerator Resolve(string url, ulong sender, string name, int maxItems, Action<List<OnlineTrack>, string> done)
        {
            var tracks = new List<OnlineTrack>();
            if (!YouTubeLink.TryParse(url, out var video, out var playlist)) { done(tracks, "Link inválido: use um link de vídeo ou playlist do YouTube."); yield break; }
            bool isPlaylist = YouTubeLink.IsPlaylistRequest(video, playlist);
            maxItems = Mathf.Clamp(maxItems, 1, OnlineMusicState.PlaylistBatch);
            string key = ReadKey();

            if (key.Length > 0)
            {
                yield return ResolveWithApi(key, isPlaylist ? "" : video, isPlaylist ? playlist : "", sender, name, maxItems, tracks, e => done(tracks, e));
                yield break;
            }

            var ids = new List<string>();
            if (!isPlaylist) ids.Add(video);
            else
            {
                // Feed público oficial da playlist (até 15 vídeos, sem chave); recurso: o player oficial do anfitrião.
                string error = null;
                yield return FeedLookup(playlist, maxItems * 2, ids, e => error = e);
                if (ids.Count == 0 && PlaylistExpander != null)
                    yield return PlaylistExpander(playlist, maxItems, (list, e) => { if (list != null) ids.AddRange(list); error = e; });
                if (ids.Count == 0) { done(tracks, error ?? "Playlist vazia, privada ou indisponível."); yield break; }
            }

            string lastError = null;
            foreach (string id in ids)
            {
                if (tracks.Count >= maxItems) break;
                OnlineTrack track = null;
                yield return OEmbedLookup(id, sender, name, (t, e) => { track = t; lastError = e; });
                if (track == null) continue; // item indisponível: salta para o próximo
                track.PlaylistId = isPlaylist ? playlist : "";
                tracks.Add(track);
            }
            done(tracks, tracks.Count == 0 ? lastError ?? "Nenhum vídeo disponível." : null);
        }

        static readonly System.Text.RegularExpressions.Regex FeedVideo = new System.Text.RegularExpressions.Regex("<yt:videoId>([A-Za-z0-9_-]{11})</yt:videoId>");

        static IEnumerator FeedLookup(string playlist, int max, List<string> ids, Action<string> fail)
        {
            using (var request = UnityWebRequest.Get("https://www.youtube.com/feeds/videos.xml?playlist_id=" + Uri.EscapeDataString(playlist)))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    fail("Sem ligação ao YouTube (rede ou tempo esgotado). A partida continua; tente outra vez.");
                    yield break;
                }
                if (request.responseCode == 404 || request.responseCode == 400) { fail("Playlist inexistente ou privada."); yield break; }
                if (request.responseCode != 200) { fail("YouTube indisponível (" + request.responseCode + ")."); yield break; }
                foreach (System.Text.RegularExpressions.Match m in FeedVideo.Matches(request.downloadHandler.text))
                {
                    if (ids.Count >= max) break;
                    if (!ids.Contains(m.Groups[1].Value)) ids.Add(m.Groups[1].Value);
                }
                if (ids.Count == 0) fail("Playlist vazia ou privada.");
            }
        }

        static IEnumerator OEmbedLookup(string id, ulong sender, string name, Action<OnlineTrack, string> done)
        {
            string target = Uri.EscapeDataString("https://www.youtube.com/watch?v=" + id);
            using (var request = UnityWebRequest.Get("https://www.youtube.com/oembed?format=json&url=" + target))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    done(null, "Sem ligação ao YouTube (rede ou tempo esgotado). A partida continua; tente outra vez.");
                    yield break;
                }
                long code = request.responseCode;
                // 401: o dono não deixa incorporar — o oEmbed não dá metadados, mas a página oficial toca-o e informa o
                // título ao começar (OnlineTrack.PlaceholderTitle até lá). 403: privado.
                if (code == 401)
                {
                    done(new OnlineTrack { VideoId = id, Title = OnlineTrack.PlaceholderTitle, AddedBy = sender, AddedByName = OnlineTrack.Clip(name, OnlineTrack.MaxName) }, null);
                    yield break;
                }
                if (code == 403) { done(null, "Vídeo privado."); yield break; }
                if (code == 404 || code == 400) { done(null, "Vídeo inexistente, removido ou privado."); yield break; }
                if (code != 200) { done(null, "YouTube indisponível (" + code + ")."); yield break; }
                OEmbed meta = null;
                try { meta = JsonUtility.FromJson<OEmbed>(request.downloadHandler.text); } catch (Exception) { }
                if (meta == null) { done(null, "Resposta inválida do YouTube."); yield break; }
                done(new OnlineTrack
                {
                    VideoId = id,
                    Title = OnlineTrack.Clip(meta.title, OnlineTrack.MaxTitle),
                    Channel = OnlineTrack.Clip(meta.author_name, OnlineTrack.MaxChannel),
                    AddedBy = sender,
                    AddedByName = OnlineTrack.Clip(name, OnlineTrack.MaxName)
                }, null);
            }
        }

        static IEnumerator ResolveWithApi(string key, string video, string playlist, ulong sender, string name, int maxItems, List<OnlineTrack> tracks, Action<string> done, List<string> knownIds = null)
        {
            var ids = new List<string>();
            if (knownIds != null) ids.AddRange(knownIds);
            else if (playlist.Length == 0) ids.Add(video);
            else
            {
                // Nunca pede uma lista sem limite: o mesmo máximo por adição/jogador.
                string endpoint = "playlistItems?part=snippet&maxResults=" + Mathf.Min(50, maxItems * 2) + "&playlistId=" + Uri.EscapeDataString(playlist);
                Response response = null; string error = null;
                yield return Request(endpoint, key, (r, e) => { response = r; error = e; });
                if (response == null) { done(error); yield break; }
                if (response.items != null)
                    foreach (var item in response.items)
                        if (!string.IsNullOrEmpty(item.snippet?.resourceId?.videoId)) ids.Add(item.snippet.resourceId.videoId);
            }
            if (ids.Count == 0) { done("Playlist vazia, privada ou indisponível."); yield break; }
            Response details = null; string failure = null;
            yield return Request("videos?part=snippet,contentDetails,status&id=" + Uri.EscapeDataString(string.Join(",", ids)), key,
                (r, e) => { details = r; failure = e; });
            if (details == null) { done(failure); yield break; }
            var byId = new Dictionary<string, Item>();
            if (details.items != null) foreach (var item in details.items) if (item.id != null) byId[item.id] = item;
            foreach (string id in ids)
            {
                if (tracks.Count >= maxItems) break;
                // Removidos e privados são saltados. Vídeos que a editora não deixa incorporar entram: tocam na página
                // oficial do YouTube (o que só falha lá — login por idade — é tratado ao tocar).
                if (!byId.TryGetValue(id, out var item) || item.status == null || item.status.privacyStatus == "private") continue;
                double duration;
                try { duration = XmlConvert.ToTimeSpan(item.contentDetails.duration).TotalSeconds; }
                catch (Exception) { duration = 0; }
                tracks.Add(new OnlineTrack
                {
                    VideoId = id, PlaylistId = playlist,
                    Title = OnlineTrack.Clip(item.snippet?.title, OnlineTrack.MaxTitle),
                    Channel = OnlineTrack.Clip(item.snippet?.channelTitle, OnlineTrack.MaxChannel),
                    Duration = duration, AddedBy = sender, AddedByName = OnlineTrack.Clip(name, OnlineTrack.MaxName)
                });
            }
            done(tracks.Count == 0 ? "Nenhum vídeo disponível (privado ou removido)." : null);
        }

        static IEnumerator Request(string endpoint, string key, Action<Response, string> done)
        {
            using (var request = UnityWebRequest.Get("https://www.googleapis.com/youtube/v3/" + endpoint + "&key=" + Uri.EscapeDataString(key)))
            {
                request.timeout = 12;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    done(null, request.responseCode == 403 ? "YouTube recusou o pedido (chave ou quota da Data API)." :
                        request.responseCode == 404 ? "Playlist inexistente ou privada." :
                        "Sem ligação ao YouTube (rede ou tempo esgotado). A partida continua.");
                    yield break;
                }
                Response response = null;
                try { response = JsonUtility.FromJson<Response>(request.downloadHandler.text); } catch (Exception) { }
                done(response, response == null ? "Resposta inválida do YouTube." : null);
            }
        }
    }
}
