using UnityEngine;

public class BattleInfoUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private float bulletTimeScale = 0.1f;

    private float initialFixedDeltaTime;
    private bool opened;

    private void Awake()
    {
        initialFixedDeltaTime = Time.fixedDeltaTime;
        if (panel != null) panel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) Open();
        if (Input.GetKeyUp(KeyCode.Tab)) Close();
    }

    private void Open()
    {
        if (opened) return;
        opened = true;
        if (panel != null) panel.SetActive(true);
        Time.timeScale = bulletTimeScale;
        Time.fixedDeltaTime = initialFixedDeltaTime * Time.timeScale;
    }

    private void Close()
    {
        if (!opened) return;
        opened = false;
        if (panel != null) panel.SetActive(false);
        Time.timeScale = 1f;
        Time.fixedDeltaTime = initialFixedDeltaTime;
    }

    private void OnDisable()
    {
        if (opened) Close();
    }
}
