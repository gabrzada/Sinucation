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

        void OpenSettings()
        {
            _settingsOpen = true;
            _gfxDraft = GraphicsOptions.FromJson(_game.Settings.Graphics.ToJson());
            _gfxDirty = false;
        }

        void DrawPause()
        {
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.02f, 0.03f, 0.06f, 0.65f));
            float w = 620f, h = 640f, x = (_vw - w) * 0.5f, y = (_vh - h) * 0.5f;
            var panel = new Rect(x, y, w, h);
            Plate(panel, NavyDark, true, 22f);
            Logo(new Rect(panel.center.x - 70f, y - 70f, 140f, 140f));
            T(new Rect(x, y + 76f, w, 70f), "PAUSA", 58, Mustard, TextAnchor.MiddleCenter, true, true);
            float by = y + 160f, bw = w - 80f, bh = 78f;
            if (Primary(new Rect(x + 40f, by, bw, bh), "CONTINUAR", true, 32, IconKind.Play)) _game.TogglePause();
            by += bh + 16f;
            if (Secondary(new Rect(x + 40f, by, bw, bh), "DEFINIÇÕES", true, 32, IconKind.Gear)) OpenSettings();
            by += bh + 16f;
            if (Secondary(new Rect(x + 40f, by, bw, bh), "PERSONALIZAR", true, 32, IconKind.Head))
            {
                _game.TogglePause();
                _customizeOverlay = true;
            }
            by += bh + 16f;
            if (DangerBtn(new Rect(x + 40f, by, bw * 0.5f - 8f, bh), "SAIR DA SALA", true, 24, IconKind.Door)) _game.Leave();
            if (DangerBtn(new Rect(x + 40f + bw * 0.5f + 8f, by, bw * 0.5f - 8f, bh), "SAIR DO JOGO", true, 24)) _game.Quit();
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
            JsonUtility.FromJsonOverwrite(_gfxDraft.ToJson(), _game.Settings.Graphics);
            _game.ApplyGraphics();
            _gfxDirty = false;
            ShowToast("Definições gráficas aplicadas.", 2f);
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
                    s.SfxVolume = d.SfxVolume;
                    s.UiVolume = d.UiVolume;
                    break;
                case 3:
                    s.MouseSensitivity = d.MouseSensitivity;
                    s.FieldOfView = d.FieldOfView;
                    break;
                default:
                    s.ComfortMode = d.ComfortMode;
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

            g.PostProcessing = GfxChoice(right, ref ry, "Pós-processamento", new[] { "Desligado", "Baixo", "Médio", "Alto" }, g.PostProcessing);
            g.AntiAliasing = GfxChoice(right, ref ry, "Anti-aliasing", new[] { "Nenhum", "FXAA", "SMAA", "MSAA 2×", "MSAA 4×", "TAA" }, g.AntiAliasing);
            float scale = SliderRow(right, ref ry, "Escala de render", g.RenderScale, 0.5f, 1f, "P0");
            if (Mathf.Abs(scale - g.RenderScale) > 0.001f)
            {
                g.RenderScale = Mathf.Round(scale * 20f) / 20f;
                MarkCustom();
            }
            string[] upscalers = GraphicsQuality.DlssAvailable ? new[] { "Bilinear", "AMD FSR 1.0", "Unity STP", "NVIDIA DLSS" } : new[] { "Bilinear", "AMD FSR 1.0", "Unity STP" };
            g.Upscaler = GfxChoice(right, ref ry, "Upscaler", upscalers, g.Upscaler);
            g.Bloom = GfxToggle(right, ref ry, "Bloom", g.Bloom);
            g.AmbientOcclusion = GfxToggle(right, ref ry, "Oclusão ambiente", g.AmbientOcclusion);
            g.DepthOfField = GfxToggle(right, ref ry, "Profundidade de campo", g.DepthOfField);
            g.MotionBlur = GfxToggle(right, ref ry, "Motion blur", g.MotionBlur);

            float by = Mathf.Max(ly, ry) + 2f;
            string note = GraphicsQuality.DlssStatus + ".  O upscaler só actua com escala de render abaixo de 100%.";
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
                "Spotify/YouTube Music não são possíveis: os termos do Spotify proíbem usá-lo em jogos e os do YouTube proíbem separar o áudio do vídeo.",
                19, Muted, TextAnchor.UpperLeft, false, false, true);
        }

        void DrawAudioTab(Rect r)
        {
            var s = _game.Settings;
            var col = new Rect(r.x + 40f, 0f, r.width - 80f, 0f);
            float y = r.y + 30f;
            s.MasterVolume = SliderRow(col, ref y, "Volume geral", s.MasterVolume, 0f, 1f, "P0");
            s.MusicVolume = SliderRow(col, ref y, "Música (jukebox e tensão)", s.MusicVolume, 0f, 1f, "P0");
            s.SfxVolume = SliderRow(col, ref y, "Efeitos (bolas, taco, gosma)", s.SfxVolume, 0f, 1f, "P0");
            s.UiVolume = SliderRow(col, ref y, "Interface", s.UiVolume, 0f, 1f, "P0");
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
                { "WASD", "Andar" }, { "Rato", "Olhar / apontar" }, { "Espaço", "Saltar · segurar = gatilho" }, { "E", "Interagir · pegar no taco" },
                { "Clique + puxar", "Recuar o taco e tacar" }, { "Shift", "Mira fina" }, { "Setas", "Efeito na bola" }, { "Tab", "Vista de cima / rato livre" },
                { "Q", "Largar o taco / sair da máquina" }, { "G", "Atirar coisas" }, { "1–4", "Emotes" }, { "T", "Chat" }, { "R", "Pronto (sala)" }, { "Esc", "Menu" }
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
            s.ComfortMode = OnOff(OptionRow(col, ref y, "Modo conforto (sem abanões, quedas nem rolamento)"), s.ComfortMode);
            s.UiScale = SliderRow(col, ref y, "Tamanho da interface", s.UiScale, 0.75f, 1.3f, "P0");
            T(new Rect(col.x, y + 20f, col.width, 100f),
                "O modo conforto também suaviza os debuffs de câmara (Ecrã Invertido, Bêbado, Visão de Túnel) para quem enjoa. Não muda as regras nem a física.",
                19, Muted, TextAnchor.UpperLeft, false, false, true);
        }
    }
}
