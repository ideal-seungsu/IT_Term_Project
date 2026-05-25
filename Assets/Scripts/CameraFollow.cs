using UnityEngine;

/// <summary>
/// 플레이어를 따라가는 3인칭 카메라.
/// 보트의 Yaw(좌우 회전)만 따라가서, 파도로 인한 Pitch/Roll·Y 흔들림이 카메라로 전달되지 않게 한다.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 18f, -25f);
    [SerializeField] private float smoothSpeed = 4f;
    [SerializeField] private float verticalSmoothSpeed = 1.2f; // 위아래는 더 느리게 → 파도 흔들림 흡수
    [SerializeField] private float lookAhead = 30f;

    void LateUpdate()
    {
        if (target == null) return;

        // 보트의 yaw 만 추출 → pitch/roll 흔들림 무시
        float yaw = target.eulerAngles.y;
        Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);

        Vector3 desiredPosition = target.position + yawOnly * offset;
        var cur = transform.position;
        transform.position = new Vector3(
            Mathf.Lerp(cur.x, desiredPosition.x, smoothSpeed * Time.deltaTime),
            Mathf.Lerp(cur.y, desiredPosition.y, verticalSmoothSpeed * Time.deltaTime),
            Mathf.Lerp(cur.z, desiredPosition.z, smoothSpeed * Time.deltaTime)
        );

        // 보트 앞쪽 (yaw 방향 기준) 을 바라봄 → 항로 시야 확보
        Vector3 forwardOnly = yawOnly * Vector3.forward;
        Vector3 lookAt = target.position + forwardOnly * lookAhead + Vector3.up * 2f;
        transform.LookAt(lookAt);
    }
}
