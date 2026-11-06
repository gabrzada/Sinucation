using System;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Modal de carregamento: fundo escurecido por cima de tudo (o jogador não vê objectos a aparecer nem os primeiros
    /// frames a compilar shaders), emblema, spinner, título e progresso. Fecha quando o trabalho acaba E os frames
    /// estabilizam (vários seguidos abaixo de 40 ms), nunca antes do tempo mínimo; tem um limite de segurança.
    /// </summary>
    public sealed partial class HudView
    {
        string _loadingTitle, _loadingDetail;
        Func<bool> _loadingDone;
        Func<float> _loadingProgress;
        Action _loadingWork;
        bool _loadingWorkDone;
        int _loadingDrawnFrames, _stableFrames;
        float _loadingMinUntil, _loadingLimit, _loadingAlpha, _loadingShownProgress;

        public bool LoadingVisible => _loadingTitle != null || _loadingAlpha > 0.01f;

        /// <summary>Mostra o modal até <paramref name="done"/> devolver true (e os frames estabilizarem).</summary>
        public void ShowLoading(string title, Func<bool> done, float minSeconds = 0.35f, Func<float> progress = null, string detail = null)
        {
            _loadingTitle = title;
            _loadingDetail = detail;
            _loadingDone = done;
            _loadingProgress = progress;
            _loadingWork = null;
            _loadingWorkDone = true;
            _loadingDrawnFrames = 0;
            _stableFrames = 0;
            _loadingMinUntil = Time.unscaledTime + minSeconds;
            _loadingLimit = Time.unscaledTime + 25f;
            _loadingShownProgress = 0f;
        }

        /// <summary>
        /// Mostra o modal primeiro e só depois (quando já foi desenhado) executa <paramref name="work"/> — para trabalho
        /// pesado na thread principal (aplicar gráficos) não congelar o ecrã com a cena a meio.
        /// </summary>
        public void RunWithLoading(string title, Action work, float minSeconds = 0.6f, string detail = null)
        {
            ShowLoading(title, () => _loadingWorkDone, minSeconds, null, detail);
            _loadingWork = work;
            _loadingWorkDone = false;
        }

        void UpdateLoading()
        {
            if (_loadingTitle == null)
            {
                _loadingAlpha = Mathf.MoveTowards(_loadingAlpha, 0f, Time.unscaledDeltaTime * 4f);
                return;
            }
            _loadingAlpha = Mathf.MoveTowards(_loadingAlpha, 1f, Time.unscaledDeltaTime * 10f);
            if (_loadingWork != null && _loadingDrawnFrames >= 2)
            {
                var work = _loadingWork;
                _loadingWork = null;
                try { work(); }
                catch (Exception e) { Debug.LogException(e); }
                _loadingWorkDone = true;
                _stableFrames = 0;
            }
            // Frames estáveis = já não há compilação de shaders nem uploads a meio.
            _stableFrames = Time.unscaledDeltaTime < 0.04f ? _stableFrames + 1 : 0;
            bool done = _loadingDone == null || _loadingDone();
            if ((done && Time.unscaledTime >= _loadingMinUntil && _stableFrames >= 10) || Time.unscaledTime > _loadingLimit)
                _loadingTitle = null;
        }

        void DrawLoading()
        {
            if (_loadingAlpha <= 0.01f) return;
            if (Event.current.type == EventType.Repaint && _loadingTitle != null) _loadingDrawnFrames++;
            float a = _loadingAlpha;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.02f, 0.03f, 0.06f, 0.82f));

            float w = 720f, h = 250f;
            var card = new Rect((_vw - w) * 0.5f, (_vh - h) * 0.5f, w, h);
            Plate(card, NavyDark, true, 22f);
            Logo(new Rect(card.x + 30f, card.y + 40f, 170f, 170f));

            // Spinner: três arcos (as cores das formas do emblema) a rodar.
            // Spinner à direita, a meio do cartão (longe do título).
            var spin = new Rect(card.xMax - 88f, card.center.y - 28f, 56f, 56f);
            float t = Time.unscaledTime;
            Color[] colors = { Mustard, Coral, Teal };
            for (int i = 0; i < 3; i++)
            {
                float angle = t * 280f + i * 120f;
                var dot = new Vector2(spin.center.x + Mathf.Cos(angle * Mathf.Deg2Rad) * 20f, spin.center.y + Mathf.Sin(angle * Mathf.Deg2Rad) * 20f);
                float size = 12f + 4f * Mathf.Sin(t * 6f + i * 2f);
                Round(new Rect(dot.x - size * 0.5f, dot.y - size * 0.5f, size, size), colors[i], size * 0.5f);
            }

            float tx = card.x + 226f, tw = card.xMax - 120f - tx;
            string title = _loadingTitle ?? "PRONTO";
            T(new Rect(tx, card.y + 40f, tw, 60f), title, 38, Mustard, TextAnchor.MiddleLeft, true, true);
            int dots = 1 + (int)(t * 2.5f) % 3;
            string detail = _loadingDetail ?? (_loadingWorkDone ? "A estabilizar a imagem" : "Só um instante");
            T(new Rect(tx, card.y + 104f, tw, 34f), detail + new string('.', dots), 22, Ivory, TextAnchor.MiddleLeft);

            // Barra de progresso (ou barra indeterminada quando não há medida).
            var bar = new Rect(tx, card.y + 160f, tw, 16f);
            Round(bar, Navy, 8f);
            if (_loadingProgress != null)
            {
                _loadingShownProgress = Mathf.MoveTowards(_loadingShownProgress, Mathf.Clamp01(_loadingProgress()), Time.unscaledDeltaTime * 1.5f);
                Round(new Rect(bar.x, bar.y, Mathf.Max(16f, bar.width * _loadingShownProgress), bar.height), Mustard, 8f);
                T(new Rect(bar.x, bar.yMax + 6f, bar.width, 24f), Mathf.RoundToInt(_loadingShownProgress * 100f) + "%", 18, Muted, TextAnchor.MiddleRight, true);
            }
            else
            {
                float seg = bar.width * 0.3f;
                float x = bar.x + Mathf.PingPong(t * bar.width * 0.9f, bar.width - seg);
                Round(new Rect(x, bar.y, seg, bar.height), Mustard, 8f);
            }
            GUI.color = old;

            // O modal engole cliques e teclas: nada por baixo reage enquanto carrega.
            if (_loadingTitle != null && (Event.current.isMouse || Event.current.isKey || Event.current.type == EventType.ScrollWheel)) Event.current.Use();
        }
    }
}
