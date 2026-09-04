using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Внешне настраиваемое описание сессии ремонта: неисправности, поломки,
/// диагностики и связи между ними (граф), а также палитра из 3 цветов.
/// Настраивается в Inspector (файл-ассет в Assets/Settings), без правки кода.
/// </summary>
[CreateAssetMenu(fileName = "RepairSessionData", menuName = "Space Sheep/Repair Session Data")]
public class RepairSessionData : ScriptableObject
{
    [Header("Палитра")]
    [Tooltip("Цвет всех плиток/овалов: неисправности, поломки на корабле, диагностики (единая заливка).")]
    public Color tileColor = new Color(1f, 0.6f, 0.1f);
    [Tooltip("Цвет обводки выбранной плитки/овала.")]
    public Color outlineColor = new Color(1f, 1f, 1f);
    [Tooltip("Цвет прогресс-бара диагностики и ромбов-маркеров на диагностируемых поломках.")]
    public Color progressColor = new Color(0.6f, 0.25f, 0.95f);

    [Header("Балансные значения для ремонтируемого корабля")]
    [Tooltip("Максимальное количество поломок на корабле (n). Из общего списка ремонтируемых поломок случайно выбирается не более n; если поломок меньше — берутся все.")]
    public int maxBreakdownsPerSession = 3;
    [Tooltip("Количество неисправностей, связанных с каждой поломкой (m). Для каждой выбранной поломки из её consequenceFaultIds случайно берётся не более m; если неисправностей меньше — берутся все.")]
    public int faultsPerBreakdown = 2;

    [Header("Контент сессии")]
    [Tooltip("Размер овалов поломок на корабле в нормализованных единицах относительно размера корабля. Единый для всех поломок (все овалы одинакового размера).")]
    public Vector2 ovalSize = new Vector2(0.07f, 0.045f);
    public RepairFault[] faults;
    public RepairBreakdown[] breakdowns;
    public RepairDiagnostic[] diagnostics;

    private Dictionary<string, RepairFault> _faultById;
    private Dictionary<string, RepairBreakdown> _breakdownById;
    private Dictionary<string, RepairDiagnostic> _diagnosticById;

    // Текущая случайно выбранная сессия: n поломок на корабле и для каждой — m неисправностей.
    private RepairBreakdown[] _sessionBreakdowns = new RepairBreakdown[0];
    private readonly Dictionary<string, string[]> _faultsForBreakdown = new Dictionary<string, string[]>();

    public void BuildLookups()
    {
        _faultById = new Dictionary<string, RepairFault>();
        _breakdownById = new Dictionary<string, RepairBreakdown>();
        _diagnosticById = new Dictionary<string, RepairDiagnostic>();

        if (faults != null)
            foreach (var f in faults)
                if (f != null && !string.IsNullOrEmpty(f.id))
                    _faultById[f.id] = f;

        if (breakdowns != null)
            foreach (var b in breakdowns)
                if (b != null && !string.IsNullOrEmpty(b.id))
                    _breakdownById[b.id] = b;

        if (diagnostics != null)
            foreach (var d in diagnostics)
                if (d != null && !string.IsNullOrEmpty(d.id))
                    _diagnosticById[d.id] = d;
    }

    public RepairFault GetFault(string id) => _faultById.TryGetValue(id, out var f) ? f : null;
    public RepairBreakdown GetBreakdown(string id) => _breakdownById.TryGetValue(id, out var b) ? b : null;
    public RepairDiagnostic GetDiagnostic(string id) => _diagnosticById.TryGetValue(id, out var d) ? d : null;

    /// <summary>Поломки, отобранные для текущей сессии (не более n).</summary>
    public RepairBreakdown[] SessionBreakdowns => _sessionBreakdowns ?? new RepairBreakdown[0];

    /// <summary>Входит ли поломка в текущую сессию.</summary>
    public bool IsInSession(string breakdownId)
    {
        foreach (var b in SessionBreakdowns)
            if (b != null && b.id == breakdownId)
                return true;
        return false;
    }

    /// <summary>Связана ли указанная неисправность с поломкой в рамках текущей сессии (учитывает выбранные m неисправностей).</summary>
    public bool BreakdownHasFault(string breakdownId, string faultId)
    {
        if (!_faultsForBreakdown.TryGetValue(breakdownId, out var faults))
            return false;
        return faults != null && System.Array.IndexOf(faults, faultId) >= 0;
    }

    /// <summary>Входит ли неисправность в текущую сессию (связана хотя бы с одной выбранной поломкой).</summary>
    public bool IsFaultInSession(string faultId)
    {
        if (string.IsNullOrEmpty(faultId)) return false;
        foreach (var kv in _faultsForBreakdown)
            if (kv.Value != null)
                for (int i = 0; i < kv.Value.Length; i++)
                    if (kv.Value[i] == faultId)
                        return true;
        return false;
    }

    /// <summary>
    /// Случайный выбор балансного набора: n поломок из общего списка (все, если меньше)
    /// и для каждой — m неисправностей из её consequenceFaultIds (все, если меньше).
    /// Вызывается при старте сессии и при каждом перезапуске.
    /// </summary>
    public void RollSession()
    {
        int n = Mathf.Max(1, maxBreakdownsPerSession);
        int m = Mathf.Max(1, faultsPerBreakdown);

        // Кандидаты — поломки, которые можно отремонтировать (есть выявляющая их диагностика).
        var pool = new List<RepairBreakdown>();
        if (breakdowns != null)
            foreach (var b in breakdowns)
                if (b != null && !string.IsNullOrEmpty(b.id) && IsRepairable(b))
                    pool.Add(b);
        // Если таких нет — берём все поломки, чтобы игра оставалась проходимой.
        if (pool.Count == 0 && breakdowns != null)
            foreach (var b in breakdowns)
                if (b != null && !string.IsNullOrEmpty(b.id))
                    pool.Add(b);

        var chosen = new List<RepairBreakdown>();
        while (pool.Count > 0 && chosen.Count < n)
        {
            int idx = Random.Range(0, pool.Count);
            chosen.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        _sessionBreakdowns = chosen.ToArray();

        _faultsForBreakdown.Clear();
        foreach (var b in _sessionBreakdowns)
        {
            var candidate = new List<string>();
            if (b.consequenceFaultIds != null)
                foreach (var f in b.consequenceFaultIds)
                    if (!string.IsNullOrEmpty(f) && !candidate.Contains(f))
                        candidate.Add(f);

            var picked = new List<string>();
            while (candidate.Count > 0 && picked.Count < m)
            {
                int idx = Random.Range(0, candidate.Count);
                picked.Add(candidate[idx]);
                candidate.RemoveAt(idx);
            }
            _faultsForBreakdown[b.id] = picked.ToArray();
        }
    }

    /// <summary>Есть ли хотя бы одна диагностика, выявляющая эту поломку.</summary>
    private bool IsRepairable(RepairBreakdown b)
    {
        if (diagnostics == null) return false;
        foreach (var d in diagnostics)
            if (d != null && d.breakdownIds != null)
                for (int i = 0; i < d.breakdownIds.Length; i++)
                    if (d.breakdownIds[i] == b.id)
                        return true;
        return false;
    }

    public int TotalBreakdownCount => SessionBreakdowns.Length;
}

[System.Serializable]
public class RepairFault
{
    [Tooltip("Уникальный текстовый идентификатор (например \"Н1\").")]
    public string id;
    [Tooltip("Отображаемое название на панели неисправностей.")]
    public string displayName;
}

[System.Serializable]
public class RepairBreakdown
{
    [Tooltip("Уникальный текстовый идентификатор (например \"П3\").")]
    public string id;
    [Tooltip("Отображаемое название (метка на овале корабля).")]
    public string displayName;
    [Tooltip("Позиция овала (место локализации) на спрайте корабля в нормализованных координатах 0..1 (0,0 - низ-лево, 1,1 - верх-право).")]
    public Vector2 shipPosition = new Vector2(0.5f, 0.5f);
    [Tooltip("ID неисправностей, которые может повлечь эта поломка. По ним выводятся овалы на корабле при выборе неисправности.")]
    public string[] consequenceFaultIds;
}

[System.Serializable]
public class RepairDiagnostic
{
    [Tooltip("Уникальный текстовый идентификатор (например \"Д2\").")]
    public string id;
    [Tooltip("Отображаемое название на панели диагностики (например \"Диагностика В\").")]
    public string displayName;
    [Tooltip("Время исполнения в секундах - столько длится выполнение; на плитке показывается «ММ:СС» (например 5 => \"00:05\").")]
    public float duration;
    [Tooltip("ID поломок, которые находит эта диагностика и которые устраняются по завершении.")]
    public string[] breakdownIds;
}