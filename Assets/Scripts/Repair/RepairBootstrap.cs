using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Автозапуск мини-игры «Ремонт корабля» при старте проекта.
/// Создаёт менеджер в рантайме — без каких-либо ссылок на скрипты в сцене,
/// поэтому запуск не зависит от GUID и кэша импорта (надёжно работает всегда).
/// </summary>
public static class RepairBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Object.FindFirstObjectByType<RepairGameManager>() != null)
            return;

        var data = Resources.Load<RepairSessionData>("RepairSessionData");
        if (data == null)
        {
            Debug.LogError("[RepairBootstrap] Ассет RepairSessionData не найден в Assets/Resources/RepairSessionData.asset");
            return;
        }

        var ship = LoadShipSprite();
        if (ship == null)
        {
            Debug.LogError("[RepairBootstrap] Не удалось загрузить спрайт корабля (Assets/Art/Sprites/Ship.png).");
            return;
        }

        var go = new GameObject("RepairGame");
        var manager = go.AddComponent<RepairGameManager>();
        manager.Initialize(data, ship, 60f);
    }

    private static Sprite LoadShipSprite()
    {
        var sprite = Resources.Load<Sprite>("Ship");
#if UNITY_EDITOR
        if (sprite == null)
            sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprites/Ship.png");
#endif
        return sprite;
    }
}