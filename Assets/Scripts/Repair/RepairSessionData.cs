using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Внешне настраиваемое описание сессии ремонта: неисправности, поломки,
/// диагностики и связи между ними (граф), а также палитра цветов 1-4.
/// Настраивается в Inspector (файл-ассет в Assets/Settings), без правки кода.
/// </summary>
[CreateAssetMenu(fileName = "RepairSessionData", menuName = "Space Sheep/Repair Session Data")]
public class RepairSessionData : ScriptableObject
{
    [Header("Палитра")]
    [Tooltip("Цвет 1 - активация/подтверждение (плитки неисправностей в шаге 3, кнопка запуска, диагностика в процессе).")]
    public Color color1 = new Color(0f, 0.81f, 1f);
    [Tooltip("Цвет 2 - связь/кандидат (последствия поломки, диагностики-кандидаты, поломки диагностики на корабле).")]
    public Color color2 = new Color(1f, 0.62f, 0.11f);
    [Tooltip("Цвет 3 - выбор/обводка (обводка выбранной поломки и выбранной диагностики).")]
    public Color color3 = new Color(0.49f, 1f, 0.42f);
    [Tooltip("Цвет 4 - прогресс и символ диагностики (прогресс-бары, ромб на границе поломок).")]
    public Color color4 = new Color(1f, 0.83f, 0.14f);

    [Header("Контент сессии")]
    public RepairFault[] faults;
    public RepairBreakdown[] breakdowns;
    public RepairDiagnostic[] diagnostics;

    private Dictionary<string, RepairFault> _faultById;
    private Dictionary<string, RepairBreakdown> _breakdownById;
    private Dictionary<string, RepairDiagnostic> _diagnosticById;

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

    public int TotalBreakdownCount => breakdowns != null ? breakdowns.Length : 0;
}

[System.Serializable]
public class RepairFault
{
    [Tooltip("Уникальный текстовый идентификатор (например \"Н1\").")]
    public string id;
    [Tooltip("Отображаемое название на панели неисправностей.")]
    public string displayName;
    [Tooltip("ID поломок, которые возможны при этой неисправности (овалы на корабле в шаге 1).")]
    public string[] breakdownIds;
}

[System.Serializable]
public class RepairBreakdown
{
    [Tooltip("Уникальный текстовый идентификатор (например \"П3\").")]
    public string id;
    [Tooltip("Отображаемое название (метка на овале корабля).")]
    public string displayName;
    [Tooltip("Позиция овала на спрайте корабля в нормализованных координатах 0..1 (0,0 - низ-лево, 1,1 - верх-право).")]
    public Vector2 shipPosition = new Vector2(0.5f, 0.5f);
    [Tooltip("Размер овала в нормализованных единицах относительно размера корабля.")]
    public Vector2 ovalSize = new Vector2(0.06f, 0.04f);
    [Tooltip("ID двух неисправностей, которые влечет эта поломка (подсвечиваются цветом 2 на панели неисправностей).")]
    public string[] consequenceFaultIds;
    [Tooltip("ID диагностик, которые могут выявить эту поломку. Только они остаются активными после выбора поломки.")]
    public string[] diagnosticIds;
}

[System.Serializable]
public class RepairDiagnostic
{
    [Tooltip("Уникальный текстовый идентификатор (например \"Д2\").")]
    public string id;
    [Tooltip("Отображаемое название на панели диагностики (например \"Диагностика В\").")]
    public string displayName;
    [Tooltip("Длительность диагностики в ЧЧ:ММ - показывается на плитке (например 2 => \"00:02\").")]
    public float durationInMinutes;
    [Tooltip("Фактическое время заполнения прогресс-бара в реальных секундах.")]
    public float durationSeconds;
    [Tooltip("ID поломок, которые находит эта диагностика и которые устраняются по завершении.")]
    public string[] breakdownIds;
}