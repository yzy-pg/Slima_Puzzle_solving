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
        public string[] levelScenes =
        {
            "Assets/Scenes/map01.unity", "Assets/Scenes/map02.unity", "Assets/Scenes/map03.unity"
        };

        [Header("Cover Illustration (1280 x 720)")]
        public Rect beakerRect = new Rect(735, 80, 456, 565);
        public Rect slimeRect = new Rect(884, 275, 230, 180);

        enum Page { Home, Slots, Settings, Playing, Pause, ConfirmReset, ConfirmQuit }
        static SlimaFrontEnd _instance;
        Page _page;
        Page _returnPage;
        readonly SlimaSave[] _slots = new SlimaSave[SlimaSaveStore.SlotCount];
        readonly string[] _slotErrors = new string[SlimaSaveStore.SlotCount];
        BlockMapGameRoot _level;
        SlimaSave _save;
        int _slot = -1;
        int _resetSlot;
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
        Texture2D _coverArt, _buttonArt, _panelArt;
        Sprite _beakerArt, _slimeArt;
        Texture2D _slotEmptyArt, _slotSavedArt, _slotCompleteArt;

        static readonly Color Ink = new Color(0.14f, 0.29f, 0.42f);
        static readonly Color Paper = new Color(0.94f, 0.97f, 1f);
        static readonly Color Blue = new Color(0.26f, 0.54f, 0.76f);
        static readonly Color Muted = new Color(0.30f, 0.45f, 0.57f);
        // The generated button has transparent padding above and below its artwork.
        static readonly Rect ButtonUv = new Rect(0f, 160f / 724f, 1f, 432f / 724f);

        void Awake()
        {
            if (_instance != null) { Destroy(gameObject); return; }
            _instance = this;
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
                if (notify) _message = $"已保存到存档 {_slot + 1}";
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

        int NextLevelIndex() => _save == null ? -1 : Array.IndexOf(levelScenes, _save.scenePath) + 1;

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
            _slotEmptyArt = Art("slot-empty");
            _slotSavedArt = Art("slot-saved");
            _slotCompleteArt = Art("slot-complete");
            _title = Style(132, Blue, TextAnchor.MiddleLeft, _boldItalicFont);
            _titleShadow = Style(132, Ink, TextAnchor.MiddleLeft, _boldItalicFont);
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

        static Texture2D Art(string name) => Resources.Load<Texture2D>("SlimaUI/" + name);

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
                Label(new Rect(860, 66, 380, 30), $"存档 {_slot + 1} · 自动保存 · F5 手动保存", _small);
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
                        case Page.Slots: DrawSlots(); break;
                        case Page.Settings: DrawSettings(); break;
                        case Page.Pause: DrawPause(); break;
                        case Page.ConfirmReset: DrawReset(); break;
                        case Page.ConfirmQuit: DrawQuit(); break;
                    }
                DrawMessage(646);
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
            Illustration(beakerRect, _beakerArt);
            Illustration(slimeRect, _slimeArt);
            GUI.color = previous;
        }

        void DrawHome()
        {
            Label(new Rect(76, 94, 650, 190), "SLIMA", _titleShadow);
            // Overlap on the pixel grid adds weight without changing the font family.
            for (int x = 0; x <= 2; x++)
                for (int y = 0; y <= 2; y++)
                    Label(new Rect(70 + x, 88 + y, 650, 190), "SLIMA", _title);
            if (Button(new Rect(110, 382, 340, 76), "开始游戏", 36, _homeButton))
            { RefreshSlots(); _message = ""; _page = Page.Slots; }
            if (Button(new Rect(110, 486, 340, 76), "设置", 36, _homeButton)) OpenSettings(Page.Home);
            if (Button(new Rect(110, 590, 340, 76), "退出游戏", 36, _homeButton))
            { _returnPage = Page.Home; _page = Page.ConfirmQuit; }
        }

        void DrawSlots()
        {
            Label(new Rect(100, 142, 700, 54), "选择存档", _heading);
            Label(new Rect(100, 200, 970, 38), "空存档开始新冒险，已有存档继续上次进度。", _small);
            for (int i = 0; i < _slots.Length; i++)
            {
                int slot = i;
                float x = 100 + i * 365;
                SlimaSave save = _slots[i];
                Texture2D card = save == null ? _slotEmptyArt :
                    save.progress != null && save.progress.won ? _slotCompleteArt : _slotSavedArt;
                Image(new Rect(x, 264, 340, 314), card);
                Label(new Rect(x + 24, 288, 220, 42), $"存档 {i + 1:00}", _heading);
                string status = _slotErrors[i] ?? (save == null ? "空存档" :
                    $"{LevelName(save.scenePath)} · {(save.progress != null && save.progress.won ? "已通关" : "冒险中")}");
                Label(new Rect(x + 24, 354, 305, 40), status, _body);
                string time = save == null ? "开启你的第一次冒险" :
                    DateTime.Parse(save.savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind)
                        .ToLocalTime().ToString("yyyy/MM/dd  HH:mm");
                Label(new Rect(x + 24, 401, 300, 36), time, _small);
                GUI.enabled = _slotErrors[i] == null;
                if (Button(new Rect(x + 24, 464, 292, 54), save == null ? "新游戏" : "继续游戏")) StartSlot(slot);
                GUI.enabled = true;
                if (save != null || _slotErrors[i] != null)
                    if (Button(new Rect(x + 24, 529, 292, 32), "重新开始", 16))
                    { _resetSlot = slot; _message = ""; _page = Page.ConfirmReset; }
            }
            if (Button(new Rect(100, 600, 160, 44), "返回")) _page = Page.Home;
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
            Label(new Rect(450, 221, 500, 30), $"当前使用存档 {_slot + 1} · {LevelName(_save.scenePath)}", _small);
            if (Button(new Rect(450, 286, 380, 52), "继续游戏")) Resume();
            if (Button(new Rect(450, 354, 380, 52), "保存进度")) SaveProgress(true);
            if (Button(new Rect(450, 422, 380, 52), "设置")) OpenSettings(Page.Pause);
            if (Button(new Rect(450, 490, 380, 52), "保存并返回主菜单")) ReturnToMenu();
            if (Button(new Rect(450, 558, 380, 52), "保存并退出游戏"))
            { _returnPage = Page.Pause; _page = Page.ConfirmQuit; }
        }

        void DrawReset()
        {
            Image(new Rect(340, 202, 600, 320), _panelArt);
            Label(new Rect(390, 202, 490, 40), $"重新开始存档 {_resetSlot + 1}？", _heading);
            Label(new Rect(390, 312, 700, 40), "这个存档的原有进度将被覆盖。", _body);
            if (Button(new Rect(390, 420, 240, 54), "取消")) _page = Page.Slots;
            if (Button(new Rect(650, 420, 240, 54), "确认重新开始")) StartSlot(_resetSlot, true);
        }

        void DrawQuit()
        {
            Image(new Rect(340, 202, 600, 320), _panelArt);
            Label(new Rect(390, 202, 490, 40), "退出游戏？", _heading);
            Label(new Rect(390, 312, 700, 40), _level == null ? "期待下一次冒险。" : "当前进度会保存到所选存档。", _body);
            if (Button(new Rect(390, 420, 240, 54), "取消")) _page = _returnPage;
            if (Button(new Rect(650, 420, 240, 54), "确认退出")) Quit();
        }

        bool Button(Rect rect, string text, int size = 24, GUIStyle style = null)
        {
            style = style ?? _button;
            style.fontSize = size;
            Color previous = GUI.color;
            bool hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
            GUI.color = !GUI.enabled ? new Color(1f, 1f, 1f, 0.45f) :
                hovered && GUIUtility.hotControl != 0 ? new Color(0.66f, 0.82f, 1f) :
                hovered ? new Color(0.85f, 0.94f, 1f) : Color.white;
            if (_buttonArt != null) GUI.DrawTextureWithTexCoords(rect, _buttonArt, ButtonUv);
            GUI.color = previous;
            bool pressed = GUI.Button(rect, text, style);
            return pressed;
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
