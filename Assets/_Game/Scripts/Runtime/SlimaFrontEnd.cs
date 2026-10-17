using System;
using System.Collections;
using System.IO;
using BlockMap;
using BlockMap.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimaPuzzle
{
    public sealed class SlimaFrontEnd : MonoBehaviour
    {
        public const string MenuScene = "Assets/_Game/Scenes/MainMenu.unity";
        public const int LevelSelectorCount = 20;
        public const int LevelPageCount = 5;
        public const int LevelCapacity = LevelSelectorCount * LevelPageCount;
        public static readonly string[] DefaultLevelScenes =
        {
            "Assets/Scenes/test-level 1-0.unity",
            "Assets/Scenes/test-level 1-1.unity",
            "Assets/Scenes/test-level 2-0.unity",
            "Assets/Scenes/test-level 2-1.unity",
            "Assets/Scenes/test-level 3-0.unity",
            "Assets/Scenes/test-level 3-1.unity",
            "Assets/Scenes/test-level 3-2.unity",
            "Assets/Scenes/test-level 4-0.unity",
            "Assets/Scenes/test-level 4-1.unity",
            "Assets/Scenes/test-level 4-2.unity",
            "Assets/Scenes/test-level 4-3.unity",
            "Assets/Scenes/test-level 5-0.unity",
            "Assets/Scenes/test-level 5-1.unity",
            "Assets/Scenes/test-level 5-2.unity",
            "Assets/Scenes/test-level 5-3.unity",
            "Assets/Scenes/test-level 5-4.unity",
            "", "", "", ""
        };
        public string[] levelScenes = CreateLevelSlots();

        static string[] CreateLevelSlots()
        {
            var scenes = new string[LevelCapacity];
            Array.Copy(DefaultLevelScenes, scenes, DefaultLevelScenes.Length);
            return scenes;
        }

        [Header("Cover Illustration (1280 x 720)")]
        public Rect beakerRect = new Rect(735, 80, 456, 565);
        public Rect slimeRect = new Rect(884, 275, 230, 180);

        [Header("Title (1280 x 720)")]
        public Rect titleRect = new Rect(70, 58, 870, 280);
        public int titleFontSize = 240;
        public float titleLetterSpacing = 4f;
        [Range(0, 12)] public int titleWeight = 6;
        [Range(0, 20)] public int titleOutlineWidth = 10;
        [Header("Slime Animation")]
        public float slimeFloatAmplitude = 14f;
        public float slimeFloatSpeed = 1.8f;
        [Range(0f, 1f)] public float slimeGlow = 0.8f;
        [Range(0, 32)] public int slimeParticleCount = 16;

        enum Page { Home, Levels, Settings, Playing, Pause, ConfirmQuit, ConfirmLevel }
        static SlimaFrontEnd _instance;
        Page _page;
        Page _returnPage;
        readonly SlimaSave[] _slots = new SlimaSave[SlimaSaveStore.SlotCount];
        readonly string[] _slotErrors = new string[SlimaSaveStore.SlotCount];
        BlockMapGameRoot _level;
        SlimaSave _save;
        int _slot = -1;
        int _selectionSlot;
        int _selectedLevel = -1;
        int _levelPage;
        bool _loading;
        bool _pauseRequested;
        bool _dirty;
        float _nextSave;
        float _volume;
        bool _fullscreen;
        string _message = "";
        // Validation supplies an isolated directory; normal play uses persistentDataPath.
        string _saveDirectory = null;
        Font _font, _boldFont, _boldItalicFont;
        GUIStyle _title, _titleShadow, _heading, _body, _small, _button, _homeButton, _slider, _sliderThumb;
        Texture2D _coverArt, _buttonArt, _panelArt, _slimeGlowArt;
        Sprite _beakerArt, _slimeArt;

        static readonly Color Ink = new Color(0.85f, 0.96f, 1f);
        static readonly Color Paper = new Color(0.025f, 0.06f, 0.11f);
        static readonly Color Blue = new Color(0.36f, 0.85f, 1f);
        static readonly Color Muted = new Color(0.52f, 0.70f, 0.81f);
        // The generated button has transparent padding above and below its artwork.
        static readonly Rect ButtonUv = new Rect(0f, 160f / 724f, 1f, 432f / 724f);

        void Awake()
        {
            if (_instance != null) { Destroy(gameObject); return; }
            _instance = this;
            if (levelScenes == null) levelScenes = CreateLevelSlots();
            else if (levelScenes.Length < LevelCapacity) Array.Resize(ref levelScenes, LevelCapacity);
            DontDestroyOnLoad(gameObject);
            _volume = Mathf.Clamp01(PlayerPrefs.GetFloat("Slima.Volume", 0.8f));
            _fullscreen = PlayerPrefs.GetInt("Slima.Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            AudioListener.volume = _volume;
            Screen.fullScreen = _fullscreen;
            _font = PixelFont("Regular");
            _boldFont = PixelFont("Bold");
            _boldItalicFont = PixelFont("BoldItalic");
            Font.textureRebuilt += SetFontSampling;
            SetFontSampling(_font);
            SetFontSampling(_boldFont);
            SetFontSampling(_boldItalicFont);
            SceneManager.sceneLoaded += OnSceneLoaded;
            RefreshSlots();
        }

        void OnDestroy()
        {
            if (_instance != this) return;
            UnbindLevel();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Time.timeScale = 1f;
            Font.textureRebuilt -= SetFontSampling;
            _instance = null;
        }

        static Font PixelFont(string face) => Resources.Load<Font>("SlimaUI/Fonts/SlimaPixelUI-" + face);

        void SetFontSampling(Font font)
        {
            if (font == null || (font != _font && font != _boldFont && font != _boldItalicFont)) return;
            if (font.material != null && font.material.mainTexture != null)
                font.material.mainTexture.filterMode = FilterMode.Point;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path == MenuScene)
            {
                _loading = false;
                _page = Page.Home;
                RefreshSlots();
                return;
            }
            if (_slot >= 0 && _save != null && scene.path == _save.scenePath)
                StartCoroutine(BindLevel());
        }

        IEnumerator BindLevel()
        {
            yield return null; // The level root creates the party and mechanisms in Start.
            _level = FindObjectOfType<BlockMapGameRoot>();
            if (_level == null || _level.Party == null)
            {
                _message = "关卡缺少可玩的起点，请检查场景。";
                ReturnToMenu(false);
                yield break;
            }
            if (_save.progress != null) _level.RestoreProgress(_save.progress);
            _level.ProgressChanged += MarkDirty;
            _level.Party.Changed += MarkDirty;
            _level.GetComponent<BlockGameplayController>().Changed += MarkDirty;
            _loading = false;
            _page = Page.Playing;
            _dirty = true;
            SaveProgress(false);
        }

        void UnbindLevel()
        {
            if (_level == null) return;
            _level.ProgressChanged -= MarkDirty;
            if (_level.Party != null) _level.Party.Changed -= MarkDirty;
            _level.GetComponent<BlockGameplayController>().Changed -= MarkDirty;
            _level = null;
        }

        void MarkDirty() => _dirty = true;

        void Update()
        {
            if (!_loading && Input.GetKeyDown(KeyCode.Escape))
            {
                if (_page == Page.Levels) { _message = ""; _page = Page.Home; return; }
                if (_page == Page.ConfirmLevel) { _page = Page.Levels; return; }
            }
            if (_loading || _level == null) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_page == Page.Playing) _pauseRequested = true;
                else if (_page == Page.Pause) Resume();
                else if (_page == Page.Settings) _page = _returnPage;
            }
            if (_page == Page.Playing && Input.GetKeyDown(KeyCode.F5)) SaveProgress(true);
        }

        void LateUpdate()
        {
            if (_loading || _level == null || _level.Party.IsBusy) return;
            if (_pauseRequested)
            {
                _pauseRequested = false;
                SaveProgress(false);
                Time.timeScale = 0f;
                _page = Page.Pause;
            }
            if (_dirty && Time.unscaledTime >= _nextSave) SaveProgress(false);
        }

        void RefreshSlots()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slotErrors[i] = null;
                try { _slots[i] = SlimaSaveStore.Read(i, _saveDirectory); }
                catch (Exception error) when (IsSaveError(error))
                {
                    _slots[i] = null;
                    _slotErrors[i] = "存档无法读取，可重新开始";
                }
            }
        }

        static bool IsSaveError(Exception error) => error is IOException ||
            error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException;

        void StartSlot(int slot, bool reset = false)
        {
            SlimaSave save = !reset ? _slots[slot] : null;
            if (save == null && _slotErrors[slot] != null && !reset) return;
            if (save == null)
            {
                if (levelScenes == null || levelScenes.Length == 0)
                { _message = "尚未配置关卡。"; return; }
                save = new SlimaSave { scenePath = levelScenes[0] };
            }
            if (!Application.CanStreamedLevelBeLoaded(save.scenePath))
            { _message = "找不到关卡，请检查 Build Settings：" + Path.GetFileNameWithoutExtension(save.scenePath); return; }
            try
            {
                if (save.progress == null) SlimaSaveStore.Write(slot, save, _saveDirectory);
            }
            catch (Exception error) when (IsSaveError(error))
            { _message = "无法创建存档：" + error.Message; return; }
            LoadSlot(slot, save);
        }

        void LoadSlot(int slot, SlimaSave save)
        {
            _slot = slot;
            _save = save;
            _message = "";
            LoadLevel();
        }

        void LoadLevel()
        {
            UnbindLevel();
            _pauseRequested = false;
            Time.timeScale = 1f;
            _loading = true;
            SceneManager.LoadSceneAsync(_save.scenePath);
        }

        bool SaveProgress(bool notify)
        {
            if (_slot < 0 || _level == null) return false;
            GameplayProgress progress = _level.CaptureProgress();
            if (progress == null)
            {
                if (notify) _message = "请等待史莱姆移动结束后保存。";
                return false;
            }
            try
            {
                _save.progress = progress;
                SlimaSaveStore.Write(_slot, _save, _saveDirectory);
                _dirty = false;
                _nextSave = Time.unscaledTime + 1f;
                if (notify) _message = "进度已保存";
                return true;
            }
            catch (Exception error) when (IsSaveError(error))
            {
                _message = "保存失败：" + error.Message;
                _dirty = true;
                _nextSave = Time.unscaledTime + 5f;
                return false;
            }
        }

        void ReturnToMenu(bool save = true)
        {
            if (save && !SaveProgress(true)) return;
            UnbindLevel();
            _slot = -1;
            _save = null;
            _pauseRequested = false;
            Time.timeScale = 1f;
            _loading = true;
            SceneManager.LoadSceneAsync(MenuScene);
        }

        int NextLevelIndex()
        {
            if (_save == null || levelScenes == null) return -1;
            int current = Array.IndexOf(levelScenes, _save.scenePath);
            if (current < 0) return -1;
            for (int i = current + 1; i < Mathf.Min(levelScenes.Length, LevelCapacity); i++)
                if (!string.IsNullOrEmpty(levelScenes[i])) return i;
            return -1;
        }

        void NextLevel()
        {
            int next = NextLevelIndex();
            if (next <= 0 || next >= levelScenes.Length) return;
            if (!Application.CanStreamedLevelBeLoaded(levelScenes[next]))
            { _message = "下一关未加入 Build Settings。"; return; }
            if (!SaveProgress(false)) return;
            var nextSave = new SlimaSave { scenePath = levelScenes[next] };
            try { SlimaSaveStore.Write(_slot, nextSave, _saveDirectory); }
            catch (Exception error) when (IsSaveError(error))
            { _message = "保存失败：" + error.Message; return; }
            _save = nextSave;
            _message = "";
            LoadLevel();
        }

        void Resume()
        {
            Time.timeScale = 1f;
            _page = Page.Playing;
            _message = "";
        }

        void Quit()
        {
            if (_level != null && !SaveProgress(false)) return;
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _level != null) SaveProgress(false);
        }

        void OnApplicationQuit()
        {
            if (_level != null) SaveProgress(false);
        }

        void InitStyles()
        {
            if (_heading != null) return;
            _coverArt = Art("cover-background");
            _beakerArt = Resources.Load<Sprite>("SlimaUI/cover-beaker");
            _slimeArt = Resources.Load<Sprite>("SlimaUI/cover-slime");
            _buttonArt = Art("button");
            _panelArt = Art("panel");
            _slimeGlowArt = Art("slime-glow");
            _title = Style(titleFontSize, Ink, TextAnchor.MiddleLeft, _boldItalicFont);
            _titleShadow = Style(titleFontSize, new Color(0.015f, 0.045f, 0.085f), TextAnchor.MiddleLeft, _boldItalicFont);
            _title.hover.textColor = _title.active.textColor = _title.focused.textColor = _title.normal.textColor;
            _titleShadow.hover.textColor = _titleShadow.active.textColor = _titleShadow.focused.textColor = _titleShadow.normal.textColor;
            _title.padding = _titleShadow.padding = new RectOffset();
            _title.clipping = _titleShadow.clipping = TextClipping.Overflow;
            _heading = Style(30, Ink, TextAnchor.MiddleLeft, _boldFont);
            _body = Style(22, Ink, TextAnchor.MiddleLeft);
            _small = Style(16, Muted, TextAnchor.MiddleLeft);
            _button = new GUIStyle
            {
                font = _boldFont, fontSize = 24, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Ink }, hover = { textColor = Blue },
                active = { textColor = Ink }, focused = { textColor = Blue }
            };
            _homeButton = new GUIStyle(_button) { font = _boldItalicFont, fontSize = 36 };
            _slider = new GUIStyle { fixedHeight = 24 };
            _slider.normal.background = Art("slider-track");
            _sliderThumb = new GUIStyle { fixedWidth = 30, fixedHeight = 30 };
            _sliderThumb.normal.background = Art("slider-thumb");
            _sliderThumb.hover.background = _sliderThumb.normal.background;
            _sliderThumb.active.background = _sliderThumb.normal.background;
            _sliderThumb.focused.background = _sliderThumb.normal.background;
        }

        static Texture2D Art(string name) => Resources.Load<Texture2D>("SlimaUI/Dark/" + name);

        GUIStyle Style(int size, Color color, TextAnchor alignment, Font face = null) => new GUIStyle(GUI.skin.label)
        { font = face != null ? face : _font, fontSize = size, fontStyle = FontStyle.Normal,
            alignment = alignment, normal = { textColor = color } };

        void OnGUI()
        {
            InitStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            int oldDepth = GUI.depth;
            bool oldEnabled = GUI.enabled;
            GUI.depth = -100;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280f * scale) / 2f,
                (Screen.height - 720f * scale) / 2f), Quaternion.identity, Vector3.one * scale);
            if (_page == Page.Playing && !_loading)
            {
                Image(new Rect(850, 14, 402, 88), _panelArt);
                if (Button(new Rect(1090, 22, 150, 42), "暂停 / ESC", 18)) _pauseRequested = true;
                Label(new Rect(860, 66, 380, 30), "自动保存 · F5 手动保存", _small);
                if (_level != null && _level.Won)
                {
                    bool hasNext = NextLevelIndex() > 0 && NextLevelIndex() < levelScenes.Length;
                    if (Button(new Rect(450, 450, 380, 52), hasNext ? "下一关" : "全部完成 · 返回主菜单"))
                    {
                        if (hasNext) NextLevel();
                        else ReturnToMenu();
                    }
                }
                DrawMessage(664);
            }
            else
            {
                Fill(new Rect(-1280, -720, 3840, 2160), _level == null ? Paper :
                    new Color(Paper.r, Paper.g, Paper.b, 0.96f));
                DrawBackdrop();
                GUI.enabled = !_loading;
                if (_loading) Label(new Rect(480, 325, 450, 60), "正在加载…", _heading);
                else
                    switch (_page)
                    {
                        case Page.Home: DrawHome(); break;
                        case Page.Levels: DrawLevels(); break;
                        case Page.Settings: DrawSettings(); break;
                        case Page.Pause: DrawPause(); break;
                        case Page.ConfirmQuit: DrawQuit(); break;
                        case Page.ConfirmLevel: DrawLevelConfirm(); break;
                    }
                DrawMessage(_page == Page.Levels ? 174 : 646);
            }
            GUI.enabled = oldEnabled;
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }

        void DrawBackdrop()
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _page == Page.Home && !_loading ? 1f : 0.18f);
            Image(new Rect(0, 0, 1280, 720), _coverArt);
            if (_page == Page.Home && !_loading) DrawTitle();
            float opacity = GUI.color.a;
            Rect floatingSlime = FloatingSlimeRect(Time.unscaledTime);
            Rect aura = new Rect(floatingSlime.center.x - floatingSlime.width * 0.8f,
                floatingSlime.center.y - floatingSlime.height * 0.8f,
                floatingSlime.width * 1.6f, floatingSlime.height * 1.6f);
            GUI.color = new Color(1f, 1f, 1f, opacity * slimeGlow * (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 2.4f)));
            Image(aura, _slimeGlowArt);
            GUI.color = new Color(0.65f, 0.9f, 1f, opacity);
            Illustration(beakerRect, _beakerArt);
            DrawSlimeParticles(Time.unscaledTime, opacity);
            GUI.color = new Color(1f, 1f, 1f, opacity);
            Illustration(floatingSlime, _slimeArt);
            GUI.color = previous;
        }

        Rect FloatingSlimeRect(float time)
        {
            Rect rect = slimeRect;
            // Quantize to the UI pixel grid and keep moving while gameplay is paused.
            rect.y += Mathf.Round(Mathf.Sin(time * slimeFloatSpeed) * slimeFloatAmplitude);
            return rect;
        }

        void DrawSlimeParticles(float time, float opacity)
        {
            for (int i = 0; i < slimeParticleCount; i++)
            {
                float phase = Mathf.Repeat(time * (0.11f + i % 3 * 0.018f) + i * 0.618034f, 1f);
                float x = beakerRect.x + beakerRect.width * (0.28f + Mathf.Repeat(i * 0.381966f, 1f) * 0.48f);
                x += Mathf.Sin(time * 0.8f + i * 2f) * 8f;
                float y = beakerRect.y + beakerRect.height * (0.77f - phase * 0.59f);
                x = Mathf.Round(x / 2f) * 2f;
                y = Mathf.Round(y / 2f) * 2f;
                float alpha = Mathf.Sin(phase * Mathf.PI) * opacity;
                float size = i % 3 == 0 ? 4f : 2f;
                Fill(new Rect(x - 3, y - 3, size + 6, size + 6), new Color(0.12f, 0.65f, 1f, alpha * 0.15f));
                Fill(new Rect(x, y, size, size), new Color(0.55f, 0.94f, 1f, alpha * 0.9f));
                if (i % 4 == 0)
                {
                    Fill(new Rect(x - 4, y + 1, size + 8, 2), new Color(0.5f, 0.89f, 1f, alpha * 0.7f));
                    Fill(new Rect(x + 1, y - 4, 2, size + 8), new Color(0.5f, 0.89f, 1f, alpha * 0.7f));
                }
            }
        }

        void DrawTitle()
        {
            const string text = "SLIMA";
            _title.fontSize = _titleShadow.fontSize = titleFontSize;
            _boldItalicFont.RequestCharactersInTexture(text, titleFontSize, FontStyle.Normal);
            float width = 0f;
            foreach (char letter in text)
            {
                _boldItalicFont.GetCharacterInfo(letter, out CharacterInfo glyph, titleFontSize);
                // Pack visible ink bounds; the italic font's side bearings leave large gaps.
                width += glyph.maxX - glyph.minX + titleWeight * 2 + titleLetterSpacing;
            }
            width -= titleLetterSpacing;
            Matrix4x4 previous = GUI.matrix;
            // Fit the oversized lettering to the marked width without reducing its height.
            GUI.matrix = previous * Matrix4x4.TRS(titleRect.position, Quaternion.identity,
                new Vector3(titleRect.width / Mathf.Max(1f, width), 1f, 1f));
            // Finish the whole outline before filling, so close letters keep their white strokes.
            for (int pass = 0; pass < 2; pass++)
            {
                float x = titleWeight;
                int radius = pass == 0 ? titleWeight + titleOutlineWidth : titleWeight;
                GUIStyle style = pass == 0 ? _titleShadow : _title;
                foreach (char letter in text)
                {
                    _boldItalicFont.GetCharacterInfo(letter, out CharacterInfo glyph, titleFontSize);
                    Rect rect = new Rect(x - glyph.minX, 0, titleFontSize * 2, titleRect.height);
                    string character = letter.ToString();
                    if (radius == 0) Label(rect, character, style);
                    else
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                                Label(new Rect(rect.x + dx * radius, dy * radius, rect.width, rect.height), character, style);
                    x += glyph.maxX - glyph.minX + titleWeight * 2 + titleLetterSpacing;
                }
            }
            GUI.matrix = previous;
        }

        void DrawHome()
        {
            if (Button(new Rect(110, 382, 340, 76), "开始游戏", 36, _homeButton))
                OpenLevelSelection();
            if (Button(new Rect(110, 486, 340, 76), "设置", 36, _homeButton)) OpenSettings(Page.Home);
            if (Button(new Rect(110, 590, 340, 76), "退出游戏", 36, _homeButton))
            { _returnPage = Page.Home; _page = Page.ConfirmQuit; }
        }

        void OpenLevelSelection()
        {
            RefreshSlots();
            OpenLevels(0);
        }

        bool LevelAvailable(int index) => index >= 0 && index < LevelCapacity &&
            levelScenes != null && index < levelScenes.Length && !string.IsNullOrEmpty(levelScenes[index]) &&
            Application.CanStreamedLevelBeLoaded(levelScenes[index]);

        void OpenLevels(int slot)
        {
            _selectionSlot = slot;
            _selectedLevel = _slots[slot] == null ? -1 : Array.IndexOf(levelScenes, _slots[slot].scenePath);
            if (!LevelAvailable(_selectedLevel))
            {
                _selectedLevel = -1;
                for (int i = 0; i < LevelCapacity; i++)
                    if (LevelAvailable(i)) { _selectedLevel = i; break; }
            }
            _levelPage = _selectedLevel < 0 ? 0 : _selectedLevel / LevelSelectorCount;
            _message = "";
            _page = Page.Levels;
        }

        void StartSelectedLevel(bool confirmed)
        {
            if (!LevelAvailable(_selectedLevel)) return;
            SlimaSave previous = _slots[_selectionSlot];
            string scenePath = levelScenes[_selectedLevel];
            if (previous != null && previous.scenePath == scenePath)
            { StartSlot(_selectionSlot); return; }
            if ((previous != null || _slotErrors[_selectionSlot] != null) && !confirmed)
            { _page = Page.ConfirmLevel; return; }
            var save = new SlimaSave { scenePath = scenePath };
            try { SlimaSaveStore.Write(_selectionSlot, save, _saveDirectory); }
            catch (Exception error) when (IsSaveError(error))
            { _message = "无法创建存档：" + error.Message; return; }
            _slots[_selectionSlot] = save;
            _slotErrors[_selectionSlot] = null;
            LoadSlot(_selectionSlot, save);
        }

        void DrawLevels()
        {
            Label(new Rect(4, 12, 400, 50), "选择关卡", _heading);
            // Keep the frame and body, leaving out the panel's old header and divider.
            if (_panelArt != null)
            {
                GUI.DrawTextureWithTexCoords(new Rect(80, 80, 1120, 20), _panelArt, new Rect(0, 0.97f, 1, 0.03f));
                GUI.DrawTextureWithTexCoords(new Rect(80, 100, 1120, 530), _panelArt, new Rect(0, 0.04f, 1, 0.78f));
                GUI.DrawTextureWithTexCoords(new Rect(80, 630, 1120, 20), _panelArt, new Rect(0, 0, 1, 0.04f));
            }
            bool enabled = GUI.enabled;
            for (int cell = 0; cell < LevelSelectorCount; cell++)
            {
                int index = _levelPage * LevelSelectorCount + cell;
                Rect rect = new Rect(158 + cell % 5 * 216, 112 + cell / 5 * 112, 100, 100);
                bool available = LevelAvailable(index);
                GUI.enabled = enabled && available;
                if (_selectedLevel == index)
                    Fill(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), new Color(Blue.r, Blue.g, Blue.b, 0.4f));
                if (Button(rect, (index + 1).ToString(), 32, square: true)) _selectedLevel = index;
            }
            GUI.enabled = enabled;
            bool continuing = LevelAvailable(_selectedLevel) && _slots[_selectionSlot] != null &&
                _slots[_selectionSlot].scenePath == levelScenes[_selectedLevel];
            Label(new Rect(110, 574, 710, 44), _selectedLevel < 0 ? "本页关卡待开放" :
                $"已选 {_selectedLevel + 1} · {(continuing ? "保留进度并继续" : "从关卡起点开始")} · 自动保存", _small);
            GUI.enabled = enabled && LevelAvailable(_selectedLevel);
            if (Button(new Rect(870, 574, 300, 44), continuing ? "继续所选关卡" : "进入所选关卡")) StartSelectedLevel(false);
            GUI.enabled = enabled;
            if (Button(new Rect(20, 654, 110, 48), "返回")) { _message = ""; _page = Page.Home; }
            GUI.enabled = enabled && _levelPage > 0;
            if (Button(new Rect(475, 654, 82, 48), "<", 32)) SetLevelPage(_levelPage - 1);
            GUI.enabled = enabled;
            Label(new Rect(606, 656, 130, 44), $"{_levelPage + 1} / {LevelPageCount}", _body);
            GUI.enabled = enabled && _levelPage < LevelPageCount - 1;
            if (Button(new Rect(735, 654, 82, 48), ">", 32)) SetLevelPage(_levelPage + 1);
            GUI.enabled = enabled;
        }

        void SetLevelPage(int page)
        {
            if (page < 0 || page >= LevelPageCount || page == _levelPage) return;
            _levelPage = page;
            _selectedLevel = -1;
            int first = page * LevelSelectorCount;
            for (int i = first; i < first + LevelSelectorCount; i++)
                if (LevelAvailable(i)) { _selectedLevel = i; break; }
            _message = "";
        }

        void DrawLevelConfirm()
        {
            Image(new Rect(340, 202, 600, 320), _panelArt);
            Label(new Rect(390, 202, 490, 40), $"进入第 {_selectedLevel + 1:00} 关？", _heading);
            Label(new Rect(390, 300, 490, 70), "当前进度将被替换。\n确认后从关卡起点开始。", _body);
            if (Button(new Rect(390, 420, 240, 54), "取消")) _page = Page.Levels;
            if (Button(new Rect(650, 420, 240, 54), "确认进入")) StartSelectedLevel(true);
        }

        string LevelName(string scenePath)
        {
            int index = Array.IndexOf(levelScenes, scenePath);
            return index >= 0 ? $"第 {index + 1} 关" : Path.GetFileNameWithoutExtension(scenePath);
        }

        void OpenSettings(Page returnPage)
        { _returnPage = returnPage; _page = Page.Settings; _message = ""; }

        void DrawSettings()
        {
            Image(new Rect(330, 156, 620, 436), _panelArt);
            Label(new Rect(376, 168, 500, 48), "设置", _heading);
            Label(new Rect(376, 270, 490, 40), $"主音量    {Mathf.RoundToInt(_volume * 100)}%", _body);
            float volume = GUI.HorizontalSlider(new Rect(380, 324, 510, 30), _volume, 0f, 1f,
                _slider, _sliderThumb);
            if (!Mathf.Approximately(volume, _volume))
            {
                _volume = volume;
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat("Slima.Volume", volume);
            }
            Label(new Rect(376, 378, 250, 44), "显示模式", _body);
            if (Button(new Rect(650, 378, 240, 44), _fullscreen ? "全屏" : "窗口"))
            {
                _fullscreen = !_fullscreen;
                Screen.fullScreen = _fullscreen;
                PlayerPrefs.SetInt("Slima.Fullscreen", _fullscreen ? 1 : 0);
            }
            Label(new Rect(376, 445, 510, 42), "设置会保留到下次启动。", _small);
            if (Button(new Rect(376, 516, 514, 48), "保存并返回"))
            { PlayerPrefs.Save(); _page = _returnPage; }
        }

        void DrawPause()
        {
            Image(new Rect(390, 140, 500, 510), _panelArt);
            Label(new Rect(450, 164, 500, 54), "游戏已暂停", _heading);
            Label(new Rect(450, 221, 500, 30), $"{LevelName(_save.scenePath)} · 自动保存", _small);
            if (Button(new Rect(450, 286, 380, 52), "继续游戏")) Resume();
            if (Button(new Rect(450, 354, 380, 52), "保存进度")) SaveProgress(true);
            if (Button(new Rect(450, 422, 380, 52), "设置")) OpenSettings(Page.Pause);
            if (Button(new Rect(450, 490, 380, 52), "保存并返回主菜单")) ReturnToMenu();
            if (Button(new Rect(450, 558, 380, 52), "保存并退出游戏"))
            { _returnPage = Page.Pause; _page = Page.ConfirmQuit; }
        }

        void DrawQuit()
        {
            Image(new Rect(340, 202, 600, 320), _panelArt);
            Label(new Rect(390, 202, 490, 40), "退出游戏？", _heading);
            Label(new Rect(390, 312, 700, 40), _level == null ? "期待下一次冒险。" : "当前进度会自动保存。", _body);
            if (Button(new Rect(390, 420, 240, 54), "取消")) _page = _returnPage;
            if (Button(new Rect(650, 420, 240, 54), "确认退出")) Quit();
        }

        bool Button(Rect rect, string text, int size = 24, GUIStyle style = null, bool square = false)
        {
            style = style ?? _button;
            style.fontSize = size;
            Color previous = GUI.color;
            bool hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
            GUI.color = !GUI.enabled ? new Color(1f, 1f, 1f, 0.45f) :
                hovered && GUIUtility.hotControl != 0 ? new Color(0.66f, 0.82f, 1f) :
                hovered ? new Color(1.15f, 1.15f, 1.15f) : Color.white;
            if (_buttonArt != null)
            {
                if (square) DrawSquareButtonArt(rect);
                else GUI.DrawTextureWithTexCoords(rect, _buttonArt, ButtonUv);
            }
            GUI.color = previous;
            bool pressed = GUI.Button(rect, text, style);
            return pressed;
        }

        void DrawSquareButtonArt(Rect rect)
        {
            // Nine-slice the existing artwork so its pixel border stays thin on square buttons.
            const float edge = 8f, edgeU = 192f / 2172f, edgeV = 80f / 724f;
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    float x = col == 0 ? rect.x : col == 1 ? rect.x + edge : rect.xMax - edge;
                    float y = row == 0 ? rect.y : row == 1 ? rect.y + edge : rect.yMax - edge;
                    float width = col == 1 ? rect.width - edge * 2 : edge;
                    float height = row == 1 ? rect.height - edge * 2 : edge;
                    float u = col == 0 ? 0f : col == 1 ? edgeU : 1f - edgeU;
                    float v = row == 0 ? ButtonUv.yMax - edgeV : row == 1 ? ButtonUv.y + edgeV : ButtonUv.y;
                    float uvHeight = row == 1 ? ButtonUv.height - edgeV * 2 : edgeV;
                    // Sample a plain side strip, avoiding stretched rivets.
                    if (row == 1 && col != 1) { v = 0.60f; uvHeight = 0.04f; }
                    GUI.DrawTextureWithTexCoords(new Rect(x, y, width, height), _buttonArt,
                        new Rect(u, v, col == 1 ? 1f - edgeU * 2 : edgeU, uvHeight));
                }
        }

        static void Image(Rect rect, Texture2D texture)
        {
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        }

        static void Illustration(Rect rect, Sprite sprite)
        {
            if (sprite != null) GUI.DrawTexture(rect, sprite.texture, ScaleMode.ScaleToFit);
        }

        void Label(Rect rect, string text, GUIStyle style) => GUI.Label(rect, text, style);
        void DrawMessage(float y)
        {
            if (string.IsNullOrEmpty(_message)) return;
            Image(new Rect(90, y - 4, 1120, 46), _panelArt);
            Label(new Rect(110, y, 1080, 38), _message, _small);
        }

        static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
