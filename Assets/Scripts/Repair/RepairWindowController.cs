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

    private enum State { Idle, FaultSelected, BreakdownSelected, DiagnosticSelected, Running, RepairSelected, Repairing, RepairDone }

    private class TileView
    {
        public GameObject root;
        public Image image;
        public Image outline;
        public Button button;
        public Text label;
        public Text duration;
        public Image durationPlate;
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

    private class RepairButtonView
    {
        public Image panel;   // фон кнопки (затеняется у второй кнопки)
        public Text caption;  // надпись кнопки («Я ремонт» / «Робот ремонт»)
        public string captionText; // исходная надпись (восстанавливается при сбросе)
        public Text values;   // строка «ММ:СС · плата N» (скрывается на активной кнопке)
        public Image fill;    // прогресс-бар ремонта (виден только на нажатой кнопке)
    }

    private RepairGameManager _manager;
    private State _state;
    private RepairFault _selectedFault;
    private RepairBreakdown _selectedBreakdown;
    private RepairDiagnostic _selectedDiagnostic;
    private readonly HashSet<string> _fixedBreakdowns = new HashSet<string>();
    private readonly HashSet<string> _repairedBreakdowns = new HashSet<string>(); // Поломки, устранённые ремонтом (окрашиваются repairingColor).
    private readonly HashSet<string> _completedDiagnostics = new HashSet<string>();

    private float _runTimer;
    private float _runDuration;

    private Canvas _canvas;
    private Text _timerLabel;
    private GameObject _windowRoot;
    private GameObject _winOverlay;
    private GameObject _loseOverlay;
    private GameObject _repairPanel;      // Окно починки диагностированной поломки (низ экрана)
    private Text _repairTitle;
    private RepairButtonView _humanButton;   // «Я ремонт»
    private RepairButtonView _robotButton;   // «Робот ремонт»
    private bool _repairingIsHuman;       // Какая кнопка ремонта нажата (в состоянии Repairing)
    private float _repairTimer;
    private float _repairDuration;

    private Text _moneyLabel;             // Счётчик денег в шапке окна ремонта

    private readonly Dictionary<string, TileView> _faultTiles = new Dictionary<string, TileView>();
    private readonly Dictionary<string, TileView> _diagTiles = new Dictionary<string, TileView>();
    private readonly Dictionary<string, OvalView> _ovals = new Dictionary<string, OvalView>();

    private Transform _shipTransform;
    private Vector2 _shipSize = Vector2.one;
    private float _shipScale = 1f;

    private TileView _runningDiagTile;
    private readonly List<GameObject> _rhombs = new List<GameObject>();
    private Font _font;

    private Sprite _uiWhite;
    private Sprite _uiRounded;  // Скруглённый 9-slice спрайт для плиток и их подложек
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
        _manager.OnMoneyChanged += UpdateMoneyLabel;

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
            case State.Repairing:
                TickRepair();
                break;
            case State.Idle:
            case State.FaultSelected:
            case State.BreakdownSelected:
            case State.DiagnosticSelected:
                // Поломку можно выбрать сразу на любом шаге, в том числе в Idle —
                // без предварительного выбора неисправности или диагностики.
                TryPickOval(false);
                break;
            case State.RepairSelected:
            case State.RepairDone:
                // Отвечают все овалы: клик по диагностированной переводит выбор
                // (и окно починки) на неё, клик по неотдиагностированной выбирает её
                // для диагностики (окно починки закрывается), отремонтированная
                // в RepairDone закрывает окно починки (см. OnBreakdownSelected).
                TryPickOval(false);
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
        // До открытия окна ремонта поломки на корабле не показываются.
        HideOvals();
    }

    // ---------- Процедурные спрайты ----------
    private void CreateGlyphSprites()
    {
        _uiWhite = Sprite.Create(CreateSolidTexture(4, 4, Color.white), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        // Скруглённые углы плиток: 9-slice спрайт с рамкой 8px — углы остаются
        // скруглёнными при любом размере изображения (плитки 230x86, подложки уже).
        _uiRounded = Sprite.Create(CreateRoundedTexture(32, 8), new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
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

    /// <summary>Белая текстура с небольшими скруглёнными углами (сглаженный край ~1px).
    /// Используется как 9-slice спрайт: рамка равна радиусу, углы не растягиваются.</summary>
    private static Texture2D CreateRoundedTexture(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = radius;
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Знаковое расстояние (SDF) до скруглённого прямоугольника с центром (half, half).
                float dx = Mathf.Abs(x + 0.5f - half) - (half - r);
                float dy = Mathf.Abs(y + 0.5f - half) - (half - r);
                float d = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                          + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
            }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
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

    /// <summary>Задаёт изображению скруглённый 9-slice спрайт (плитки и их подложки).</summary>
    private void MakeRounded(Image img)
    {
        if (img == null || _uiRounded == null) return;
        img.sprite = _uiRounded;
        img.type = Image.Type.Sliced;
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
        MakeRounded(bg); // небольшие скругления углов плитки
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
        MakeRounded(view.outline); // те же скругления, чтобы рамка выбора не торчала по углам
        view.outline.rectTransform.anchoredPosition = pos;
        view.outline.rectTransform.sizeDelta = new Vector2(TileWidth + 12f, TileHeight + 12f);
        view.outline.rectTransform.SetSiblingIndex(bg.transform.GetSiblingIndex());
        return view;
    }

    private TileView CreateDiagnosticTile(string id, string label, Transform parent, Vector2 pos, Action onClick)
    {
        var view = CreateTile(id, label, parent, pos, onClick);

        // Длительность ММ:СС: подпись крупнее и на небольшой подложке цвета обводки,
        // чтобы её было хорошо видно на заливке плитки. Показывается с шага выбора поломки.
        var dur = CreateText("Duration", view.root.transform, "", 20, TextAnchor.LowerCenter, ReadableTextColor(_manager.Session.outlineColor));
        dur.rectTransform.anchorMin = new Vector2(0f, 0f);
        dur.rectTransform.anchorMax = new Vector2(1f, 0f);
        dur.rectTransform.anchoredPosition = new Vector2(0f, 1f); // на полвысоты строки ниже, чтобы подложка не задевала название
        dur.rectTransform.sizeDelta = new Vector2(-44f, 26f);
        dur.gameObject.SetActive(false);

        // Подложка под длительностью: чуть шире и выше текста, цвет — из палитры (outlineColor).
        // Смещается вниз вместе со строкой (на полвысоты строки), чтобы не перекрывать название.
        var plate = CreatePanel("DurationPlate", view.root.transform, _manager.Session.outlineColor);
        MakeRounded(plate); // небольшие скругления углов подложки времени
        plate.rectTransform.anchorMin = new Vector2(0f, 0f);
        plate.rectTransform.anchorMax = new Vector2(1f, 0f);
        plate.rectTransform.anchoredPosition = new Vector2(0f, 0f);
        plate.rectTransform.sizeDelta = new Vector2(-28f, 30f);
        plate.raycastTarget = false; // не должна перехватывать клики по плитке
        plate.transform.SetSiblingIndex(0); // позади текста длительности
        plate.gameObject.SetActive(false);

        view.durationPlate = plate;
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

        // Общего затемняющего оверлея больше нет: корабль в доке и овалы поломок
        // рисуются на нём без затемнения, прямо поверх спрайта корабля.
        // root — прозрачный контейнер для трёх отдельных полупрозрачных окон
        // (неисправности слева, диагностики справа, ремонт снизу) и шапки;
        // его SetActive включает/выключает всё сразу.
        var rootRt = CreateRect("RepairWindow", _canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _windowRoot = rootRt.gameObject;

        // Верхняя полоса: заголовок и кнопка закрытия.
        var header = CreatePanel("Header", rootRt, new Color(0.05f, 0.07f, 0.12f, 0.96f));
        header.rectTransform.anchorMin = new Vector2(0f, 1f);
        header.rectTransform.anchorMax = new Vector2(1f, 1f);
        header.rectTransform.offsetMin = new Vector2(0f, -96f);
        header.rectTransform.offsetMax = Vector2.zero;

        var title = CreateText("Title", header.transform, "РЕМОНТ КОРАБЛЯ", 34, TextAnchor.MiddleCenter, new Color(0.9f, 0.92f, 1f));
        Stretch(title.rectTransform, 0f);

        // Счётчик денег: слева в шапке; растёт на плату за каждый выполненный ремонт.
        _moneyLabel = CreateText("Money", header.transform, "", 26, TextAnchor.MiddleLeft, new Color(0.95f, 0.85f, 0.4f));
        _moneyLabel.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        _moneyLabel.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        _moneyLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
        _moneyLabel.rectTransform.anchoredPosition = new Vector2(24f, 0f);
        _moneyLabel.rectTransform.sizeDelta = new Vector2(340f, 44f);
        UpdateMoneyLabel(_manager.Money);

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
        BuildRepairPanel(rootRt.transform);

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
    }

    // ---------- Окно починки диагностированной поломки ----------
    private void BuildRepairPanel(Transform panel)
    {
        var session = _manager.Session;
        if (session == null || panel == null) return;

        // Окно починки внизу по центру: две кнопки — ручной и роботизированный ремонт.
        var root = CreatePanel("RepairPanel", panel, new Color(0.04f, 0.06f, 0.10f, 0.92f));
        var rt = root.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 16f);
        rt.sizeDelta = new Vector2(640f, 150f);
        _repairPanel = root.gameObject;

        _repairTitle = CreateText("Title", root.transform, "", 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.88f, 0.95f));
        _repairTitle.rectTransform.anchorMin = new Vector2(0f, 0.74f);
        _repairTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
        _repairTitle.rectTransform.offsetMin = Vector2.zero;
        _repairTitle.rectTransform.offsetMax = Vector2.zero;

        _humanButton = CreateRepairButton("HumanRepair", root.transform, new Vector2(-165f, -10f), "Я ремонт", () => OnRepairOptionClicked(true));
        _robotButton = CreateRepairButton("RobotRepair", root.transform, new Vector2(165f, -10f), "Робот ремонт", () => OnRepairOptionClicked(false));

        _repairPanel.SetActive(false);
    }

    private RepairButtonView CreateRepairButton(string name, Transform parent, Vector2 position, string caption, UnityEngine.Events.UnityAction onClick)
    {
        var p = CreatePanel(name, parent, new Color(0.10f, 0.14f, 0.24f, 1f));
        p.rectTransform.anchoredPosition = position;
        p.rectTransform.sizeDelta = new Vector2(300f, 88f);

        var button = p.gameObject.AddComponent<Button>();
        button.targetGraphic = p;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);

        var cap = CreateText("Caption", p.transform, caption, 24, TextAnchor.MiddleCenter, Color.white);
        cap.rectTransform.anchorMin = new Vector2(0f, 0.52f);
        cap.rectTransform.anchorMax = new Vector2(1f, 1f);
        cap.rectTransform.offsetMin = Vector2.zero;
        cap.rectTransform.offsetMax = Vector2.zero;

        var values = CreateText("Values", p.transform, "", 19, TextAnchor.MiddleCenter, new Color(0.75f, 0.82f, 0.92f));
        values.rectTransform.anchorMin = new Vector2(0f, 0f);
        values.rectTransform.anchorMax = new Vector2(1f, 0.52f);
        values.rectTransform.offsetMin = Vector2.zero;
        values.rectTransform.offsetMax = Vector2.zero;

        // Прогресс-бар ремонта — в той же зоне, что и цифры; виден только на нажатой кнопке.
        var fill = CreateFilledBar("RepairFill", p.transform, Color.white);
        fill.rectTransform.anchorMin = new Vector2(0f, 0f);
        fill.rectTransform.anchorMax = new Vector2(1f, 0.52f);
        fill.rectTransform.offsetMin = new Vector2(10f, 6f);
        fill.rectTransform.offsetMax = new Vector2(-10f, -4f);
        fill.fillAmount = 0f;
        fill.gameObject.SetActive(false);

        return new RepairButtonView { panel = p, values = values, fill = fill, caption = cap, captionText = caption };
    }

    private void ShowRepairPanel(bool active)
    {
        if (_repairPanel == null) return;
        if (!active || _selectedBreakdown == null)
        {
            _repairPanel.SetActive(false);
            return;
        }

        if (_repairTitle != null)
            _repairTitle.text = "ПОЧИНКА: " + _selectedBreakdown.displayName;

        // При каждом показе окна кнопки возвращаются в исходное состояние:
        // цифры видны, прогресс-бар скрыт, затенения нет.
        ResetRepairButton(_humanButton);
        ResetRepairButton(_robotButton);
        // Длительность и плата берутся из параметров выбранной поломки.
        if (_humanButton != null && _humanButton.values != null)
            _humanButton.values.text = FormatMmSs(_selectedBreakdown.humanRepairDuration) + "  ·  плата " + _selectedBreakdown.humanRepairCost;
        if (_robotButton != null && _robotButton.values != null)
            _robotButton.values.text = FormatMmSs(_selectedBreakdown.robotRepairDuration) + "  ·  плата " + _selectedBreakdown.robotRepairCost;

        _repairPanel.SetActive(true);
    }

    private static readonly Color RepairButtonNormalColor = new Color(0.10f, 0.14f, 0.24f, 1f);
    private static readonly Color RepairButtonShadedColor = new Color(0.04f, 0.05f, 0.09f, 1f);

    private static void ResetRepairButton(RepairButtonView view)
    {
        if (view == null) return;
        if (view.panel != null) view.panel.color = RepairButtonNormalColor;
        if (view.caption != null)
        {
            // Надпись кнопки («Я ремонт» / «Робот ремонт») возвращается к исходной.
            view.caption.text = view.captionText;
            view.caption.color = Color.white;
        }
        if (view.values != null)
        {
            view.values.gameObject.SetActive(true);
            view.values.color = new Color(0.75f, 0.82f, 0.92f, 1f);
        }
        if (view.fill != null)
        {
            view.fill.gameObject.SetActive(false);
            view.fill.fillAmount = 0f;
        }
    }

    /// <summary>Состояние кнопок во время ремонта: активная показывает прогресс-бар, вторая затенена.</summary>
    private void ApplyRepairingButtons()
    {
        var active = _repairingIsHuman ? _humanButton : _robotButton;
        var idle = _repairingIsHuman ? _robotButton : _humanButton;

        if (idle != null)
        {
            // Вторая кнопка затеняется: тёмный фон и приглушённая подпись.
            if (idle.panel != null) idle.panel.color = RepairButtonShadedColor;
            if (idle.values != null) idle.values.color = new Color(0.75f, 0.82f, 0.92f, 0.35f);
        }
        if (active != null)
        {
            // На нажатой кнопке цифры заменяются прогресс-баром цвета repairingColor.
            if (active.values != null) active.values.gameObject.SetActive(false);
            if (active.fill != null)
            {
                active.fill.color = _manager.Session.repairingColor;
                active.fill.gameObject.SetActive(true);
            }
        }
    }

    // Запуск ремонта: переход FSM в состояние Repairing.
    private void OnRepairOptionClicked(bool isHuman)
    {
        // Клик по любой кнопке после завершённого ремонта («Выполнен») закрывает окно починки.
        if (_state == State.RepairDone)
        {
            ResetToIdle();
            Render();
            return;
        }

        if (_state != State.RepairSelected || _selectedBreakdown == null) return;

        _repairingIsHuman = isHuman;
        // Длительность — из параметров выбранной поломки.
        float duration = isHuman ? _selectedBreakdown.humanRepairDuration : _selectedBreakdown.robotRepairDuration;
        _repairDuration = Mathf.Max(0.1f, duration);
        _repairTimer = 0f;
        _state = State.Repairing;
        Render();
    }

    /// <summary>Кадр ремонта: прогресс-бар на кнопке и мерцание заливки ремонтируемой поломки.</summary>
    private void TickRepair()
    {
        _repairTimer += Time.deltaTime;
        float p = Mathf.Clamp01(_repairTimer / _repairDuration);

        var view = _repairingIsHuman ? _humanButton : _robotButton;
        if (view != null && view.fill != null)
            view.fill.fillAmount = p;

        // Ремонтируемая поломка медленно мерцает: яркость заливки колеблется вокруг repairingColor.
        if (_selectedBreakdown != null && _ovals.TryGetValue(_selectedBreakdown.id, out var ov) && ov.renderer != null)
        {
            var c = _manager.Session.repairingColor;
            float pulse = 0.7f + 0.3f * Mathf.PingPong(Time.time * 1.5f, 1f);
            ov.renderer.color = new Color(c.r * pulse, c.g * pulse, c.b * pulse, c.a);
        }

        if (_repairTimer >= _repairDuration) CompleteRepair();
    }

    private void CompleteRepair()
    {
        if (_selectedBreakdown == null)
        {
            ResetToIdle();
            Render();
            return;
        }

        // Отремонтированная поломка остаётся на корабле и окрашивается repairingColor.
        _repairedBreakdowns.Add(_selectedBreakdown.id);
        _fixedBreakdowns.Add(_selectedBreakdown.id);

        // На счёт зачисляется плата за выполненный ремонт, счётчик денег обновляется.
        int fee = _repairingIsHuman ? _selectedBreakdown.humanRepairCost : _selectedBreakdown.robotRepairCost;
        _manager.AddMoney(fee);

        // Неисправности, причиной которых была эта поломка, исчезают из панели,
        // если ни одна другая не устранённая поломка сессии их больше не влечёт.
        RemoveFaultTilesForBreakdown(_selectedBreakdown.id);

        if (_fixedBreakdowns.Count >= _manager.Session.TotalBreakdownCount)
        {
            _manager.ReportWin();
            return;
        }

        // Окно починки остаётся открытым: на нажатой кнопке — «Выполнен» (RepairDone).
        _state = State.RepairDone;
        Render();
    }

    /// <summary>
    /// После ремонта поломки её неисправности исчезают из панели неисправностей,
    /// но только если ни одна другая ещё не устранённая поломка сессии их не влечёт.
    /// </summary>
    private void RemoveFaultTilesForBreakdown(string breakdownId)
    {
        var session = _manager.Session;

        foreach (var faultId in session.GetFaultsForBreakdown(breakdownId))
        {
            bool stillEntailed = false;
            foreach (var other in session.SessionBreakdowns)
            {
                if (other == null || other.id == breakdownId) continue;
                // Отремонтированные поломки больше не влекут своих неисправностей.
                if (_repairedBreakdowns.Contains(other.id)) continue;
                if (session.BreakdownHasFault(other.id, faultId))
                {
                    stillEntailed = true;
                    break;
                }
            }
            if (stillEntailed) continue;

            if (_faultTiles.TryGetValue(faultId, out var view))
            {
                if (view.root != null) Destroy(view.root);
                if (view.outline != null) Destroy(view.outline);
                _faultTiles.Remove(faultId);
            }
        }

        // Оставшиеся плитки уезжают вверх, чтобы в панели не оставалось «дыр».
        int index = 0;
        foreach (var kv in _faultTiles)
        {
            if (kv.Value == null || kv.Value.image == null) continue;
            kv.Value.image.rectTransform.anchoredPosition = new Vector2(0f, -110f - index * 108f);
            index++;
        }
    }

    /// <summary>Кнопки починки после завершённого ремонта: на нажатой «Выполнен», вторая затенена.</summary>
    private void ApplyDoneRepairButtons()
    {
        var active = _repairingIsHuman ? _humanButton : _robotButton;
        var idle = _repairingIsHuman ? _robotButton : _humanButton;

        if (idle != null)
        {
            if (idle.panel != null) idle.panel.color = RepairButtonShadedColor;
            if (idle.values != null) idle.values.color = new Color(0.75f, 0.82f, 0.92f, 0.35f);
        }
        if (active != null)
        {
            // Текст на кнопке ремонта сменяется «Выполнен»; цифры и прогресс-бар скрываются.
            if (active.caption != null) active.caption.text = "Выполнен";
            if (active.values != null) active.values.gameObject.SetActive(false);
            if (active.fill != null)
            {
                active.fill.fillAmount = 1f;
                active.fill.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateMoneyLabel(int money)
    {
        if (_moneyLabel == null) return;
        _moneyLabel.text = "СЧЕТ: " + money;
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
        _repairedBreakdowns.Clear();
        _completedDiagnostics.Clear();
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
        // Окно после рестарта закрыто — поломки остаются скрытыми до клика по кораблю.
        HideOvals();
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
        _repairingIsHuman = false;
        _repairTimer = 0f;
        _repairDuration = 0f;
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

    private void TryPickOval(bool onlyDetected)
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
                // При onlyDetected откликаются только диагностированные поломки.
                if (onlyDetected && !_fixedBreakdowns.Contains(kv.Key)) continue;
                OnBreakdownSelected(kv.Key);
                return;
            }
        }
    }

    private void OnBreakdownSelected(string id)
    {
        var b = _manager.Session.GetBreakdown(id);
        if (b == null) return;

        // Уже отремонтированная поломка: в RepairDone клик по ней закрывает окно
        // починки (подтверждение «Выполнен»); в остальных состояниях не откликается.
        if (_repairedBreakdowns.Contains(id))
        {
            if (_state == State.RepairDone)
            {
                ResetToIdle();
                Render();
            }
            return;
        }

        // Клик по диагностированной поломке открывает окно починки (новое состояние FSM).
        if (_fixedBreakdowns.Contains(id))
        {
            _selectedBreakdown = b;
            _selectedFault = null;
            _selectedDiagnostic = null;
            _state = State.RepairSelected;
            Render();
            return;
        }

        // Поломку можно выбрать только если есть хотя бы одна диагностика, которая её выявляет.
        if (!AnyDiagnosticRevealsBreakdown(b)) return;
        _selectedBreakdown = b;
        _state = State.BreakdownSelected;
        Render();
    }

    private void OnDiagnosticClicked(RepairDiagnostic diagnostic)
    {
        // Плитки диагностик кликабельны на любом шаге, кроме момента выполнения
        // текущей диагностики (внутри Running переключиться нельзя).
        if (_state == State.Running) return;

        // Выполненную диагностику нельзя выбрать и запустить повторно:
        // клик по её плитке игнорируется, текущий выбор не сбрасывается.
        if (diagnostic == null || _completedDiagnostics.Contains(diagnostic.id)) return;

        // Повторный клик по уже выбранной диагностике запускает её.
        if (_state == State.DiagnosticSelected && _selectedDiagnostic == diagnostic)
        {
            StartRun();
            return;
        }

        // Клик по другой (или новой) диагностике — просто переключает выбор:
        // название плитки меняется на строку запуска, предыдущая восстанавливает имя.
        _selectedDiagnostic = diagnostic;
        _state = State.DiagnosticSelected;
        Render();
    }

    private void StartRun()
    {
        if (_state != State.DiagnosticSelected || _selectedDiagnostic == null) return;
        // Страховка: выполненную диагностику запустить повторно нельзя.
        if (_completedDiagnostics.Contains(_selectedDiagnostic.id)) return;
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
        var diag = _selectedDiagnostic;
        // Диагностика считается выполненной — надпись на её плитке станет «Выполнена».
        if (diag != null && !string.IsNullOrEmpty(diag.id)) _completedDiagnostics.Add(diag.id);

        // Поломки не удаляются: помечаем их устранёнными, Render() окрасит их в detectedColor.
        foreach (var id in diag.breakdownIds)
        {
            // Устраняются только поломки текущей сессии (остальные просто игнорируем).
            if (!_manager.Session.IsInSession(id)) continue;
            _fixedBreakdowns.Add(id); // повторная диагностика уже устранённой поломки ничего не меняет
        }
        HideRhomb();

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
        // Окно починки видно только в состоянии RepairSelected (включается в конце switch).
        ShowRepairPanel(false);

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
                    SetDiagTileLabel(kv.Value, _manager.Session.GetDiagnostic(kv.Key), _completedDiagnostics.Contains(kv.Key) ? "Выполнена" : null);
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, false);
                }
                foreach (var kv in _ovals)
                {
                    // Все поломки остаются видимыми; отремонтированные окрашены repairingColor,
                    // диагностированные — detectedColor.
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : pal.tileColor);
                    SetOvalVisible(kv.Value, true, fill, false, pal.outlineColor);
                }
                break;

            case State.FaultSelected:
                foreach (var kv in _faultTiles)
                {
                    bool isSel = kv.Key == _selectedFault.id;
                    SetTileColor(kv.Value, isSel ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, isSel, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    SetDiagTileLabel(kv.Value, _manager.Session.GetDiagnostic(kv.Key), _completedDiagnostics.Contains(kv.Key) ? "Выполнена" : null);
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, false);
                }
                foreach (var kv in _ovals)
                {
                    // Все поломки остаются видимыми; связанные с неисправностью
                    // подсвечиваются, диагностированные отмечены detectedColor,
                    // отремонтированные — repairingColor.
                    bool related = BreakdownHasFaultConsequence(kv.Key, _selectedFault);
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : (related ? pal.selectedTileColor : pal.tileColor));
                    SetOvalVisible(kv.Value, true, fill, false, pal.outlineColor);
                }
                break;

            case State.BreakdownSelected:
                foreach (var kv in _faultTiles)
                {
                    // Подсвечиваются неисправности, которые может повлечь выбранная поломка
                    // (в рамках текущей сессии). Обводка с них снимается.
                    bool isConsequence = _manager.Session.BreakdownHasFault(_selectedBreakdown.id, kv.Key);
                    SetTileColor(kv.Value, isConsequence ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    // Диагностики, которые могут выявить выбранную поломку, подсвечиваются.
                    // Все плитки остаются кликабельными.
                    bool canReveal = DiagnosticRevealsBreakdown(kv.Key, _selectedBreakdown.id);
                    SetDiagTileLabel(kv.Value, _manager.Session.GetDiagnostic(kv.Key), _completedDiagnostics.Contains(kv.Key) ? "Выполнена" : null);
                    SetTileColor(kv.Value, canReveal ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, false);
                }
                foreach (var kv in _ovals)
                {
                    // Все поломки остаются видимыми; выбранная подсвечена и обведена,
                    // диагностированные — detectedColor, отремонтированные — repairingColor.
                    bool sel = kv.Key == _selectedBreakdown.id;
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : (sel ? pal.selectedTileColor : pal.tileColor));
                    SetOvalVisible(kv.Value, true, fill, sel, pal.outlineColor);
                }
                break;

            case State.DiagnosticSelected:
                foreach (var kv in _faultTiles)
                {
                    // Все неисправности остаются базового цвета при выбранной диагностике.
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    bool sel = kv.Key == _selectedDiagnostic.id;
                    // Все диагностики кликабельны: можно выбрать другую
                    // диагностику, повторный клик по выбранной запускает её.
                    var d = _manager.Session.GetDiagnostic(kv.Key);
                    SetDiagTileLabel(kv.Value, d, sel ? "запустить диагностику" : _completedDiagnostics.Contains(kv.Key) ? "Выполнена" : null);
                    SetTileColor(kv.Value, sel ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, sel, pal.outlineColor);
                    SetDiagDuration(kv.Value, sel ? FormatMmSs(d.duration) : null, sel);
                    SetDiagProgressActive(kv.Value, false);
                }
                foreach (var kv in _ovals)
                {
                    // Поломки не скрываются; выявляемые подсвечиваются,
                    // диагностированные — detectedColor, отремонтированные — repairingColor.
                    bool detected = IsInArray(kv.Key, _selectedDiagnostic.breakdownIds);
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : (detected ? pal.selectedTileColor : pal.tileColor));
                    SetOvalVisible(kv.Value, true, fill, false, pal.outlineColor);
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
                    // В процессе выполнения на тайле диагностики надпись меняется на статус.
                    SetDiagTileLabel(kv.Value, _manager.Session.GetDiagnostic(kv.Key), sel ? "В ПРОЦЕССЕ..." : _completedDiagnostics.Contains(kv.Key) ? "Выполнена" : null);
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, false);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, sel);
                }
                foreach (var kv in _ovals)
                {
                    // Поломки не скрываются, цвет обычный; выявляемые диагностикой
                    // отмечаются ромбом (ShowRhomb), диагностированные — detectedColor,
                    // отремонтированные — repairingColor.
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : pal.tileColor);
                    SetOvalVisible(kv.Value, true, fill, false, pal.outlineColor);
                }
                ShowRhomb(pal.progressColor);
                break;

            case State.RepairSelected:
                foreach (var kv in _faultTiles)
                {
                    // Подсвечиваются неисправности, которые может повлечь диагностированная
                    // поломка и которые есть на этом корабле (в рамках текущей сессии).
                    bool isConsequence = _manager.Session.BreakdownHasFault(_selectedBreakdown.id, kv.Key);
                    SetTileColor(kv.Value, isConsequence ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    // Диагностики, которые могут выявить эту поломку, подсвечиваются;
                    // длительность — только у ещё не выполненных.
                    bool canReveal = DiagnosticRevealsBreakdown(kv.Key, _selectedBreakdown.id);
                    bool completed = _completedDiagnostics.Contains(kv.Key);
                    var d = _manager.Session.GetDiagnostic(kv.Key);
                    SetDiagTileLabel(kv.Value, d, completed ? "Выполнена" : null);
                    SetTileColor(kv.Value, canReveal ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, canReveal && !completed ? FormatMmSs(d.duration) : null, canReveal && !completed);
                    SetDiagProgressActive(kv.Value, false);
                }
                foreach (var kv in _ovals)
                {
                    // Диагностированная поломка не пропадает: остаётся detectedColor,
                    // отремонтированные — repairingColor, выбранная обводится outlineColor.
                    bool sel = kv.Key == _selectedBreakdown.id;
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : pal.tileColor);
                    SetOvalVisible(kv.Value, true, fill, sel, pal.outlineColor);
                }
                ShowRepairPanel(true);
                break;

            case State.Repairing:
                // Панели — как при выборе починки, но переключаться во время ремонта нельзя.
                foreach (var kv in _faultTiles)
                {
                    bool isConsequence = _manager.Session.BreakdownHasFault(_selectedBreakdown.id, kv.Key);
                    SetTileColor(kv.Value, isConsequence ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, false);
                    SetOutline(kv.Value, false, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    bool canReveal = DiagnosticRevealsBreakdown(kv.Key, _selectedBreakdown.id);
                    bool completed = _completedDiagnostics.Contains(kv.Key);
                    var d = _manager.Session.GetDiagnostic(kv.Key);
                    SetDiagTileLabel(kv.Value, d, completed ? "Выполнена" : null);
                    SetTileColor(kv.Value, canReveal ? pal.selectedTileColor : pal.tileColor);
                    SetTileInteractable(kv.Value, false);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, canReveal && !completed ? FormatMmSs(d.duration) : null, canReveal && !completed);
                    SetDiagProgressActive(kv.Value, false);
                }
                foreach (var kv in _ovals)
                {
                    // Ремонтируемая поломка заливается repairingColor (мерцание — в TickRepair);
                    // отремонтированные остаются repairingColor, диагностированные — detectedColor.
                    bool repairing = kv.Key == _selectedBreakdown.id;
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairing || repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : pal.tileColor);
                    SetOvalVisible(kv.Value, true, fill, repairing, pal.outlineColor);
                }
                ShowRepairPanel(true);
                ApplyRepairingButtons();
                break;

            case State.RepairDone:
                // Ремонт завершён: панели снова активны, на нажатой кнопке «Выполнен».
                foreach (var kv in _faultTiles)
                {
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                }
                foreach (var kv in _diagTiles)
                {
                    bool completed = _completedDiagnostics.Contains(kv.Key);
                    SetDiagTileLabel(kv.Value, _manager.Session.GetDiagnostic(kv.Key), completed ? "Выполнена" : null);
                    SetTileColor(kv.Value, pal.tileColor);
                    SetTileInteractable(kv.Value, true);
                    SetOutline(kv.Value, false, pal.outlineColor);
                    SetDiagDuration(kv.Value, null, false);
                    SetDiagProgressActive(kv.Value, false);
                }
                foreach (var kv in _ovals)
                {
                    // Отремонтированные поломки остаются на корабле и окрашены repairingColor,
                    // диагностированные — detectedColor, прочие — tileColor.
                    bool repairedB = _repairedBreakdowns.Contains(kv.Key);
                    bool fixedB = _fixedBreakdowns.Contains(kv.Key);
                    Color fill = repairedB ? pal.repairingColor : (fixedB ? pal.detectedColor : pal.tileColor);
                    SetOvalVisible(kv.Value, true, fill, false, pal.outlineColor);
                }
                ShowRepairPanel(true);
                ApplyDoneRepairButtons();
                break;
        }
    }

    private bool BreakdownHasFaultConsequence(string breakdownId, RepairFault fault)
    {
        var b = _manager.Session.GetBreakdown(breakdownId);
        // Учитываем только поломки, вошедшие в текущую сессию, и выбранные для них неисправности.
        return b != null
            && _manager.Session.IsInSession(breakdownId)
            && _manager.Session.BreakdownHasFault(breakdownId, fault.id);
    }

    /// <summary>Выявляет ли указанная диагностика данную поломку.</summary>
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

    private static void SetDiagTileLabel(TileView view, RepairDiagnostic d, string overrideText = null)
    {
        if (view == null || view.label == null) return;
        view.label.text = string.IsNullOrEmpty(overrideText) ? (d != null ? d.displayName : "") : overrideText;
    }

    private static void SetDiagDuration(TileView view, string text, bool active)
    {
        if (view.duration == null) return;
        view.duration.text = text ?? "";
        bool show = active && !string.IsNullOrEmpty(text);
        view.duration.gameObject.SetActive(show);
        if (view.durationPlate != null) view.durationPlate.gameObject.SetActive(show);
    }

    /// <summary>
    /// Цвет текста, читаемый на подложке заданного цвета: тёмный на светлой подложке,
    /// светлый — на тёмной.
    /// </summary>
    private static Color ReadableTextColor(Color bg)
    {
        float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
        return lum > 0.5f ? new Color(0.08f, 0.10f, 0.16f, 1f) : new Color(0.92f, 0.95f, 1f, 1f);
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
            // Поломки скрыты до клика по кораблю (показываются при открытии окна ремонта).
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
