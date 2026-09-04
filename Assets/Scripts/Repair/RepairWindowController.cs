using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Строит UI окна ремонта и реализует FSM диагностического цикла:
/// S0 Idle —> S1 Выбрана неисправность -> S2 Выбрана поломка -> S3 Выбрана диагностика -> S4 В процессе.
/// По завершении диагностики связанные поломки устраняются; когда все устранены — победа.
/// </summary>
public class RepairWindowController : MonoBehaviour
{
    private const float TileWidth = 230f;
    private const float TileHeight = 86f;

    private enum State { Idle, FaultSelected, BreakdownSelected, DiagnosticSelected, Running }

    private class TileView
    {
        public GameObject root;
        public Image image;
        public Image outline;
        public Button button;
        public Text label;
        public Text duration;
        public Image progress;
    }

    private class OvalView
    {
        public GameObject root;
        public SpriteRenderer renderer;
        public BoxCollider2D collider;
        public GameObject outline;
        public TextMesh label;
        public bool visible;
    }

    private RepairGameManager _manager;
    private State _state;
    private RepairFault _selectedFault;
    private RepairBreakdown _selectedBreakdown;
    private RepairDiagnostic _selectedDiagnostic;
    private readonly HashSet<string> _fixedBreakdowns = new HashSet<string>();

    private float _runTimer;
    private float _runDuration;

    private Canvas _canvas;
    private Text _timerLabel;
    private GameObject _windowRoot;
    private GameObject _winOverlay;
    private GameObject _loseOverlay;

    private readonly Dictionary<string, TileView> _faultTiles = new Dictionary<string, TileView>();
    private readonly Dictionary<string, TileView> _diagTiles = new Dictionary<string, TileView>();
    private readonly Dictionary<string, OvalView> _ovals = new Dictionary<string, OvalView>();

    private Transform _shipTransform;
    private Vector2 _shipSize = Vector2.one;
    private float _shipScale = 1f;

    private RectTransform _runArea;
    private Button _runButton;
    private Text _runLabel;
    private TileView _runningDiagTile;
    private readonly List<GameObject> _rhombs = new List<GameObject>();
    private Font _font;

    private Sprite _uiWhite;
    private Sprite _circleSprite;
    private Sprite _ringSprite;
    private Sprite _squareSprite;
    private Image _faultsPanelRoot;

    public void Init(RepairGameManager manager)
    {
        _manager = manager;
        _font = GetBuiltinFont();
        _shipTransform = manager.ShipTransform;

        var sr = _shipTransform.GetComponent<SpriteRenderer>();
        if (sr != null) _shipSize = sr.bounds.size;
        _shipScale = manager.ShipTransform.localScale.x > 0f ? manager.ShipTransform.localScale.x : 1f;

        if (manager.Session != null) manager.Session.BuildLookups();

        _manager.OnWin += ShowWin;
        _manager.OnTimeUp += ShowLose;

        CreateGlyphSprites();
        CreateOvals();
    }

    private void Update()
    {
        if (_manager == null || !_manager.isActiveAndEnabled) return;
        RenderTimer();
        if (!_manager.WindowOpen) return;

        switch (_state)
        {
            case State.Running:
                TickRunning();
                break;
            case State.FaultSelected:
            case State.BreakdownSelected:
            case State.DiagnosticSelected:
                // Клик по любому отображаемому овалу поломки в любой стадии выбора
                // переключает выбранную поломку (и при необходимости — неисправность).
                TryPickOval();
                break;
        }
    }
    /// <summary>Строит всё игровое UI окна ремонта (вызывается RepairGameManager после Init).</summary>
    public void BuildUserInterface()
    {
        _font = GetBuiltinFont();
        CreateEventSystem();
        BuildCanvas();
        BuildTimerBar();
        BuildWindow();
        BuildEndOverlays();
        RenderTimer();
        HideOvals();
    }

    // ---------- Процедурные спрайты ----------
    private void CreateGlyphSprites()
    {
        _uiWhite = Sprite.Create(CreateSolidTexture(4, 4, Color.white), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        _circleSprite = Sprite.Create(CreateCircleTexture(64), new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
        _ringSprite = Sprite.Create(CreateRingTexture(64), new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
        _squareSprite = Sprite.Create(CreateSolidTexture(16, 16, Color.white), new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Texture2D CreateSolidTexture(int w, int h, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r;
                float dy = (y + 0.5f - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - (d - 0.85f) * 6f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>Кольцо: прозрачный центр и прозрачный внешний край, непрозрачное кольцо между ними.
    /// Используется как обводка, чтобы выбранный овал не перекрывался заливкой.</summary>
    private static Texture2D CreateRingTexture(int size)
    {
        const float inner = 0.78f; // внутренняя граница кольца
        const float outer = 1.0f;  // внешняя граница кольца
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r;
                float dy = (y + 0.5f - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((d - inner) / 0.08f) * Mathf.Clamp01((outer - d) / 0.08f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    private static Font GetBuiltinFont()
    {
        // В Unity 6 (6000.x) встроенный шрифт называется LegacyRuntime.ttf —
        // это же имя использует сам UGUI (Text.AssignDefaultFont). Старые имена
        // сохраняем как запасные варианты для других версий Unity.
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacySystem.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    // ---------- EventSystem / Canvas ----------
    private void CreateEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        var moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (moduleType != null) go.AddComponent(moduleType);
        else go.AddComponent<StandaloneInputModule>();
    }

    private void BuildCanvas()
    {
        var go = new GameObject("RepairUI");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
    }

    // ---------- Таймер ----------
    private void BuildTimerBar()
    {
        var bar = CreateRect("TimerBar", _canvas.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(380f, 62f));
        var img = bar.gameObject.AddComponent<Image>();
        img.sprite = _uiWhite;
        img.color = new Color(0.04f, 0.06f, 0.10f, 0.85f);

        _timerLabel = CreateText("TimerLabel", bar, "", 32, TextAnchor.MiddleCenter, Color.white);
        Stretch(_timerLabel.rectTransform, 0f);
    }

    private void RenderTimer()
    {
        if (_timerLabel == null || _manager == null) return;
        float t = Mathf.Max(0f, _manager.CurrentTime);
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t - minutes * 60f);
        _timerLabel.text = "ВРЕМЯ: " + minutes.ToString("00") + ":" + seconds.ToString("00");
        _timerLabel.color = t <= 10f ? new Color(1f, 0.35f, 0.3f) : Color.white;
    }
    // ---------- UI-хелперы ----------
    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    private static void Stretch(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor anchor, Color color)
    {
        var rt = CreateRect(name, parent, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = GetBuiltinFont();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private Image CreatePanel(string name, Transform parent, Color color)
    {
        var rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = _uiWhite;
        img.color = color;
        img.raycastTarget = true;
        return img;
    }

    private Image CreateFilledBar(string name, Transform parent, Color fillColor)
    {
        var rt = CreateRect(name, parent, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = _uiWhite;
        img.color = fillColor;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillAmount = 0f;
        return img;
    }

    private TileView CreateTile(string id, string label, Transform parent, Vector2 pos, Action onClick)
    {
        var bg = CreatePanel(id, parent, _manager.Session.tileColor);
        bg.rectTransform.anchoredPosition = pos;
        bg.rectTransform.sizeDelta = new Vector2(TileWidth, TileHeight);

        var view = new TileView();
        view.root = bg.gameObject;
        view.image = bg;
        view.label = CreateText("Label", bg.transform, label, 24, TextAnchor.MiddleCenter, Color.white);
        Stretch(view.label.rectTransform, 8f);

        view.button = bg.gameObject.AddComponent<Button>();
        view.button.targetGraphic = bg;
        view.button.transition = Selectable.Transition.None;
        view.button.onClick.AddListener(() => onClick());

        // Обводка (выбор) — подложка чуть больше, прозрачная по умолчанию.
        view.outline = CreatePanel("Outline", parent, new Color(1f, 1f, 1f, 0f));
        view.outline.rectTransform.anchoredPosition = pos;
        view.outline.rectTransform.sizeDelta = new Vector2(TileWidth + 12f, TileHeight + 12f);
        view.outline.rectTransform.SetSiblingIndex(bg.transform.GetSiblingIndex());
        return view;
    }

    private TileView CreateDiagnosticTile(string id, string label, Transform parent, Vector2 pos, Action onClick)
    {
        var view = CreateTile(id, label, parent, pos, onClick);

        // Длительность ЧЧ:ММ (показывается с шага выбора поломки).
        var dur = CreateText("Duration", view.root.transform, "", 16, TextAnchor.LowerCenter, new Color(0.85f, 0.9f, 1f));
        dur.rectTransform.anchorMin = new Vector2(0f, 0f);
        dur.rectTransform.anchorMax = new Vector2(1f, 0f);
        dur.rectTransform.anchoredPosition = new Vector2(0f, 14f);
        dur.rectTransform.sizeDelta = new Vector2(-40f, 22f);
        dur.gameObject.SetActive(false);
        view.duration = dur;

        // Прогресс-бар внутри плитки (заполняется в процессе диагностики).
        var progress = CreatePanel("Progress", view.root.transform, new Color(0, 0, 0, 0));
        progress.raycastTarget = false;
        progress.gameObject.SetActive(false);
        view.progress = progress;

        var fill = CreateFilledBar("Fill", progress.transform, Color.white);
        Stretch(fill.rectTransform, 2f);
        progress.color = new Color(0.02f, 0.03f, 0.05f, 0f);
        progress.rectTransform.anchorMin = new Vector2(0f, 0f);
        progress.rectTransform.anchorMax = new Vector2(1f, 0f);
        progress.rectTransform.anchoredPosition = new Vector2(0f, 12f);
        progress.rectTransform.sizeDelta = new Vector2(-44f, 18f);
        view.progress = progress;
        return view;
    }

    private static string FormatMmSs(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int m = total / 60;
        int s = total % 60;
        return m.ToString("00") + ":" + s.ToString("00");
    }

    private static void SetOutline(TileView view, bool enabled, Color color)
    {
        view.outline.color = enabled ? color : new Color(1f, 1f, 1f, 0f);
    }

    private static void SetTileColor(TileView view, Color color)
    {
        view.image.color = color;
    }

    private static void SetTileInteractable(TileView view, bool value)
    {
        view.button.interactable = value;
    }
    // ---------- Окно ремонта ----------
    private void BuildWindow()
    {
        var session = _manager.Session;
        if (session == null) return;

        // Полупрозрачный оверлей на весь экран. Центр не перекрываем, чтобы
        // были видны корабль в доке и овалы поломок на нём.
        var rootRt = CreateRect("RepairWindow", _canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var dim = rootRt.gameObject.AddComponent<Image>();
        dim.sprite = _uiWhite;
        dim.color = new Color(0.02f, 0.03f, 0.06f, 0.5f);
        _windowRoot = rootRt.gameObject;

        // Верхняя полоса: заголовок и кнопка закрытия.
        var header = CreatePanel("Header", rootRt, new Color(0.05f, 0.07f, 0.12f, 0.96f));
        header.rectTransform.anchorMin = new Vector2(0f, 1f);
        header.rectTransform.anchorMax = new Vector2(1f, 1f);
        header.rectTransform.offsetMin = new Vector2(0f, -96f);
        header.rectTransform.offsetMax = Vector2.zero;

        var title = CreateText("Title", header.transform, "РЕМОНТ КОРАБЛЯ", 34, TextAnchor.MiddleCenter, new Color(0.9f, 0.92f, 1f));
        Stretch(title.rectTransform, 0f);

        // Кнопка закрытия окна (таймер снова пойдёт).
        var close = CreatePanel("Close", header.transform, new Color(0.45f, 0.12f, 0.12f, 1f));
        close.rectTransform.anchorMin = new Vector2(1f, 0.5f);
        close.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        close.rectTransform.anchoredPosition = new Vector2(-52f, 0f);
        close.rectTransform.sizeDelta = new Vector2(44f, 44f);
        var closeBtn = close.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = close;
        closeBtn.transition = Selectable.Transition.None;
        closeBtn.onClick.AddListener(() => _manager.CloseWindow());
        var closeLabel = CreateText("X", close.transform, "X", 26, TextAnchor.MiddleCenter, Color.white);
        Stretch(closeLabel.rectTransform, 0f);

        BuildFaultsPanel(rootRt.transform);
        BuildDiagnosticsPanel(rootRt.transform);

        _windowRoot.SetActive(false);
    }

    private void BuildFaultsPanel(Transform panel)
    {
        var session = _manager.Session;
        if (session == null) return;

        // Контейнер и заголовок создаём один раз; плитки пересобираются при каждой сессии.
        if (_faultsPanelRoot == null)
        {
            if (panel == null) return;
            _faultsPanelRoot = CreatePanel("FaultsPanel", panel, new Color(0.04f, 0.06f, 0.10f, 0.85f));
            _faultsPanelRoot.rectTransform.anchoredPosition = new Vector2(-480f, 0f);
            _faultsPanelRoot.rectTransform.sizeDelta = new Vector2(330f, 660f);

            var title = CreateText("Title", _faultsPanelRoot.transform, "НЕИСПРАВНОСТИ", 20, TextAnchor.MiddleCenter, new Color(0.75f, 0.8f, 0.9f));
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(0f, -48f);
            title.rectTransform.offsetMax = new Vector2(0f, -18f);
        }

        // Удалить старые плитки (root и отдельную обводку-подложку) — набор сессии мог измениться.
        foreach (var kv in _faultTiles)
        {
            if (kv.Value == null) continue;
            if (kv.Value.root != null) Destroy(kv.Value.root);
            if (kv.Value.outline != null) Destroy(kv.Value.outline);
        }
        _faultTiles.Clear();

        // Отображаются только неисправности, связанные с поломками текущей сессии.
        int index = 0;
        if (session.faults != null)
        {
            for (int i = 0; i < session.faults.Length; i++)
            {
                var f = session.faults[i];
                if (f == null || !session.IsFaultInSession(f.id)) continue;
                var view = CreateTile(f.id, f.displayName, _faultsPanelRoot.transform, new Vector2(0f, -110f - index * 108f), () => OnFaultClicked(f));
                view.button.interactable = true;
                _faultTiles[f.id] = view;
                index++;
            }
        }
    }

    private void BuildDiagnosticsPanel(Transform panel)
    {
        var session = _manager.Session;
        var col = CreatePanel("DiagnosticsPanel", panel, new Color(0.04f, 0.06f, 0.10f, 0.85f));
        col.rectTransform.anchoredPosition = new Vector2(480f, 0f);
        col.rectTransform.sizeDelta = new Vector2(330f, 660f);

        var title = CreateText("Title", col.transform, "ДИАГНОСТИКА", 20, TextAnchor.MiddleCenter, new Color(0.75f, 0.8f, 0.9f));
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(0f, -48f);
        title.rectTransform.offsetMax = new Vector2(0f, -18f);

        for (int i = 0; i < session.diagnostics.Length; i++)
        {
            var d = session.diagnostics[i];
            var view = CreateDiagnosticTile(d.id, d.displayName, col.transform, new Vector2(0f, -110f - i * 108f), () => OnDiagnosticClicked(d));
            _diagTiles[d.id] = view;
        }

        // Зона запуска — «плавающая»: позиционируется по центру выбранной
        // плитки диагностики (см. Render, состояние DiagnosticSelected).
        _runArea = CreateRect("RunArea", col.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 56f));

        var runBtn = CreatePanel("RunButton", _runArea, _manager.Session.tileColor);
        runBtn.rectTransform.anchoredPosition = Vector2.zero;
        runBtn.rectTransform.sizeDelta = new Vector2(300f, 56f);
        _runButton = runBtn.gameObject.AddComponent<Button>();
        _runButton.targetGraphic = runBtn;
        _runButton.transition = Selectable.Transition.None;
        _runButton.onClick.AddListener(StartRun);

        _runLabel = CreateText("RunLabel", runBtn.transform, "Запустить диагностику", 20, TextAnchor.MiddleCenter, new Color(0.95f, 0.97f, 1f));
        Stretch(_runLabel.rectTransform, 0f);

        _runArea.gameObject.SetActive(false);
    }

    private void BuildEndOverlays()
    {
        _winOverlay = CreateEndOverlay("WinOverlay", "КОРАБЛЬ ОТРЕМОНТИРОВАН!", "Все неисправности устранены. Корабль готов к полёту.", new Color(0.35f, 1f, 0.45f));
        _loseOverlay = CreateEndOverlay("LoseOverlay", "ВРЕМЯ ВЫШЛО!", "Не удалось отремонтировать корабль вовремя.", new Color(1f, 0.4f, 0.35f));
    }

    private GameObject CreateEndOverlay(string name, string titleText, string subtitleText, Color accent)
    {
        var root = CreateRect(name, _canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var dim = root.gameObject.AddComponent<Image>();
        dim.sprite = _uiWhite;
        dim.color = new Color(0f, 0f, 0f, 0.92f);

        var panel = CreatePanel("Box", root, new Color(0.05f, 0.07f, 0.12f, 1f));
        panel.rectTransform.anchoredPosition = Vector2.zero;
        panel.rectTransform.sizeDelta = new Vector2(900f, 460f);

        var t1 = CreateText("Title", panel.transform, titleText, 42, TextAnchor.MiddleCenter, accent);
        t1.rectTransform.anchorMin = new Vector2(0f, 0.68f);
        t1.rectTransform.anchorMax = new Vector2(1f, 1f);
        t1.rectTransform.offsetMin = Vector2.zero;
        t1.rectTransform.offsetMax = Vector2.zero;

        var t2 = CreateText("Sub", panel.transform, subtitleText, 26, TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 0.9f));
        t2.rectTransform.anchorMin = new Vector2(0f, 0.45f);
        t2.rectTransform.anchorMax = new Vector2(1f, 0.68f);
        t2.rectTransform.offsetMin = Vector2.zero;
        t2.rectTransform.offsetMax = Vector2.zero;

        var btn = CreatePanel("RestartButton", panel.transform, new Color(1f, 1f, 1f, 1f));
        btn.rectTransform.anchoredPosition = new Vector2(0f, -150f);
        btn.rectTransform.sizeDelta = new Vector2(280f, 72f);
        var button = btn.gameObject.AddComponent<Button>();
        button.targetGraphic = btn;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => _manager.Restart());
        var btnText = CreateText("Txt", btn.transform, "НАЧАТЬ ЗАНОВО", 28, TextAnchor.MiddleCenter, new Color(0.08f, 0.08f, 0.12f));
        Stretch(btnText.rectTransform, 0f);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }
    // ---------- Жизненный цикл окна ----------
    public void Open()
    {
        if (_windowRoot == null) return;
        ResetToIdle();
        _windowRoot.SetActive(true);
        Render();
    }

    public void Close()
    {
        if (_windowRoot != null) _windowRoot.SetActive(false);
        HideRhomb();
        HideOvals();
    }

    /// <summary>
    /// Полный сброс мини-игры «здесь и сейчас» без перезагрузки сцены.
    /// Восстанавливает исходное состояние дока: пересоздаёт овалы поломок,
    /// очищает прогресс, прячет окно ремонта и финальные оверлеи.
    /// </summary>
    public void ResetGame()
    {
        // Удалить овалы, созданные/оставшиеся от прошлой сессии, и пересоздать все заново.
        foreach (var kv in _ovals)
            if (kv.Value != null && kv.Value.root != null)
                Destroy(kv.Value.root);
        _ovals.Clear();
        _fixedBreakdowns.Clear();
        // Новая случайная сессия: n поломок и m неисправностей на каждую.
        _manager.Session.RollSession();
        // Пересобрать список неисправностей под новую сессию.
        BuildFaultsPanel(null);
        CreateOvals();

        if (_winOverlay != null) _winOverlay.SetActive(false);
        if (_loseOverlay != null) _loseOverlay.SetActive(false);
        if (_windowRoot != null) _windowRoot.SetActive(false);

        ResetToIdle();
        Render();
    }

    private void ResetToIdle()
    {
        _state = State.Idle;
        _selectedFault = null;
        _selectedBreakdown = null;
        _selectedDiagnostic = null;
        _runTimer = 0f;
        _runDuration = 0f;
        _runningDiagTile = null;
        SetRunAreaActive(false);
        HideRhomb();
    }

    // ---------- FSM: переходы ----------
    private void OnFaultClicked(RepairFault fault)
    {
        // Из Running (диагностика в процессе) переключаться нельзя.
        if (_state == State.Running) return;

        _selectedFault = fault;
        _selectedBreakdown = null;
        _selectedDiagnostic = null;
        _runTimer = 0f;
        _runDuration = 0f;
        _runningDiagTile = null;
        _state = State.FaultSelected;
        Render();
    }

    private void TryPickOval()
    {
        if (Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        var cam = Camera.main;
        if (cam == null) return;

        var mousePos = Mouse.current.position.ReadValue();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -cam.transform.position.z));
        var hits = Physics2D.OverlapPointAll(new Vector2(world.x, world.y));
        foreach (var hit in hits)
        {
            foreach (var kv in _ovals)
            {
                var oval = kv.Value;
                // Выбирать можно только овалы, которые сейчас отображаются.
                if (!oval.visible || oval.collider != hit) continue;
                OnBreakdownSelected(kv.Key);
                return;
            }
        }
    }

    private void OnBreakdownSelected(string id)
    {
        var b = _manager.Session.GetBreakdown(id);
        // Поломку можно выбрать только если есть хотя бы одна диагностика, которая её выявляет.
        if (b == null || !AnyDiagnosticRevealsBreakdown(b)) return;
        _selectedBreakdown = b;
        _state = State.BreakdownSelected;
        Render();
    }

    private void OnDiagnosticClicked(RepairDiagnostic diagnostic)
    {
        if (_state != State.BreakdownSelected && _state != State.DiagnosticSelected) return;

        // Повторный клик по выбранной диагностике отменяет выбор —
        // можно вернуться и выбрать другую диагностику (без запуска).
        if (_state == State.DiagnosticSelected && _selectedDiagnostic == diagnostic)
        {
            _selectedDiagnostic = null;
            _state = State.BreakdownSelected;
            Render();
            return;
        }

        _selectedDiagnostic = diagnostic;
        _state = State.DiagnosticSelected;
        Render();
    }

    private void StartRun()
    {
        if (_state != State.DiagnosticSelected || _selectedDiagnostic == null) return;
        _runDuration = Mathf.Max(0.1f, _selectedDiagnostic.duration);
        _runTimer = 0f;
        _state = State.Running;
        _runningDiagTile = _diagTiles[_selectedDiagnostic.id];
        Render();
    }

    private void TickRunning()
    {
        _runTimer += Time.deltaTime;
        float p = Mathf.Clamp01(_runTimer / _runDuration);
        if (_runningDiagTile != null && _runningDiagTile.progress != null)
        {
            var fill = _runningDiagTile.progress.transform.GetChild(0).GetComponent<Image>();
            fill.fillAmount = p;
        }
        if (_runTimer >= _runDuration) CompleteRun();
    }

    private void CompleteRun()
    {
        _state = State.Idle;
        var diag = _selectedDiagnostic;
        foreach (var id in diag.breakdownIds)
        {
            // Устраняются только поломки текущей сессии (остальные просто игнорируем).
            if (!_manager.Session.IsInSession(id)) continue;
            if (!_fixedBreakdowns.Add(id)) continue; // уже устранена ранее
            if (_ovals.TryGetValue(id, out var oval))
            {
                Destroy(oval.root);
                _ovals.Remove(id);
            }
        }
        HideRhomb();
        SetRunAreaActive(false);

        if (_fixedBreakdowns.Count >= _manager.Session.TotalBreakdownCount)
        {
            _manager.ReportWin();
            return;
        }

        ResetToIdle();
        Render();
    }

    private void Render()
    {
        if (_manager == null || _manager.Session == null) return;
        var pal = _manager.Session;

        HideRhomb();

        switch (_state)
        {
            case State.Idle:
                foreach (var kv in _faultTiles)
                {
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, false);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, false);
                }
                SetRunAreaActive(false);
                foreach (var kv in _ovals) SetOvalVisible(kv.Value, false, Color.white, false, pal.outlineColor);
                break;

            case State.FaultSelected:
                foreach (var kv in _faultTiles)
                {
                    bool isSel = kv.Key == _selectedFault.id;
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, isSel, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, false);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, false);
                }
                SetRunAreaActive(false);
                foreach (var kv in _ovals)
                {
                    bool show = BreakdownHasFaultConsequence(kv.Key, _selectedFault);
                    SetOvalVisible(kv.Value, show, pal.tileColor, false, pal.outlineColor);
                }
                break;

            case State.BreakdownSelected:
                foreach (var kv in _faultTiles)
                {
                    bool isCurrent = kv.Key == _selectedFault.id;
                    SetTileColor(kv.Value, pal.tileColor);
                    // Плитки неисправностей остаются кликабельными, чтобы можно было
                    // вернуться и выбрать другую неисправность (и другую поломку).
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, isCurrent, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    bool can = DiagnosticRevealsBreakdown(kv.Key, _selectedBreakdown.id);
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, can);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, can ? FormatMmSs(_manager.Session.GetDiagnostic(kv.Key).duration) : null, can);
                    SetDiagProgressActive(kv.Value, false);
                }
                SetRunAreaActive(false);
                foreach (var kv in _ovals)
                {
                    bool sel = kv.Key == _selectedBreakdown.id;
                    bool show = sel || BreakdownHasFaultConsequence(kv.Key, _selectedFault);
                    // Заливка не меняется (все овалы одного цвета); выбранный получает обводку.
                    SetOvalVisible(kv.Value, show, pal.tileColor, sel, pal.outlineColor);
                }
                break;

            case State.DiagnosticSelected:
                foreach (var kv in _faultTiles)
                {
                    // Тоже можно вернуться к выбору неисправности.
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, kv.Key == _selectedFault.id, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    bool can = DiagnosticRevealsBreakdown(kv.Key, _selectedBreakdown.id);
                    bool sel = kv.Key == _selectedDiagnostic.id;
                    // Кандидаты остаются кликабельными: можно выбрать другую
                    // диагностику или нажать на выбранную, чтобы отменить её.
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, can);
                    SetOutline(kv.Value, sel, pal.outlineColor);
                    SetDiagDuration(kv.Value, can ? FormatMmSs(_manager.Session.GetDiagnostic(kv.Key).duration) : null, can);
                    SetDiagProgressActive(kv.Value, false);
                }
                SetRunAreaActive(true);
                if (_selectedDiagnostic != null && _diagTiles.TryGetValue(_selectedDiagnostic.id, out var selectedTile))
                    PositionRunAreaAtTile(selectedTile);
                _runButton.image.color = pal.tileColor;
                _runLabel.text = "Запустить диагностику";
                foreach (var kv in _ovals)
                {
                    bool show = IsInArray(kv.Key, _selectedDiagnostic.breakdownIds);
                    SetOvalVisible(kv.Value, show, pal.tileColor, false, pal.outlineColor);
                }
                break;

            case State.Running:
                foreach (var kv in _faultTiles)
                {
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, false);
                    SetOutline(kv.Value, false, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    bool sel = kv.Key == _selectedDiagnostic.id;
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, false);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, sel);
                }
                SetRunAreaActive(false);
                foreach (var kv in _ovals)
                {
                    bool show = IsInArray(kv.Key, _selectedDiagnostic.breakdownIds);
                    SetOvalVisible(kv.Value, show, pal.tileColor, false, pal.outlineColor);
                }
                ShowRhomb(pal.progressColor);
                break;
        }
    }

    private void SetRunAreaActive(bool value)
    {
        if (_runArea == null) return;
        _runArea.gameObject.SetActive(value);
    }

    /// <summary>
    /// Центрирует зону запуска на указанной плитке диагностики.
    /// И _runArea, и плитки — прямые дети панели диагностики с якорем-центром,
    /// поэтому достаточно скопировать anchoredPosition плитки.
    /// </summary>
    private void PositionRunAreaAtTile(TileView view)
    {
        if (_runArea == null || view == null || view.root == null) return;
        var tileRt = view.root.GetComponent<RectTransform>();
        if (tileRt == null) return;
        _runArea.anchoredPosition = tileRt.anchoredPosition;
    }

    private bool BreakdownHasFaultConsequence(string breakdownId, RepairFault fault)
    {
        var b = _manager.Session.GetBreakdown(breakdownId);
        // Учитываем только поломки, вошедшие в текущую сессию, и выбранные для них неисправности.
        return b != null
            && _manager.Session.IsInSession(breakdownId)
            && _manager.Session.BreakdownHasFault(breakdownId, fault.id);
    }

    private bool DiagnosticRevealsBreakdown(string diagnosticId, string breakdownId)
    {
        var d = _manager.Session.GetDiagnostic(diagnosticId);
        return d != null && IsInArray(breakdownId, d.breakdownIds);
    }

    private bool AnyDiagnosticRevealsBreakdown(RepairBreakdown b)
    {
        foreach (var d in _manager.Session.diagnostics)
            if (d != null && IsInArray(b.id, d.breakdownIds))
                return true;
        return false;
    }

    private static bool IsInArray(string key, string[] array)
    {
        if (array == null) return false;
        foreach (var a in array)
            if (a == key) return true;
        return false;
    }

    private static void SetDiagDuration(TileView view, string text, bool active)
    {
        if (view.duration == null) return;
        view.duration.text = text ?? "";
        view.duration.gameObject.SetActive(active && !string.IsNullOrEmpty(text));
    }

    private void SetDiagProgressActive(TileView view, bool active)
    {
        if (view.progress == null) return;
        view.progress.gameObject.SetActive(active);
        if (!active) return;
        var fill = view.progress.transform.GetChild(0).GetComponent<Image>();
        fill.color = _manager.Session.progressColor;
        fill.fillAmount = 0f;
    }

    // ---------- Овалы на корабле ----------
    private void CreateOvals()
    {
        var session = _manager.Session;
        var sessionBreakdowns = session.SessionBreakdowns;
        if (sessionBreakdowns == null || sessionBreakdowns.Length == 0) return;

        for (int i = 0; i < sessionBreakdowns.Length; i++)
        {
            var b = sessionBreakdowns[i];
            var go = new GameObject("Oval_" + b.id);
            go.transform.SetParent(_shipTransform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _circleSprite;
            sr.sortingOrder = 2;
            float native = _circleSprite.bounds.size.x;

            Vector2 worldOvalSize = new Vector2(_shipSize.x * session.ovalSize.x, _shipSize.y * session.ovalSize.y);
            float w = Mathf.Max(0.2f, worldOvalSize.x / _shipScale);
            float h = Mathf.Max(0.2f, worldOvalSize.y / _shipScale);
            float px = (b.shipPosition.x - 0.5f) * _shipSize.x / _shipScale;
            float py = (b.shipPosition.y - 0.5f) * _shipSize.y / _shipScale;
            go.transform.localPosition = new Vector3(px, py, 0f);
            go.transform.localScale = new Vector3(w / native, h / native, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;

            var outline = new GameObject("Outline");
            outline.transform.SetParent(go.transform, false);
            var or = outline.AddComponent<SpriteRenderer>();
            or.sprite = _ringSprite;
            or.sortingOrder = 3;
            outline.transform.localScale = new Vector3(1.22f, 1.22f, 1f);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.font = _font;
            tm.text = b.displayName;
            tm.fontSize = 24;
            tm.characterSize = h * 0.10f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            labelGo.transform.localPosition = new Vector3(0f, h * 0.8f, 0f);

            // TextMesh рисуется с sortingOrder 0 по умолчанию и оказался бы под
            // спрайтом корабля (1) и овала (2) — поднимаем метку выше всех.
            var labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null) labelRenderer.sortingOrder = 5;

            var view = new OvalView { root = go, renderer = sr, collider = col, outline = outline, label = tm, visible = false };
            _ovals[b.id] = view;
            SetOvalVisible(view, false, Color.white, false, Color.white);
        }
    }

    private static void SetOvalVisible(OvalView view, bool visible, Color fillColor, bool outlined, Color outlineColor)
    {
        if (view == null || view.root == null) return;
        view.root.SetActive(visible);
        view.visible = visible;
        // Заливку меняем только когда овал видим; при выборе она не меняется —
        // выбранная поломка сохраняет свой цвет и получает лишь обводку.
        view.renderer.color = visible ? fillColor : Color.white;
        var or = view.outline.GetComponent<SpriteRenderer>();
        or.color = visible && outlined ? outlineColor : new Color(1f, 1f, 1f, 0f);
    }

    private void HideOvals()
    {
        foreach (var kv in _ovals)
            SetOvalVisible(kv.Value, false, Color.white, false, Color.white);
    }

    // ---------- Ромб символа диагностики ----------
    private void ShowRhomb(Color color)
    {
        if (_selectedDiagnostic == null || _rhombs.Count > 0) return;
        var ids = _selectedDiagnostic.breakdownIds;
        if (ids == null || ids.Length == 0) return;

        foreach (var id in ids)
        {
            // Ромб появляется на каждой поломке, которую выявляет эта диагностика.
            if (!_ovals.TryGetValue(id, out var ov) || !ov.visible) continue;
            CreateRhomb(ov.root.transform.position, color);
        }
    }

    private void CreateRhomb(Vector2 worldPos, Color color)
    {
        var session = _manager.Session;
        if (session == null) return;

        var go = new GameObject("DiagRhomb");
        go.transform.SetParent(_shipTransform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _squareSprite;
        sr.sortingOrder = 4;
        sr.color = color;

        // Повёрнутый на 45° квадрат образует ромб, чья диагональ (расстояние между
        // противоположными вершинами) равна стороне квадрата * √2. Чтобы диагональ ромба
        // совпала с вертикальным размером овала поломки (ovalSize.y), сторона квадрата
        // должна быть _shipSize.y * ovalSize.y / √2.
        float native = _squareSprite.bounds.size.x;
        float rhombSideWorld = (_shipSize.y * session.ovalSize.y) / Mathf.Sqrt(2f);
        float rhombSideLocal = rhombSideWorld / _shipScale;
        go.transform.localScale = new Vector3(rhombSideLocal / native, rhombSideLocal / native, 1f);

        Vector2 local = ((Vector2)worldPos - (Vector2)_shipTransform.position) / _shipScale;
        go.transform.localPosition = new Vector3(local.x, local.y, 0f);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        _rhombs.Add(go);
    }

    private void HideRhomb()
    {
        foreach (var rhomb in _rhombs)
            if (rhomb != null)
                Destroy(rhomb);
        _rhombs.Clear();
    }

    // ---------- Победа / поражение ----------
    private void ShowWin()
    {
        _winOverlay.SetActive(true);
    }

    private void ShowLose()
    {
        _loseOverlay.SetActive(true);
        if (_windowRoot != null) _windowRoot.SetActive(false);
    }
}
