using System.Collections;
using System.Collections.Generic;
using System.IO;
using FriendSlop.Core;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Jukebox online (YouTube). O anfitrião é a autoridade da fila, pausa e relógio; o estado viaja numa mensagem
    /// própria só quando muda (Revision) — nunca nos snapshots de 10 Hz nem como áudio. Cada cliente toca no player
    /// oficial e corrige o desvio pela hora do servidor. Qualquer falha fica isolada: a partida nunca espera pela música.
    /// </summary>
    public sealed partial class NetGame
    {
        readonly Dictionary<ulong, double> _musicAddTimes = new Dictionary<ulong, double>();
        readonly HashSet<ulong> _musicLoading = new HashSet<ulong>();
        readonly Dictionary<ulong, double> _musicControlTimes = new Dictionary<ulong, double>();
        int _sentOnlineRevision = -1;
        float _onlineSendTimer;
        // Versões alternativas do pedido bloqueado (raiz = vídeo pedido originalmente).
        string _altRoot;
        readonly HashSet<string> _altTried = new HashSet<string>();
        readonly Queue<OnlineTrack> _altQueue = new Queue<OnlineTrack>();
        bool _altSearching;
        const int MaxAlternativeTries = 4;
        // Faixa cuja espera pelo anúncio do anfitrião já terminou (não volta a esperar).
        OnlineTrack _adHoldDone;

        /// <summary>O player oficial deste computador está activo (no anfitrião decide quem pode avançar a fila).</summary>
        public bool LocalOnlinePlayerActive { get; set; }

        // Jogadores cuja página ainda carrega ou mostra um anúncio no início da faixa (cliente → vídeo).
        readonly Dictionary<ulong, string> _adBusy = new Dictionary<ulong, string>();

        /// <summary>
        /// Este jogador ainda não está pronto para a faixa (página a carregar ou anúncio do YouTube). A sala espera pelo
        /// último jogador, com o relógio parado no início, para todos começarem a música ao mesmo tempo.
        /// </summary>
        public void ReportOnlineMusicBusy(string video, bool busy) => OnlineMusicBusyRpc(video ?? "", busy);

        [Rpc(SendTo.Server)]
        void OnlineMusicBusyRpc(string video, bool busy, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (busy && video.Length == 11 && SlotOf(sender)?.Connected == true) _adBusy[sender] = video;
            else _adBusy.Remove(sender);
        }

        bool AnyoneBusyWith(string video)
        {
            foreach (var entry in _adBusy)
                if (entry.Value == video && SlotOf(entry.Key)?.Connected == true) return true;
            return false;
        }

        /// <summary>Mensagem só para este cliente (pedido recusado, link inválido…).</summary>
        public event System.Action<string> MusicNotice;

        /// <summary>Põe um link na fila partilhada; <paramref name="device"/> diz onde foi escolhido (jukebox ou DVD).</summary>
        public void AddYouTube(string url, byte device = OnlineTrack.FromJukebox)
        {
            url = url?.Trim();
            if (string.IsNullOrEmpty(url)) return;
            if (url.Length > 512 || !YouTubeLink.TryParse(url, out _, out _))
            {
                MusicNotice?.Invoke("Link inválido: cole um link de vídeo ou playlist do YouTube.");
                return;
            }
            AddYouTubeRpc(url, device);
        }

        public void ControlOnlineMusic(OnlineMusicCommand command, int index = -1) => OnlineMusicControlRpc((int)command, index);

        /// <summary>O que este cliente pode fazer (espelha as regras do servidor, só para a UI).</summary>
        public bool CanAddOnlineMusic => !View.Music.Online.Locked && (!View.Music.Online.HostOnly || IsServer);
        public bool CanControlOnlineMusic => !View.Music.Online.HostOnly || IsServer;
        /// <summary>Pular/voltar música (YouTube e rádio): o anfitrião sempre; os outros se não estiver bloqueado.</summary>
        public bool CanSkipMusic => !View.Music.Online.SkipLocked || IsServer;
        public bool CanRemoveOnlineTrack(int index)
        {
            var queue = View.Music.Online.Queue;
            return index >= 0 && index < queue.Count && (IsServer || queue[index].AddedBy == LocalClientId);
        }

        const string SkipLockedNotice = "O anfitrião bloqueou o pular música — só ele pode passar à seguinte.";

        void Notify(ulong client, string text)
        {
            if (client == NetworkManager.LocalClientId) MusicNotice?.Invoke(text);
            else MusicNoticeRpc(text, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void MusicNoticeRpc(string text, RpcParams rpcParams) => MusicNotice?.Invoke(text);

        [Rpc(SendTo.Server)]
        void AddYouTubeRpc(string url, byte device, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var slot = SlotOf(sender);
            var state = View.Music.Online;
            if (slot == null || !slot.Connected || url == null || url.Length > 512) return;
            if (state.Locked) { Notify(sender, "O anfitrião bloqueou novas músicas."); return; }
            if (state.HostOnly && !IsHostSender(sender)) { Notify(sender, "Jukebox em modo \"só anfitrião\"."); return; }
            double now = NetworkManager.ServerTime.Time;
            if (_musicLoading.Contains(sender)) { Notify(sender, "Ainda a carregar o teu último link…"); return; }
            if (_musicAddTimes.TryGetValue(sender, out var last) && now - last < OnlineMusicState.AddCooldownSeconds)
            {
                Notify(sender, "Espera " + Mathf.CeilToInt((float)(OnlineMusicState.AddCooldownSeconds - (now - last))) + " s antes de adicionar outra.");
                return;
            }
            int room = Mathf.Min(OnlineMusicState.MaxQueue - state.Queue.Count, OnlineMusicState.MaxPerPlayer - state.CountBy(sender));
            if (room <= 0)
            {
                Notify(sender, state.Queue.Count >= OnlineMusicState.MaxQueue ? "A fila está cheia." : "Já tens " + OnlineMusicState.MaxPerPlayer + " músicas na fila.");
                return;
            }
            _musicAddTimes[sender] = now;
            _musicLoading.Add(sender);
            StartCoroutine(ResolveYouTube(url, sender, slot.Profile.Name, room, device == OnlineTrack.FromDvd ? OnlineTrack.FromDvd : OnlineTrack.FromJukebox));
        }

        IEnumerator ResolveYouTube(string url, ulong sender, string name, int room, byte device)
        {
            List<OnlineTrack> resolved = null; string failure = null;
            // Uma falha (rede, quota, excepção) nunca pode deixar o jogador preso em "a carregar".
            yield return Guarded(YouTubeCatalog.Resolve(url, sender, name, room, (tracks, error) => { resolved = tracks; failure = error; }), e =>
            {
                Debug.LogWarning("[Jukebox] " + e.Message);
                failure = "Não foi possível ler o link agora.";
            });
            _musicLoading.Remove(sender);
            if (!IsSpawned || !IsServer) yield break;
            var state = View.Music.Online;
            if (resolved == null || resolved.Count == 0) { Notify(sender, failure ?? "Nenhum vídeo disponível."); yield break; }
            if (state.Locked || (state.HostOnly && !IsHostSender(sender)) || SlotOf(sender)?.Connected != true) yield break;
            int added = 0;
            foreach (var track in resolved)
            {
                if (state.Queue.Count >= OnlineMusicState.MaxQueue || state.CountBy(sender) >= OnlineMusicState.MaxPerPlayer) break;
                track.Device = device;
                state.Queue.Add(track);
                added++;
            }
            if (added == 0) { Notify(sender, "A fila está cheia."); yield break; }
            if (state.Current == null) AdvanceOnlineMusic();
            state.Revision++;
            string where = device == OnlineTrack.FromDvd ? "no DVD" : "na jukebox";
            BroadcastToast(added == 1 ? name + " pôs " + where + ": " + resolved[0].Title : name + " pôs " + added + " vídeos " + where + ".");
            if (failure != null) Notify(sender, "Alguns itens foram saltados: " + failure);
        }

        /// <summary>Corre uma corrotina (e as aninhadas) apanhando excepções, para a jukebox nunca ficar pendurada.</summary>
        static IEnumerator Guarded(IEnumerator job, System.Action<System.Exception> onError)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(job);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (System.Exception e)
                {
                    onError(e);
                    yield break;
                }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return top.Current;
            }
        }

        void AdvanceOnlineMusic()
        {
            var state = View.Music.Online;
            state.Previous = state.Current;
            state.Current = state.Queue.Count > 0 ? state.Queue[0] : null;
            if (state.Current != null) state.Queue.RemoveAt(0);
            state.Offset = 0;
            state.StartedAt = NetworkManager.ServerTime.Time;
            state.Paused = false;
            state.AdHold = false;
            state.Revision++;
            // Fila vazia: volta às faixas CC0 locais a partir de uma faixa nova.
            if (state.Current == null && TrackCount > 0) NextTrack(state.StartedAt);
        }

        [Rpc(SendTo.Server)]
        void OnlineMusicControlRpc(int command, int index, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (SlotOf(sender)?.Connected != true) return;
            var state = View.Music.Online;
            bool host = IsHostSender(sender);
            var cmd = (OnlineMusicCommand)command;
            bool ownItem = cmd == OnlineMusicCommand.Remove && index >= 0 && index < state.Queue.Count && state.Queue[index].AddedBy == sender;
            bool hostOnlyCommand = cmd == OnlineMusicCommand.Clear || cmd == OnlineMusicCommand.Lock || cmd == OnlineMusicCommand.HostOnly || cmd == OnlineMusicCommand.SkipLock ||
                                   (cmd == OnlineMusicCommand.Remove && !ownItem);
            if (!host && (hostOnlyCommand || (state.HostOnly && !ownItem))) { Notify(sender, "Só o anfitrião pode fazer isso."); return; }
            if (!host && state.SkipLocked && (cmd == OnlineMusicCommand.Next || cmd == OnlineMusicCommand.Previous)) { Notify(sender, SkipLockedNotice); return; }
            double now = NetworkManager.ServerTime.Time;
            if (_musicControlTimes.TryGetValue(sender, out var last) && now - last < OnlineMusicState.ControlCooldownSeconds) return;
            _musicControlTimes[sender] = now;
            switch (cmd)
            {
                case OnlineMusicCommand.PlayPause:
                    if (state.Current == null) return;
                    state.Offset = state.Position(now);
                    state.StartedAt = now;
                    state.Paused = !state.Paused;
                    break;
                case OnlineMusicCommand.Next:
                    if (state.Current == null) return;
                    AdvanceOnlineMusic();
                    BroadcastToast((SlotOf(sender)?.Profile.Name ?? "Alguém") + " saltou a música.");
                    break;
                case OnlineMusicCommand.Previous:
                    if (state.Previous == null) return;
                    if (state.Current != null && state.Queue.Count < OnlineMusicState.MaxQueue) state.Queue.Insert(0, state.Current);
                    state.Current = state.Previous;
                    state.Previous = null;
                    state.Offset = 0;
                    state.StartedAt = now;
                    state.Paused = false;
                    state.AdHold = false;
                    break;
                case OnlineMusicCommand.Clear: state.Queue.Clear(); break;
                case OnlineMusicCommand.Lock: state.Locked = !state.Locked; break;
                case OnlineMusicCommand.HostOnly: state.HostOnly = !state.HostOnly; break;
                case OnlineMusicCommand.SkipLock:
                    state.SkipLocked = !state.SkipLocked;
                    BroadcastToast(state.SkipLocked ? "O anfitrião bloqueou o pular música." : "Já todos podem pular músicas.");
                    break;
                case OnlineMusicCommand.Remove:
                    if (index < 0 || index >= state.Queue.Count) return;
                    state.Queue.RemoveAt(index);
                    break;
                default: return;
            }
            state.Revision++;
        }

        /// <summary>Fim ou erro no player oficial deste cliente.</summary>
        public void ReportOnlineMusicEnd(string video, bool failed, int code = 0) => OnlineMusicEndedRpc(video, failed, code);

        [Rpc(SendTo.Server)]
        void OnlineMusicEndedRpc(string video, bool failed, int code, RpcParams rpcParams = default)
        {
            // Com o player do anfitrião activo, só ele avança a fila (um bloqueio regional num cliente fica local a esse
            // cliente). Sem ele, vale o primeiro cliente que reportar — senão um vídeo bloqueado ficava "a tocar" mudo.
            ulong sender = rpcParams.Receive.SenderClientId;
            if (View.Music.Online.Current?.VideoId != video || SlotOf(sender)?.Connected != true) return;
            if (!IsHostSender(sender) && LocalOnlinePlayerActive) return;
            // A página oficial toca o que as editoras bloqueiam nos players incorporados; o que ainda falha ali (pede
            // login por idade/verificação, indisponível) também falha para os outros: em vez de saltar, procura a mesma
            // música noutro upload. 101/150/153 ficam por compatibilidade com o player incorporado antigo.
            if (failed && YouTubeWatchPage.ShouldSearchAlternative(code)) { TryAlternative(); return; }
            if (failed) BroadcastToast("Vídeo indisponível (privado ou removido) — a seguir para o próximo.");
            AdvanceOnlineMusic();
        }

        void TryAlternative()
        {
            var state = View.Music.Online;
            var current = state.Current;
            if (current == null || _altSearching) return;
            string root = string.IsNullOrEmpty(current.OriginalId) ? current.VideoId : current.OriginalId;
            if (_altRoot != root)
            {
                _altRoot = root;
                _altTried.Clear();
                _altQueue.Clear();
                _altTried.Add(root);
                BroadcastToast("\"" + current.Title + "\" não toca no jogo (o YouTube pede login ou não o deixa aqui) — a procurar outra versão…");
                _altSearching = true;
                StartCoroutine(Guarded(FindAlternativesFor(current, root), e =>
                {
                    Debug.LogWarning("[Jukebox] " + e.Message);
                    _altSearching = false;
                    SkipBlocked(root, "Não foi possível procurar outra versão.");
                }));
                return;
            }
            PlayNextAlternative(root);
        }

        IEnumerator FindAlternativesFor(OnlineTrack original, string root)
        {
            List<OnlineTrack> found = null; string error = null;
            yield return YouTubeCatalog.FindAlternatives(original, _altTried, MaxAlternativeTries, (list, e) => { found = list; error = e; });
            _altSearching = false;
            if (!IsSpawned || !IsServer || _altRoot != root) yield break;
            if (found != null) foreach (var t in found) _altQueue.Enqueue(t);
            if (_altQueue.Count == 0) { SkipBlocked(root, error); yield break; }
            PlayNextAlternative(root);
        }

        void PlayNextAlternative(string root)
        {
            var state = View.Music.Online;
            var current = state.Current;
            if (current == null || (current.VideoId != root && current.OriginalId != root)) return;
            if (_altQueue.Count == 0 || _altTried.Count > MaxAlternativeTries) { SkipBlocked(root, "Nenhuma versão desta música toca no jogo (abre-a no YouTube)."); return; }
            var alt = _altQueue.Dequeue();
            _altTried.Add(alt.VideoId);
            alt.OriginalId = root;
            alt.AddedBy = current.AddedBy;
            alt.AddedByName = current.AddedByName;
            alt.Device = current.Device;
            state.Current = alt;
            state.Offset = 0;
            state.StartedAt = NetworkManager.ServerTime.Time;
            state.Paused = false;
            state.AdHold = false;
            state.Revision++;
            BroadcastToast("A tocar outra versão: " + alt.Title + " (" + alt.Channel + ")");
        }

        void SkipBlocked(string root, string reason)
        {
            var current = View.Music.Online.Current;
            if (current == null || (current.VideoId != root && current.OriginalId != root)) return;
            BroadcastToast((reason ?? "Este vídeo não toca no jogo.") + " A seguir para o próximo.");
            AdvanceOnlineMusic();
        }

        /// <summary>
        /// A página oficial informa a duração que o oEmbed não dá (e o título/canal de vídeos que entraram sem
        /// metadados); aceita-se a primeira informação plausível de cada campo.
        /// </summary>
        public void ReportOnlineMusicMeta(string video, double seconds, string title, string channel) =>
            OnlineMusicMetaRpc(video, seconds, OnlineTrack.Clip(title, OnlineTrack.MaxTitle), OnlineTrack.Clip(channel, OnlineTrack.MaxChannel));

        [Rpc(SendTo.Server)]
        void OnlineMusicMetaRpc(string video, double seconds, string title, string channel, RpcParams rpcParams = default)
        {
            var state = View.Music.Online;
            var current = state.Current;
            if (SlotOf(rpcParams.Receive.SenderClientId)?.Connected != true || current == null || current.VideoId != video) return;
            bool changed = false;
            if (current.Duration <= 0 && !double.IsNaN(seconds) && seconds >= 1 && seconds <= 12 * 3600) { current.Duration = seconds; changed = true; }
            if (current.Title == OnlineTrack.PlaceholderTitle && !string.IsNullOrWhiteSpace(title))
            {
                current.Title = OnlineTrack.Clip(title.Trim(), OnlineTrack.MaxTitle);
                if (string.IsNullOrEmpty(current.Channel) && !string.IsNullOrWhiteSpace(channel)) current.Channel = OnlineTrack.Clip(channel.Trim(), OnlineTrack.MaxChannel);
                changed = true;
            }
            if (changed) state.Revision++;
        }

        void UpdateOnlineMusic()
        {
            var state = View.Music.Online;
            double now = NetworkManager.ServerTime.Time;
            // Faixa acabada (ou player parado/sem internet): a fila nunca fica presa.
            if (state.Finished(now)) AdvanceOnlineMusic();
            // Espera pelos anúncios/carregamentos: uma vez por faixa e com limite (a sala nunca fica presa a ninguém).
            bool canHold = state.Current != null && !state.Paused && !ReferenceEquals(_adHoldDone, state.Current);
            bool hold = canHold && AnyoneBusyWith(state.Current.VideoId) && (state.AdHold ? now - state.StartedAt < OnlineMusicState.AdHoldLimit : state.Position(now) < 5);
            if (state.AdHold && !hold) _adHoldDone = state.Current;
            if (state.SetAdHold(hold, now)) state.Revision++;

            _onlineSendTimer -= Time.unscaledDeltaTime;
            if (state.Revision != _sentOnlineRevision && _onlineSendTimer <= 0f)
            {
                _sentOnlineRevision = state.Revision;
                _onlineSendTimer = 0.2f;
                if (NetworkManager.ConnectedClientsIds.Count > 1)
                {
                    using (var ms = new MemoryStream(1024))
                    using (var w = new BinaryWriter(ms))
                    {
                        state.Write(w);
                        w.Flush();
                        OnlineMusicSyncRpc(ms.ToArray());
                    }
                }
            }
        }

        /// <summary>Quem entra a meio recebe logo a faixa actual, a posição (pela hora do servidor) e a fila.</summary>
        void ResendOnlineMusic() => _sentOnlineRevision = -1;

        [Rpc(SendTo.NotServer)]
        void OnlineMusicSyncRpc(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var r = new BinaryReader(ms))
                    View.Music.Online.Read(r);
            }
            catch (System.Exception e) when (e is EndOfStreamException || e is InvalidDataException)
            {
                // Pacote malformado: mantém o estado anterior.
            }
        }
    }
}
