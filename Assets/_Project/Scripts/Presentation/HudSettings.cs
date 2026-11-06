using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Pausa (concept 15) e definições com separadores GERAL/GRÁFICOS/ÁUDIO/CONTROLOS/ACESSIBILIDADE (concepts 16–19).
    /// Os gráficos editam um rascunho: só são aplicados em APLICAR (como no concept 17).
    /// </summary>
    public sealed partial class HudView
    {
        static readonly string[] SettingsTabs = { "GERAL", "GRÁFICOS", "ÁUDIO", "CONTROLOS", "ACESSIBILIDADE" };
        static readonly string[] PresetNames = { "BAIXO", "MÉDIO", "ALTO", "ULTRA" };

        bool _settingsOpen;
        int _settingsTab = 1;
        GraphicsOptions _gfxDraft;
        bool _gfxDirty;
        Vector2 _graphicsScroll;

        void OpenSettings()
        {
            _settingsOpen = true;
            _gfxDraft = GraphicsOptions.FromJson(_game.Settings.Graphics.ToJson());
            _gfxDirty = false;
        }

        void DrawPause()
        {
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.02f, 0.03f, 0.06f, 0.65f));
            var net = NetGame.Instance;
            bool hostPanel = net != null && net.HasSnapshot && net.IsLocalHost && _game.Screen == AppScreen.Session;
            // Em partida: diz se o jogo parou mesmo (sozinho contra bots) ou se continua para os outros.
            bool inMatch = net != null && net.HasSnapshot && net.View.InMatch && _game.Screen == AppScreen.Session;
            float status = inMatch ? 34f : 0f;
            float w = 620f, h = (hostPanel ? 734f : 640f) + status, x = (_vw - w) * 0.5f, y = (_vh - h) * 0.5f;
            var panel = new Rect(x, y, w, h);
            Plate(panel, NavyDark, true, 22f);
            Logo(new Rect(panel.center.x - 70f, y - 70f, 140f, 140f));
            T(new Rect(x, y + 76f, w, 70f), "PAUSA", 58, Mustard, TextAnchor.MiddleCenter, true, true);
            if (inMatch)
                T(new Rect(x + 20f, y + 146f, w - 40f, 30f), _game.MatchPaused ? "PARTIDA PARADA · só tu contra bots" : "A partida continua: há outros jogadores na sala",
                    20, _game.MatchPaused ? Mustard : Muted, TextAnchor.MiddleCenter, true);
            float by = y + 160f + status, bw = w - 80f, bh = 78f;
            if (Primary(new Rect(x + 40f, by, bw, bh), _game.MatchPaused ? "RETOMAR PARTIDA" : "CONTINUAR", true, 32, IconKind.Play)) _game.TogglePause();
            by += bh + 16f;
            if (Secondary(new Rect(x + 40f, by, bw, bh), "DEFINIÇÕES", true, 32, IconKind.Gear)) OpenSettings();
            by += bh + 16f;
            if (Secondary(new Rect(x + 40f, by, bw, bh), "PERSONALIZAR", true, 32, IconKind.Head))
            {
                _game.SetPauseMenu(false, false);
                _customizeOverlay = true;
            }
            by += bh + 16f;
            // Anfitrião: regras, permissões e reinício sem recriar a sala (também a meio da partida).
            if (hostPanel)
            {
                if (Secondary(new Rect(x + 40f, by, bw, bh), "PAINEL DO ANFITRIÃO", true, 30, IconKind.Gear)) OpenHostPanel();
                by += bh + 16f;
            }
            if (DangerBtn(new Rect(x + 40f, by, bw * 0.5f - 8f, bh), "SAIR DA SALA", true, 24, IconKind.Door)) _game.Leave();
            if (DangerBtn(new Rect(x + 40f + bw * 0.5f + 8f, by, bw * 0.5f - 8f, bh), "SAIR DO JOGO", true, 24)) _game.Quit();
        }

        /// <summary>Aviso permanente enquanto a partida está congelada e o menu de pausa está fechado.</summary>
        void DrawMatchPausedBanner()
        {
            if (Event.current.type == EventType.Repaint) PauseBannerFrame = Time.frameCount;
            // Por cima de outro painel (anfitrião, personalizar, placar): só uma etiqueta no topo, sem tapar botões.
            if (_roomSettingsOpen || _customizeOverlay || _eliminatedOverlay || ResultsOpen)
            {
                float cw = 560f;
                var chip = new Rect((_vw - cw) * 0.5f, 6f, cw, 46f);
                Round(chip, new Color(0.03f, 0.05f, 0.09f, 0.92f), 23f);
                Outline(chip, Mustard, 2f, 23f);
                T(chip, "PARTIDA EM PAUSA · P para retomar", 22, Mustard, TextAnchor.MiddleCenter, true);
                return;
            }
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.02f, 0.03f, 0.06f, 0.28f));
            float w = Mathf.Min(760f, _vw - 40f), h = 132f, x = (_vw - w) * 0.5f, y = Mathf.Round(_vh * 0.36f);
            var r = new Rect(x, y, w, h);
            Plate(r, NavyDark, true, 20f);
            // Ícone de pausa: duas barras.
            Round(new Rect(x + 30f, y + 30f, 18f, 72f), Mustard, 5f);
            Round(new Rect(x + 58f, y + 30f, 18f, 72f), Mustard, 5f);
            T(new Rect(x + 100f, y + 14f, w - 330f, 60f), "PARTIDA EM PAUSA", 44, Mustard, TextAnchor.MiddleLeft, true, true);
            T(new Rect(x + 100f, y + 74f, w - 330f, 44f), "Tempo, bots e bolas parados. P para retomar.", 19, Ivory, TextAnchor.MiddleLeft, false, false, true);
            if (Primary(new Rect(r.xMax - 220f, y + 30f, 196f, 72f), "RETOMAR", true, 28, IconKind.Play)) _game.ToggleMatchPause();
        }

        void DrawSettings()
        {
            if (_gfxDraft == null) OpenSettings();
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.02f, 0.03f, 0.06f, 0.7f));
            float w = Mathf.Min(1520f, _vw - 40f), h = Mathf.Min(900f, _vh - 40f), x = (_vw - w) * 0.5f, y = (_vh - h) * 0.5f;

            // Separadores.
            var tabsBar = new Rect(x, y, w, 110f);
            Plate(tabsBar, NavyDark, true, 22f);
            Logo(new Rect(tabsBar.x + 16f, tabsBar.y + 8f, 94f, 94f));
            float tx = tabsBar.x + 130f, tw = (tabsBar.width - 150f) / SettingsTabs.Length;
            for (int i = 0; i < SettingsTabs.Length; i++)
            {
                var tab = new Rect(tx + i * tw + 4f, tabsBar.y + 16f, tw - 8f, 78f);
                if (Btn(tab, SettingsTabs[i], i == _settingsTab ? Mustard : Navy, i == _settingsTab ? NavyDark : Ivory, true, tw > 230f ? 30 : 22)) _settingsTab = i;
            }

            var body = new Rect(x + 30f, tabsBar.yMax + 16f, w - 60f, h - 110f - 16f - 120f);
            Plate(body, Mustard, true, 20f);
            var inner = Inset(body, 12f);
            Round(inner, NavyDark, 14f);

            switch (_settingsTab)
            {
                case 0:
                    DrawGeneralTab(inner);
                    break;
                case 1:
                    DrawGraphicsTab(inner);
                    break;
                case 2:
                    DrawAudioTab(inner);
                    break;
                case 3:
                    DrawControlsTab(inner);
                    break;
                default:
                    DrawAccessibilityTab(inner);
                    break;
            }

            // APLICAR / REPOR / VOLTAR.
            float by = body.yMax + 20f, bw = 300f;
            float bx = (_vw - (bw * 3f + 40f)) * 0.5f;
            if (Primary(new Rect(bx, by, bw, 90f), _gfxDirty ? "APLICAR *" : "APLICAR", true, 38))
            {
                ApplyGraphicsDraft();
                _game.Settings.Save();
            }
            if (Btn(new Rect(bx + bw + 20f, by, bw, 90f), "REPOR", Ivory, NavyDark, true, 38))
            {
                if (_settingsTab == 1)
                {
                    _gfxDraft = new GraphicsOptions();
                    _gfxDirty = true;
                }
                else ResetOtherSettings();
            }
            if (Secondary(new Rect(bx + (bw + 20f) * 2f, by, bw, 90f), "VOLTAR", true, 38))
            {
                _game.Settings.Save();
                _settingsOpen = false;
            }
        }

        void ApplyGraphicsDraft()
        {
            if (_gfxDraft == null) return;
            bool giTurnsOn = _gfxDraft.GlobalIllumination && !_game.Settings.Graphics.GlobalIllumination;
            bool styleChanges = _gfxDraft.CelShading != _game.Settings.Graphics.CelShading;
            string json = _gfxDraft.ToJson();
            _gfxDirty = false;
            // Aplicar pesa (sonda de reflexos, estilo, upscaler, GI): o modal aparece primeiro e esconde os primeiros
            // frames; a GI precisa de ~1,5 s a aquecer a cache de luz.
            RunWithLoading("A APLICAR GRÁFICOS", () =>
            {
                JsonUtility.FromJsonOverwrite(json, _game.Settings.Graphics);
                _game.ApplyGraphics();
                ShowToast("Definições gráficas aplicadas.", 2f);
            }, giTurnsOn ? 1.6f : styleChanges ? 0.9f : 0.5f, giTurnsOn ? "A aquecer a iluminação global" : null);
        }

        void ResetOtherSettings()
        {
            var s = _game.Settings;
            var d = new GameSettings();
            switch (_settingsTab)
            {
                case 0:
                    s.ShowHelp = d.ShowHelp;
                    break;
                case 2:
                    s.MasterVolume = d.MasterVolume;
                    s.MusicVolume = d.MusicVolume;
                    s.JukeboxVolume = d.JukeboxVolume;
                    s.JukeboxMuted = d.JukeboxMuted;
                    s.SfxVolume = d.SfxVolume;
                    s.UiVolume = d.UiVolume;
                    break;
                case 3:
                    s.MouseSensitivity = d.MouseSensitivity;
                    s.FieldOfView = d.FieldOfView;
                    break;
                default:
                    s.CameraMotion = d.CameraMotion;
                    s.UiScale = d.UiScale;
                    break;
            }
        }

        // ------------------------------------------------------------------ linhas

        const float RowH = 62f;

        Rect OptionRow(Rect col, ref float y, string label, float controlWidth = 300f)
        {
            var row = new Rect(col.x, y, col.width, RowH - 6f);
            Round(row, new Color(1f, 1f, 1f, 0.04f), 8f);
            T(new Rect(row.x + 18f, row.y, row.width - controlWidth - 30f, row.height), label, 24, Ivory, TextAnchor.MiddleLeft, true);
            y += RowH;
            return new Rect(row.xMax - controlWidth - 10f, row.y + 6f, controlWidth, row.height - 12f);
        }

        int GfxChoice(Rect col, ref float y, string label, string[] names, int value)
        {
            var ctl = OptionRow(col, ref y, label);
            value = Mathf.Clamp(value, 0, names.Length - 1);
            int next = Cycler(ctl, names[value], value, names.Length);
            if (next != value) MarkCustom();
            return next;
        }

        bool GfxToggle(Rect col, ref float y, string label, bool value)
        {
            var ctl = OptionRow(col, ref y, label);
            bool next = OnOff(ctl, value);
            if (next != value) MarkCustom();
            return next;
        }

        void MarkCustom()
        {
            _gfxDraft.Preset = 4;
            _gfxDirty = true;
        }

        float SliderRow(Rect col, ref float y, string label, float value, float min, float max, string format)
        {
            var ctl = OptionRow(col, ref y, label, 380f);
            float v = Slider(new Rect(ctl.x, ctl.y, ctl.width - 90f, ctl.height), value, min, max);
            T(new Rect(ctl.xMax - 80f, ctl.y, 80f, ctl.height), format == "P0" ? Mathf.RoundToInt(v * 100f) + "%" : v.ToString(format), 22, Mustard, TextAnchor.MiddleRight, true);
            return v;
        }

        // ------------------------------------------------------------------ separadores

        void DrawGraphicsTab(Rect r)
        {
            // The extra style row must remain reachable at smaller window sizes.
            var content = new Rect(0, 0, r.width - 22f, 930f);
            _graphicsScroll = GUI.BeginScrollView(r, _graphicsScroll, content);
            DrawGraphicsContent(content);
            GUI.EndScrollView();
        }

        void DrawGraphicsContent(Rect r)
        {
            var g = _gfxDraft;
            // Predefinições.
            var presets = new Rect(r.x + 20f, r.y + 16f, r.width - 40f, 70f);
            Round(presets, new Color(1f, 1f, 1f, 0.04f), 10f);
            T(new Rect(presets.x + 20f, presets.y, 260f, presets.height), "PREDEFINIÇÕES", 30, Ivory, TextAnchor.MiddleLeft, true);
            float pw = (presets.width - 320f - 30f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var b = new Rect(presets.x + 300f + i * (pw + 10f), presets.y + 8f, pw, presets.height - 16f);
                if (Btn(b, PresetNames[i], g.Preset == i ? Mustard : Navy, g.Preset == i ? NavyDark : Ivory, true, 26))
                {
                    g.ApplyPreset(i);
                    _gfxDirty = true;
                }
            }
            if (g.Preset == 4) T(new Rect(presets.xMax - 200f, presets.yMax + 2f, 190f, 22f), "PERSONALIZADO", 16, Mustard, TextAnchor.MiddleRight, true);

            float colW = (r.width - 60f) * 0.5f;
            var left = new Rect(r.x + 20f, 0f, colW, 0f);
            var right = new Rect(r.x + 40f + colW, 0f, colW, 0f);
            float ly = presets.yMax + 24f, ry = ly;

            var resolutions = UnityEngine.Screen.resolutions;
            string[] resNames = new string[resolutions.Length + 1];
            resNames[0] = "Nativa";
            for (int i = 0; i < resolutions.Length; i++) resNames[i + 1] = resolutions[i].width + " × " + resolutions[i].height;
            g.ResolutionIndex = GfxChoice(left, ref ly, "Resolução", resNames, g.ResolutionIndex + 1) - 1;
            g.DisplayMode = GfxChoice(left, ref ly, "Modo de ecrã", new[] { "Sem bordas", "Exclusivo", "Janela" }, g.DisplayMode);
            g.VSync = GfxToggle(left, ref ly, "VSync", g.VSync);
            string[] caps = new string[GraphicsOptions.FrameCaps.Length];
            for (int i = 0; i < caps.Length; i++) caps[i] = GraphicsOptions.FrameCaps[i] <= 0 ? "Sem limite" : GraphicsOptions.FrameCaps[i] + " FPS";
            g.FrameCap = GfxChoice(left, ref ly, "Limite de FPS" + (g.VSync ? " (VSync)" : ""), caps, g.FrameCap);
            g.TextureQuality = GfxChoice(left, ref ly, "Qualidade de texturas", new[] { "Muito baixa", "Baixa", "Média", "Máxima" }, g.TextureQuality);
            g.Anisotropic = GfxChoice(left, ref ly, "Filtro anisotrópico", new[] { "Desligado", "2×", "4×", "8×", "16×" }, g.Anisotropic);
            g.Shadows = GfxChoice(left, ref ly, "Sombras", new[] { "Desligadas", "Baixas", "Médias", "Altas", "Ultra" }, g.Shadows);
            g.Effects = GfxChoice(left, ref ly, "Efeitos", new[] { "Baixos", "Médios", "Altos", "Ultra" }, g.Effects);
            // Opcional e desligado por omissão; não conta como parte das predefinições de qualidade.
            string[] celNames = { "Desligado", "Suave", "Marcado (BD)" };
            int celValue = Mathf.Clamp(g.CelShading, 0, 2);
            int cel = Cycler(OptionRow(left, ref ly, "Cel shading"), celNames[celValue], celValue, celNames.Length);
            if (cel != celValue) _gfxDirty = true;
            g.CelShading = cel;

            g.PostProcessing = GfxChoice(right, ref ry, "Pós-processamento", new[] { "Desligado", "Baixo", "Médio", "Alto" }, g.PostProcessing);
            g.AntiAliasing = GfxChoice(right, ref ry, "Anti-aliasing", new[] { "Nenhum", "FXAA", "SMAA", "MSAA 2×", "MSAA 4×", "TAA" }, g.AntiAliasing);
            // Só aparecem o upscaler do fabricante desta placa (NVIDIA → DLSS, AMD → FSR 4, Intel → XeSS) e os
            // recursos seguros (escala simples e STP).
            var upscalers = UpscalerController.MenuChoices();
            var upscalerNames = System.Array.ConvertAll(upscalers, id => UpscalerController.Names[id]);
            int selected = System.Array.IndexOf(upscalers, UpscalerController.Resolve(g.Upscaler));
            g.Upscaler = upscalers[GfxChoice(right, ref ry, "Upscaler", upscalerNames, Mathf.Max(0, selected))];
            if (UpscalerController.UsesQualityMode(g.Upscaler))
            {
                // O modo do SDK decide a resolução interna; a escala de render não se aplica.
                string[] modes = new string[UpscalerController.QualityNames.Length];
                for (int i = 0; i < modes.Length; i++) modes[i] = UpscalerController.QualityName(g.Upscaler, i);
                g.UpscalerQuality = GfxChoice(right, ref ry, "Modo do upscaler", modes, g.UpscalerQuality);
            }
            else
            {
                float scale = SliderRow(right, ref ry, "Escala de render", g.RenderScale, 1f / 3f, 1f, "P0");
                if (Mathf.Abs(scale - g.RenderScale) > 0.001f)
                {
                    g.RenderScale = Mathf.Round(scale * 20f) / 20f;
                    MarkCustom();
                }
            }
            g.Bloom = GfxToggle(right, ref ry, "Bloom", g.Bloom);
            g.AmbientOcclusion = GfxToggle(right, ref ry, "Oclusão ambiente", g.AmbientOcclusion);
            g.DepthOfField = GfxToggle(right, ref ry, "Profundidade de campo", g.DepthOfField);
            g.MotionBlur = GfxToggle(right, ref ry, "Motion blur", g.MotionBlur);
            if (GraphicsQuality.GlobalIlluminationSupported)
            {
                g.GlobalIllumination = GfxToggle(right, ref ry, "Iluminação global (RT)", g.GlobalIllumination);
                if (g.GlobalIllumination)
                    g.GlobalIlluminationQuality = GfxChoice(right, ref ry, "RT: amostras e denoiser", new[] { "Rápido", "Equilibrado", "Qualidade" }, g.GlobalIlluminationQuality);
            }

            float by = Mathf.Max(ly, ry) + 2f;
            string note = UpscalerController.Status + "  " + RenderResolutionMonitor.Status + ". Upscalers temporais controlam o anti-aliasing.";
            if (g.Effects != GraphicsQuality.Options.Effects) note += "  O nº de espectadores muda ao reiniciar.";
            T(new Rect(r.x + 24f, by, r.width - 48f, r.yMax - by - 6f), note, 17, Muted, TextAnchor.UpperLeft, false, false, true);
        }

        void DrawGeneralTab(Rect r)
        {
            var s = _game.Settings;
            var col = new Rect(r.x + 40f, 0f, r.width - 80f, 0f);
            float y = r.y + 30f;
            var nameCtl = OptionRow(col, ref y, "Nome do jogador", 420f);
            string name = s.PlayerName;
            TextField(nameCtl, ref name, 16);
            if (name != s.PlayerName)
            {
                s.PlayerName = name;
                _game.ProfileChanged(false);
            }
            var lang = OptionRow(col, ref y, "Idioma");
            Pill(lang, "PORTUGUÊS", Navy, Ivory, 22);
            s.ShowHelp = OnOff(OptionRow(col, ref y, "Mostrar ajuda de teclas (F1)"), s.ShowHelp);
            T(new Rect(col.x, y + 20f, col.width, 140f),
                "Música da jukebox: faixas CC0 do OpenGameArt, partilhadas por todos na sala (fila e votação para saltar). " +
                "YouTube: página oficial do vídeo num navegador embutido (com os anúncios do YouTube), sem download ou extração de áudio; o que pedir login abre no teu navegador. O anfitrião configura a chave da Data API localmente.",
                19, Muted, TextAnchor.UpperLeft, false, false, true);
        }

        void DrawAudioTab(Rect r)
        {
            var s = _game.Settings;
            var col = new Rect(r.x + 40f, 0f, r.width - 80f, 0f);
            float y = r.y + 20f;
            s.MasterVolume = SliderRow(col, ref y, "Volume geral", s.MasterVolume, 0f, 1f, "P0");
            s.MusicVolume = SliderRow(col, ref y, "Música de tensão (roleta)", s.MusicVolume, 0f, 1f, "P0");
            s.JukeboxVolume = SliderRow(col, ref y, "Jukebox (rádio e YouTube) · só tu", s.JukeboxVolume, 0f, 1f, "P0");
            s.JukeboxMuted = OnOff(OptionRow(col, ref y, "Silenciar a jukebox"), s.JukeboxMuted);
            s.SfxVolume = SliderRow(col, ref y, "Efeitos (bolas, taco, tiros)", s.SfxVolume, 0f, 1f, "P0");
            s.UiVolume = SliderRow(col, ref y, "Interface", s.UiVolume, 0f, 1f, "P0");
            bool consent = OnOff(OptionRow(col, ref y, "Ouvir o YouTube da sala automaticamente"), s.YouTubeConsent);
            if (consent != s.YouTubeConsent)
            {
                s.YouTubeConsent = consent;
                if (consent && OnlineMusicPlayer.Instance != null) OnlineMusicPlayer.Instance.EnablePlayer();
            }
            T(new Rect(col.x, y + 20f, col.width, 60f), "Efeitos sonoros: packs CC0 do Kenney.nl. Créditos completos em Resources/Audio/CREDITS.txt.", 19, Muted, TextAnchor.UpperLeft, false, false, true);
        }

        void DrawControlsTab(Rect r)
        {
            var s = _game.Settings;
            var col = new Rect(r.x + 40f, 0f, r.width - 80f, 0f);
            float y = r.y + 30f;
            s.MouseSensitivity = SliderRow(col, ref y, "Sensibilidade do rato", s.MouseSensitivity, 0.2f, 3f, "0.00");
            s.FieldOfView = SliderRow(col, ref y, "Campo de visão", s.FieldOfView, 55f, 100f, "0");
            y += 10f;
            string[,] keys =
            {
                { "WASD", "Andar" }, { "Rato", "Olhar / apontar" }, { "Espaço", "Saltar" }, { "E", "Interagir · pegar no taco" },
                { "Clique esq.", "Tacar (puxar) · gatilho" }, { "Shift", "Mira fina" }, { "WASD (tacada)", "Efeito na bola" }, { "Tab", "Vista de cima / rato livre" },
                { "Q", "Largar o taco / sair da máquina" }, { "Botão do meio", "Marcar um sítio (todos vêem)" }, { "G", "Atirar coisas" }, { "1–4", "Emotes" },
                { "T", "Chat" }, { "J", "YouTube: ouvir · silenciar anúncio" }, { "K", "Pular anúncio (quando der)" }, { "R", "Pronto (sala)" },
                { "P", "Pausar (só tu contra bots)" }, { "Esc", "Menu" }
            };
            int n = keys.GetLength(0);
            float cw = (col.width - 20f) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float kx = col.x + (i % 2) * (cw + 20f);
                float ky = y + (i / 2) * 44f;
                if (ky + 40f > r.yMax) break;
                Pill(new Rect(kx, ky + 4f, 170f, 34f), keys[i, 0], Mustard, NavyDark, 18);
                T(new Rect(kx + 186f, ky, cw - 190f, 42f), keys[i, 1], 21, Ivory);
            }
        }

        void DrawAccessibilityTab(Rect r)
        {
            var s = _game.Settings;
            var col = new Rect(r.x + 40f, 0f, r.width - 80f, 0f);
            float y = r.y + 30f;
            string[] motion = { "Nenhum", "Reduzido", "Completo" };
            s.CameraMotion = Cycler(OptionRow(col, ref y, "Movimento da câmara"), motion[Mathf.Clamp(s.CameraMotion, 0, 2)], Mathf.Clamp(s.CameraMotion, 0, 2), 3);
            // Pré-visualização imediata (a HUD lê o valor a cada frame); guarda ao sair das definições.
            var scaleRow = OptionRow(col, ref y, "Tamanho da interface", 470f);
            float scale = Slider(new Rect(scaleRow.x, scaleRow.y, scaleRow.width - 210f, scaleRow.height), s.UiScale, UiScaleMin, UiScaleMax);
            s.UiScale = Mathf.Round(scale * 20f) / 20f;
            float effective = EffectiveScale(UnityEngine.Screen.width, UnityEngine.Screen.height, s.UiScale) / (UnityEngine.Screen.height / VirtualHeight);
            T(new Rect(scaleRow.xMax - 200f, scaleRow.y, 80f, scaleRow.height), Mathf.RoundToInt(s.UiScale * 100f) + "%", 22, Mustard, TextAnchor.MiddleRight, true);
            if (Btn(new Rect(scaleRow.xMax - 110f, scaleRow.y + 2f, 110f, scaleRow.height - 4f), "100%", Navy, Ivory, !Mathf.Approximately(s.UiScale, 1f), 18))
                s.UiScale = 1f;
            if (effective < s.UiScale - 0.01f)
                T(new Rect(col.x + 18f, y - 8f, col.width - 36f, 26f), "Neste ecrã a interface fica limitada a " + Mathf.RoundToInt(effective * 100f) + "% para caber sem cortes.", 17, Coral, TextAnchor.MiddleLeft);
            y += 20f;
            T(new Rect(col.x, y + 20f, col.width, 100f),
                "Nenhum: câmara fixa (para quem enjoa). Reduzido: um balanço suave dos passos, sem abanões nem quedas. Nos dois, os debuffs de câmara ficam mais suaves. Completo: a câmara mexe-se toda com o personagem. Não muda as regras nem a física.",
                19, Muted, TextAnchor.UpperLeft, false, false, true);
        }
    }
}
