using UnityEngine;

public class ProjectileTester : MonoBehaviour
{
    public ProjectilePooler pool;
    public ProjectileSO projectileSO;
    public float moveSpeed = 5f;

    [Header("타겟 탐색 레이어")]
    public LayerMask targetLayer;

    void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        transform.position += new Vector3(h, v, 0f) * moveSpeed * Time.deltaTime;

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouse.z = 0f;

            Vector2 dir = mouse - transform.position;

            // 🎯 마우스 위치에 오브젝트 있는지 확인
            Collider2D hit = Physics2D.OverlapPoint(mouse, targetLayer);

            Transform target = null;

            if (hit != null)
            {
                target = hit.transform;
            }

            Debug.Log("Clicked at: " + mouse);
            if (hit != null)
                Debug.Log("Hit: " + hit.name);

            var p = pool.Get();
            p.transform.position = transform.position;

            p.Setup(projectileSO, dir, pool, target, mouse);
        }
    }
}