using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Главный менеджер мини-игры «Ремонт корабля».
/// Владеет общим таймером обратного отсчёта, состоянием открытого окна ремонта,
/// игровым циклом (победа/поражение) и объектом корабля-дока, по которому игрок кликает.
/// </summary>
public class RepairGameManager : MonoBehaviour
{
    [Header("Данные сессии")]
    [SerializeField] private RepairSessionData sessionData;

    [Header("Корабль в доке")]
    [SerializeField] private Sprite shipSprite;
    [SerializeField] private Vector2 dockPosition = Vector2.zero;
    [Tooltip("Масштаб спрайта корабля в мире.")]
    [SerializeField] private float shipScale = 0.6f;

    [Header("Таймер")]
    [SerializeField] private float maxTimeSeconds = 60f;

    public float CurrentTime { get; private set; }
    public float MaxTime => maxTimeSeconds;
    public bool IsPaused { get; private set; }
    public bool IsOver { get; private set; }
    public bool WindowOpen { get; private set; }
    public RepairSessionData Session => sessionData;
    public Collider2D ShipCollider { get; private set; }
    public Transform ShipTransform { get; private set; }

    public event Action<float> OnTimeChanged;
    public event Action OnTimeUp;
    public event Action OnWin;
    public event Action OnLose;

    private RepairWindowController _window;
    private bool _initialized;

    private void Awake()
    {
        // Если менеджер размещён в сцене с заполненными полями — инициализируемся сразу.
        // При создании в рантайме (RepairBootstrap) поля заполнит метод Initialize.
        if (sessionData != null)
            Initialize(sessionData, shipSprite, maxTimeSeconds);
    }

    /// <summary>Программная инициализация мини-игры (вызывается RepairBootstrap).</summary>
    public void Initialize(RepairSessionData data, Sprite ship, float maxSeconds)
    {
        if (_initialized)
            return;
        _initialized = true;

        sessionData = data;
        if (ship != null) shipSprite = ship;
        if (maxSeconds > 0f) maxTimeSeconds = maxSeconds;

        // Овца не нужна для мини-игры: сразу отключаем её, чтобы она не улетала
        // за экран и не мешала обзору дока (даже если данные сессии не загрузятся).
        DisableSheepPlayer();

        if (sessionData == null)
        {
            Debug.LogError("[RepairGameManager] sessionData не назначен. Передайте ассет RepairSessionData.");
            return;
        }

        sessionData.BuildLookups();
        // Случайный балансный набор поломок для корабля (n поломок, m неисправностей на поломку).
        sessionData.RollSession();

        BuildShip();
        CurrentTime = maxTimeSeconds;

        _window = GetComponent<RepairWindowController>();
        if (_window == null)
            _window = gameObject.AddComponent<RepairWindowController>();
        _window.Init(this);
    }

    private void DisableSheepPlayer()
    {
        var sheep = GameObject.Find("SheepPlayer");
        if (sheep == null)
            return;

        var controller = sheep.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        var playerInput = sheep.GetComponent<PlayerInput>();
        if (playerInput != null) playerInput.enabled = false;

        var rigidbody = sheep.GetComponent<Rigidbody2D>();
        if (rigidbody != null) rigidbody.simulated = false;
    }

    private void Start()
    {
        if (sessionData == null) return;
        _window.BuildUserInterface();
        OnTimeChanged?.Invoke(CurrentTime);
    }

    private void Update()
    {
        if (sessionData == null || IsOver)
            return;

        if (!WindowOpen)
        {
            if (IsShipClicked())
            {
                OpenWindow();
                return;
            }

            if (!IsPaused)
            {
                CurrentTime -= Time.deltaTime;
                OnTimeChanged?.Invoke(CurrentTime);
                if (CurrentTime <= 0f)
                {
                    CurrentTime = 0f;
                    Lose();
                }
            }
        }
    }

    private void BuildShip()
    {
        if (shipSprite == null || ShipTransform != null)
            return;

        var go = new GameObject("RepairShip");
        go.transform.position = new Vector3(dockPosition.x, dockPosition.y, 0f);
        go.transform.localScale = new Vector3(shipScale, shipScale, 1f);
        ShipTransform = go.transform;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = shipSprite;
        renderer.sortingOrder = 1;

        var bounds = shipSprite.bounds.size;
        var collider2d = go.AddComponent<BoxCollider2D>();
        // size в локальных единицах; в мире масштабируется transform.localScale.
        collider2d.size = bounds;
        ShipCollider = collider2d;
    }

    private bool IsShipClicked()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            return false;

        var mousePos = Mouse.current.position.ReadValue();
        var cam = Camera.main;
        if (cam == null)
            return false;

        Vector3 world = cam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, -cam.transform.position.z));
        return ShipCollider != null && Physics2D.OverlapPoint(new Vector2(world.x, world.y)) == ShipCollider;
    }

    /// <summary>Открыть окно ремонта и поставить таймер на паузу.</summary>
    public void OpenWindow()
    {
        if (WindowOpen || IsOver || sessionData == null)
            return;

        WindowOpen = true;
        IsPaused = true;
        _window.Open();
    }

    /// <summary>Закрыть окно ремонта и возобновить таймер.</summary>
    public void CloseWindow()
    {
        if (!WindowOpen)
            return;

        WindowOpen = false;
        _window.Close();
        if (!IsOver)
            IsPaused = false;
    }

    /// <summary>Вызывается окном, когда устранены все поломки.</summary>
    public void ReportWin()
    {
        if (IsOver)
            return;
        IsOver = true;
        IsPaused = true;
        OnWin?.Invoke();
    }

    private void Lose()
    {
        if (IsOver)
            return;
        IsOver = true;
        IsPaused = true;
        OnTimeUp?.Invoke();
        OnLose?.Invoke();
    }

    /// <summary>
    /// Перезапуск мини-игры: сброс состояния «здесь и сейчас», без перезагрузки сцены.
    /// (Перезагрузка не подходит: RuntimeInitializeOnLoadMethod(AFTER_SCENE_LOAD) срабатывает
    /// только при загрузке первой сцены, поэтому после LoadScene мини-игра не пересоздавалась бы.)
    /// </summary>
    public void Restart()
    {
        if (_window == null)
            return;

        CurrentTime = maxTimeSeconds;
        IsOver = false;
        IsPaused = false;
        WindowOpen = false;

        _window.ResetGame();
        OnTimeChanged?.Invoke(CurrentTime);
    }
}