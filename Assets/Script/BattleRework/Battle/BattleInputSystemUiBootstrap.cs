using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleScene의 EventSystem을 New Input System UI 모듈로 통일합니다.
/// 레거시 UI 코드가 StandaloneInputModule을 생성하더라도 씬 초기화 시 제거합니다.
/// </summary>
public static class BattleInputSystemUiBootstrap
{
    private static bool sceneHookInstalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sceneHookInstalled = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (sceneHookInstalled)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneHookInstalled = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureCurrentScene()
    {
        EnsureEventSystem();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        EnsureEventSystem();
    }

    public static EventSystem EnsureEventSystem()
    {
        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject host = new("BattleEventSystem");
            Object.DontDestroyOnLoad(host);
            eventSystem = host.AddComponent<EventSystem>();
        }

        StandaloneInputModule[] legacyModules = eventSystem.GetComponents<StandaloneInputModule>();
        for (int i = 0; i < legacyModules.Length; i++)
        {
            if (legacyModules[i] != null)
                Object.Destroy(legacyModules[i]);
        }

        InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (module == null)
        {
            module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        return eventSystem;
    }
}
