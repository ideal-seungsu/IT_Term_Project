using UnityEngine;

public class Obstacle : MonoBehaviour
{
    // 장애물 타입을 인스펙터에서 선택할 수 있도록 지정
    public enum ObstacleType { Static, Floating, Patrol, Homing }
    public ObstacleType myType;

    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float floatAmplitude = 0.5f; // 출렁이는 높이
    public float floatFrequency = 1f;   // 출렁이는 속도
    public float patrolDistance = 50f;  // 순찰 이동 반경

    private Transform playerTransform;
    private Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
        // Homing 타입을 위해 플레이어 위치를 찾음
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    void Update()
    {
        // 타입에 따른 개별 움직임 로직
        switch (myType)
        {
            case ObstacleType.Floating:
                // 해수면에 떠서 위아래로 출렁이는 로직 (수학 함수 활용)
                float newY = startPos.y + Mathf.Sin(Time.time * floatFrequency) * floatAmplitude;
                transform.position = new Vector3(transform.position.x, newY, transform.position.z);
                break;

            case ObstacleType.Homing:
                // 어뢰나 토네이도처럼 플레이어를 향해 서서히 다가가는 유도 로직
                if (playerTransform != null)
                {
                    Vector3 direction = (playerTransform.position - transform.position).normalized;
                    direction.y = 0; // z, x 축으로만 이동하게 설정
                    transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
                }
                break;

            case ObstacleType.Patrol:
                // 5f로 고정되어 있던 부분을 patrolDistance 변수로 교체
                float patrolX = startPos.x + Mathf.Sin(Time.time * moveSpeed) * patrolDistance;
                transform.position = new Vector3(patrolX, transform.position.y, transform.position.z);
                break;

            case ObstacleType.Static:
                // 암초(Rock), 에버그린호 등은 움직이지 않음
                break;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        var rootGo = collision.rigidbody != null ? collision.rigidbody.gameObject : collision.gameObject;

        if (rootGo.CompareTag("Player") && GameManager.Instance != null)
        {
            GameManager.Instance.RegisterCollision();
        }
    }
}