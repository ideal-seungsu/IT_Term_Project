using UnityEngine;

/// <summary>
/// 자율운행(자동 항해) 컴포넌트 — 드롭인 방식.
/// Player GameObject (Rigidbody + PlayerController 가 붙어 있는) 에 추가하면 끝.
/// Tab 키로 수동 ↔ 자율 토글. 자율주행 시 PlayerController 가 자동으로 비활성화돼 이중 입력 방지.
///
/// 12주차 강의 내용 기반:
///   - Physics.Raycast 9방향 (0°/±15°/±25°/±40°/±55°/±90°) + Debug.DrawRay 시각화
///   - Vector3 합성 (목적지 방향 + 옆걸이)
///   - Vector3.SignedAngle 로 조향 각도 계산
/// 추가 강화:
///   - P-D 조향 댐핑 (회전 관성으로 인한 오버슈팅 방지)
///   - 회피 commitment (1.5s 같은 방향 유지 — 보트 꼬리 swing 방지)
///   - OverlapSphere 측면 블라인드 스팟 검사
///   - 자동 뱃머리(bow) 감지 — 큰 보트의 광선 시작점 보정
///   - 속도 기반 throttle floor — 파도 표류로 뒤로 밀리는 케이스 방지
///   - CRASH 진단 로그 (Editor.log 에 충돌 직전 상태 출력)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class AutoPilot : MonoBehaviour
{
    [Header("Toggle (Tab 키로 수동/자율 전환)")]
    [SerializeField] private bool autoDrive = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Engine — PlayerController 와 동일한 값으로 유지")]
    [SerializeField] private float forwardForce = 5.5f;
    [SerializeField] private float turnTorque = 1.2f;

    [Header("Sensors (Raycast 9 방향)")]
    [Tooltip("전방 감지 광선 최대 길이 (m)")]
    [SerializeField] private float sensorLength = 80f;
    [Tooltip("이 거리부터 부드러운 조향 시작 (m)")]
    [SerializeField] private float previewDistance = 60f;
    [Tooltip("이 거리부터 회피 최대 강도 (m)")]
    [SerializeField] private float avoidDistance = 30f;
    [SerializeField] private float sideSensorAngle = 25f;
    [SerializeField] private float wideSensorAngle = 55f;
    [Tooltip("광선 시작점 높이 (m, 수면 위)")]
    [SerializeField] private float sensorHeightOffset = 1.5f;
    [Tooltip("자동 감지된 뱃머리에서 앞으로 추가로 띄울 거리 (m)")]
    [SerializeField] private float sensorForwardOffset = 1.0f;
    [Tooltip("감지 레이어 (비워두면 Water/UI/IgnoreRaycast 제외 전체 자동 설정)")]
    [SerializeField] private LayerMask sensorMask = 0;

    [Header("Control")]
    [Tooltip("회피 시 옆걸이 강도 (0.5~1.0 권장)")]
    [SerializeField] private float lateralStrength = 0.75f;
    [Tooltip("자율주행 시 회전 토크 배율")]
    [SerializeField] private float autoSteerGain = 1.3f;
    [Tooltip("조향 댐핑 — P-D 제어의 D항. 회전 관성 보정")]
    [SerializeField] private float steerDamping = 0.6f;
    [Tooltip("회피 commitment 지속 시간 (초) — 꼬리 swing 방지")]
    [SerializeField] private float avoidCommitDuration = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool periodicLog = true;
    [SerializeField] private bool crashLog = true;
    [SerializeField] private bool drawGizmos = true;

    // ─── 내부 상태 ───
    private Rigidbody rb;
    private Transform goalTarget;
    private PlayerController playerController;
    private float bowLocalZ;

    // 회피 commitment 추적
    private float lastDangerTime = -10f;
    private float lastAvoidSign = 0f;

    // OverlapSphere 결과 버퍼 (alloc 회피)
    private Collider[] overlapBuf = new Collider[24];

    // 진단 스냅샷
    private float dbg_center, dbg_left, dbg_right, dbg_wLeft, dbg_wRight;
    private float dbg_mLeft, dbg_mRight, dbg_sLeft, dbg_sRight;
    private float dbg_urgency, dbg_avoidSign, dbg_steerAngle, dbg_steer, dbg_throttle;
    private float dbg_lastPeriodicLog = -10f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerController = GetComponent<PlayerController>();

        // 목적지(Goal) 자동 탐색
        var goal = FindObjectOfType<Goal>();
        if (goal != null) goalTarget = goal.transform;

        // 센서 마스크 기본값 자동 설정
        if (sensorMask.value == 0)
        {
            int mask = ~0;
            int water = LayerMask.NameToLayer("Water");
            int ui = LayerMask.NameToLayer("UI");
            if (water >= 0) mask &= ~(1 << water);
            if (ui >= 0) mask &= ~(1 << ui);
            mask &= ~(1 << 2); // Ignore Raycast
            sensorMask = mask;
        }

        // 뱃머리 자동 감지 (모든 자식 콜라이더의 가장 앞쪽 끝)
        bowLocalZ = ComputeBowLocalZ();

        Debug.Log($"[AutoPilot Init] sensorLength={sensorLength} previewDist={previewDistance} avoidDist={avoidDistance} " +
                  $"lateralStr={lateralStrength} steerGain={autoSteerGain} damping={steerDamping} bowLocalZ={bowLocalZ:F1}");

        SyncPlayerControllerState();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            autoDrive = !autoDrive;
            Debug.Log("[AutoPilot] 자율주행 " + (autoDrive ? "ON" : "OFF"));
            SyncPlayerControllerState();
        }
    }

    /// <summary>자율주행 ON 시 PlayerController 비활성화 (이중 입력/이중 가속 방지)</summary>
    void SyncPlayerControllerState()
    {
        if (playerController != null) playerController.enabled = !autoDrive;
    }

    void FixedUpdate()
    {
        if (!autoDrive) return;
        if (GameManager.Instance == null) return;
        if (GameManager.Instance.CurrentState != GameManager.GameState.Playing) return;

        AutoControl(out float h, out float v);

        // 추진력 (PlayerController 와 동일 패턴)
        if (Mathf.Abs(v) > 0.01f)
        {
            var f = transform.forward * v * forwardForce;
            rb.AddForce(f, ForceMode.Acceleration);
        }

        // 회전 (자율주행 전용 gain 적용)
        if (Mathf.Abs(h) > 0.01f)
        {
            rb.AddTorque(transform.up * h * turnTorque * autoSteerGain, ForceMode.Acceleration);
        }
    }

    // ─────────────── 자율주행 핵심 로직 ───────────────
    void AutoControl(out float steer, out float throttle)
    {
        // 9방향 광선
        float center = SensorClearDistance(0f);
        float l15    = SensorClearDistance(-15f);
        float r15    = SensorClearDistance(15f);
        float left   = SensorClearDistance(-sideSensorAngle);
        float right  = SensorClearDistance(sideSensorAngle);
        float mLeft  = SensorClearDistance(-40f);
        float mRight = SensorClearDistance(40f);
        float wLeft  = SensorClearDistance(-wideSensorAngle);
        float wRight = SensorClearDistance(wideSensorAngle);
        float sLeft  = SensorClearDistance(-90f);
        float sRight = SensorClearDistance(90f);

        // urgencyThrottle: 진로(±25°) 안 광선만. throttle 결정용.
        // urgency: 옆 ±40° 까지 포함. lateral push 결정용.
        float pathPure = Mathf.Min(center, Mathf.Min(Mathf.Min(l15, r15), Mathf.Min(left, right)));
        float pathWide = Mathf.Min(pathPure, Mathf.Min(mLeft, mRight));
        float urgencyThrottle = 1f - Mathf.Clamp01((pathPure - avoidDistance) / Mathf.Max(0.1f, previewDistance - avoidDistance));
        float urgency         = 1f - Mathf.Clamp01((pathWide - avoidDistance) / Mathf.Max(0.1f, previewDistance - avoidDistance));

        // 좌·우 그룹 최소 여유 비교 → 회피 방향
        float leftClear  = Mathf.Min(l15, Mathf.Min(left, Mathf.Min(mLeft, wLeft)));
        float rightClear = Mathf.Min(r15, Mathf.Min(right, Mathf.Min(mRight, wRight)));
        float avoidSign;
        if (Mathf.Abs(leftClear - rightClear) < 1f)
        {
            // 양쪽 비슷
            if (leftClear > 35f)
            {
                // 둘 다 멀리 — 목적지 방향으로 우회
                if (goalTarget != null)
                {
                    Vector3 toGoalDir = goalTarget.position - transform.position;
                    toGoalDir.y = 0f;
                    float goalAng = Vector3.SignedAngle(transform.forward, toGoalDir, Vector3.up);
                    avoidSign = goalAng >= 0f ? 1f : -1f;
                }
                else avoidSign = 1f;
            }
            else
            {
                // 양쪽 다 가까움 (bilateral) → 가운데로 직진 (lateral X)
                avoidSign = 0f;
            }
        }
        else
            avoidSign = (rightClear > leftClear) ? 1f : -1f;

        // 옆구리(90°) 강제 회피 — 회전 중 보트 옆면이 장애물에 쓸리는 케이스 방지
        const float SIDE_THRESHOLD = 12f;
        bool sideL = sLeft  < SIDE_THRESHOLD;
        bool sideR = sRight < SIDE_THRESHOLD;
        if (sideL && sideR) { avoidSign = 0f; urgency = Mathf.Max(urgency, 0.4f); }
        else if (sideL)      { avoidSign = 1f;  urgency = Mathf.Max(urgency, 0.65f); }
        else if (sideR)      { avoidSign = -1f; urgency = Mathf.Max(urgency, 0.65f); }

        // 후방 사분면(rear-quarter) 블라인드 스팟 검사 — OverlapSphere 로 측면 50~140° 강제 회피
        float boatScanRadius = Mathf.Max(bowLocalZ * 1.5f, 14f);
        Vector3 fwdFlat = FlatForward();
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, boatScanRadius, overlapBuf, sensorMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            var col = overlapBuf[i];
            if (col == null || col.transform.root == transform.root) continue;
            if (!(col.CompareTag("Obstacle") || col.CompareTag("Boundary"))) continue;
            Vector3 toObs = col.transform.position - transform.position;
            toObs.y = 0f;
            float distObs = toObs.magnitude;
            if (distObs < 0.1f) continue;
            float signedAng = Vector3.SignedAngle(fwdFlat, toObs.normalized, Vector3.up);
            float absAng = Mathf.Abs(signedAng);
            if (absAng < 50f || absAng > 140f) continue;
            avoidSign = signedAng > 0 ? -1f : 1f;
            urgency = Mathf.Max(urgency, 0.7f);
        }

        // 회피 commitment — 한 번 시작되면 일정 시간 같은 방향 유지 (꼬리 swing 방지)
        if (urgency >= 0.3f)
        {
            lastDangerTime = Time.time;
            lastAvoidSign = avoidSign;
        }
        float sinceDanger = Time.time - lastDangerTime;
        if (sinceDanger < avoidCommitDuration && lastAvoidSign != 0f)
        {
            // 한쪽이라도 감지되면 current avoidSign 우선 (반대편 새 장애물 대응)
            bool currentHasDirection = !float.IsInfinity(leftClear) || !float.IsInfinity(rightClear);
            if (!currentHasDirection) avoidSign = lastAvoidSign;
            // 잔류 lateral 페이드아웃 (0.4 → 0)
            float commitUrg = Mathf.Clamp01(1f - sinceDanger / avoidCommitDuration) * 0.4f;
            if (urgency < commitUrg) urgency = commitUrg;
        }

        // 1) 기본 방향 = 목적지 방향
        Vector3 desiredDir = transform.forward;
        if (goalTarget != null)
        {
            Vector3 toGoal = goalTarget.position - transform.position;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude > 0.01f) desiredDir = toGoal.normalized;
        }

        // 2) 회피 옆걸이 추가
        if (urgency > 0f)
        {
            Vector3 lateral = transform.right * (avoidSign * urgency * lateralStrength);
            desiredDir = (desiredDir + lateral).normalized;
        }

        // 3) P-D 조향 (각도 오차 - 각속도 댐핑)
        float steerAngle = Vector3.SignedAngle(transform.forward, desiredDir, Vector3.up);
        float omegaDeg   = Vector3.Dot(rb.angularVelocity, transform.up) * Mathf.Rad2Deg;
        steer = Mathf.Clamp((steerAngle - steerDamping * omegaDeg) / 60f, -1f, 1f);

        // 4) Throttle 단계별 — 진로 안 광선 기준 (±25°)
        if      (urgencyThrottle >= 0.85f) throttle = 0.20f;
        else if (urgencyThrottle >= 0.60f) throttle = 0.40f;
        else if (urgencyThrottle >= 0.30f) throttle = 0.65f;
        else if (urgencyThrottle >= 0.05f) throttle = 0.85f;
        else                                throttle = 1.00f;

        // 5) 속도 기반 floor — 파도/장애물로 느려지면 강제 가속 (stall/후진 방지)
        float speedFwd = Vector3.Dot(rb.velocity, fwdFlat);
        if      (speedFwd < 0.5f) throttle = Mathf.Max(throttle, 0.90f);
        else if (speedFwd < 2f)   throttle = Mathf.Max(throttle, 0.65f);
        else if (speedFwd < 4f)   throttle = Mathf.Max(throttle, 0.40f);

        // 진단 스냅샷 + 주기 로그
        dbg_center = center; dbg_left = left; dbg_right = right; dbg_wLeft = wLeft; dbg_wRight = wRight;
        dbg_mLeft = mLeft; dbg_mRight = mRight; dbg_sLeft = sLeft; dbg_sRight = sRight;
        dbg_urgency = urgency; dbg_avoidSign = avoidSign;
        dbg_steerAngle = steerAngle; dbg_steer = steer; dbg_throttle = throttle;
        if (periodicLog && Time.time - dbg_lastPeriodicLog > 0.5f)
        {
            dbg_lastPeriodicLog = Time.time;
            float spd = rb != null ? rb.velocity.magnitude : 0f;
            Debug.Log($"[AutoPilot] spd={spd:F1} | urg={urgency:F2} | C={center:F1} L25={left:F1} R25={right:F1} L40={mLeft:F1} R40={mRight:F1} L55={wLeft:F1} R55={wRight:F1} L90={sLeft:F1} R90={sRight:F1} | ang={steerAngle:F0}° steer={steer:F2} thr={throttle:F2} | avoid={(int)avoidSign}");
        }
    }

    /// <summary>한 방향(angleDeg)으로 Raycast 쏘고 막힌 거리(또는 Infinity) 반환. Debug.DrawRay 로 시각화.</summary>
    float SensorClearDistance(float angleDeg)
    {
        Vector3 dir = Quaternion.AngleAxis(angleDeg, Vector3.up) * FlatForward();
        Vector3 origin = GetSensorOrigin();
        float clear = float.PositiveInfinity;
        Color rayColor = Color.green;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, sensorLength, sensorMask, QueryTriggerInteraction.Collide))
        {
            bool isSelf = hit.collider.transform.root == transform.root;
            if (!isSelf && (hit.collider.CompareTag("Obstacle") || hit.collider.CompareTag("Boundary")))
            {
                clear = hit.distance;
                rayColor = Color.red;
            }
        }
        float drawDist = float.IsInfinity(clear) ? sensorLength : clear;
        Debug.DrawRay(origin, dir * drawDist, rayColor);
        return clear;
    }

    /// <summary>boat pitch/roll 무시한 수평 전방 벡터 — 광선·옆걸이가 위로 솟구치지 않게.</summary>
    Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.001f) return Vector3.forward;
        return f.normalized;
    }

    /// <summary>광선 시작점 — 뱃머리(bowLocalZ) + sensorForwardOffset(앞), sensorHeightOffset(위), 수평 투영.</summary>
    Vector3 GetSensorOrigin()
    {
        float bowZ = bowLocalZ > 0.01f ? bowLocalZ : ComputeBowLocalZ();
        return transform.position + FlatForward() * (bowZ + sensorForwardOffset) + Vector3.up * sensorHeightOffset;
    }

    /// <summary>자식 콜라이더의 8 corner 를 보트 local 공간으로 변환해 가장 큰 +Z 를 뱃머리로 채택.</summary>
    float ComputeBowLocalZ()
    {
        float maxZ = 0f;
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (col == null || col.isTrigger) continue;
            Bounds wb = col.bounds;
            Vector3 mn = wb.min, mx = wb.max;
            Vector3[] corners = {
                new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z),
                new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mn.y, mx.z),
                new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mn.y, mx.z),
                new Vector3(mn.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mx.z),
            };
            foreach (var c in corners)
            {
                float lz = transform.InverseTransformPoint(c).z;
                if (lz > maxZ) maxZ = lz;
            }
        }
        return maxZ;
    }

    /// <summary>Scene 뷰 Gizmo — Player 선택 시 광선 시작점 + 9방향 광선 시각화.</summary>
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Vector3 origin = GetSensorOrigin();
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(origin, 0.4f);

        Gizmos.color = new Color(1f, 1f, 0f, 0.7f);
        float[] angles = { 0f, -15f, 15f, -sideSensorAngle, sideSensorAngle, -40f, 40f, -wideSensorAngle, wideSensorAngle, -90f, 90f };
        foreach (var a in angles)
        {
            Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * FlatForward();
            Gizmos.DrawLine(origin, origin + dir * sensorLength);
        }
    }

    /// <summary>화면 좌상단 자율주행 상태 표시.</summary>
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        var style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
        GUI.color = autoDrive ? Color.cyan : Color.white;
        GUI.Label(new Rect(10, 10, 260, 30), autoDrive ? " AUTO-PILOT: ON  (Tab)" : " AUTO-PILOT: OFF (Tab)", style);
    }

    // ─── 진단 (Obstacle/Boundary 충돌 시 자율주행 직전 상태 로그) ───
    void OnCollisionEnter(Collision c) { if (c?.gameObject != null) LogCrash(c.gameObject.CompareTag("Boundary") ? "Boundary" : (c.gameObject.CompareTag("Obstacle") ? "Obstacle" : null), c.gameObject); }
    void OnTriggerEnter(Collider c) { if (c != null) LogCrash(c.CompareTag("Obstacle") ? "Obstacle" : null, c.gameObject); }

    void LogCrash(string what, GameObject other)
    {
        if (!crashLog || !autoDrive || string.IsNullOrEmpty(what)) return;
        Vector3 toHit = other != null ? other.transform.position - transform.position : Vector3.zero;
        float dist = toHit.magnitude;
        float angle = Vector3.SignedAngle(transform.forward, toHit, Vector3.up);
        float spd = rb != null ? rb.velocity.magnitude : 0f;
        Debug.LogWarning(
            $"[AutoPilot CRASH] hit={what}({(other != null ? other.name : "?")}) | dist={dist:F1}m angle={angle:F0}° | spd={spd:F1} | " +
            $"last urg={dbg_urgency:F2} steerAng={dbg_steerAngle:F0}° steer={dbg_steer:F2} thr={dbg_throttle:F2} avoidSign={(int)dbg_avoidSign} | " +
            $"sensors C={dbg_center:F1} L25={dbg_left:F1} R25={dbg_right:F1} L40={dbg_mLeft:F1} R40={dbg_mRight:F1} L55={dbg_wLeft:F1} R55={dbg_wRight:F1} L90={dbg_sLeft:F1} R90={dbg_sRight:F1}");
    }
}
