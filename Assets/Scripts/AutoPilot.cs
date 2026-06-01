using UnityEngine;

// 자율주행 컴포넌트
// Player에 붙이고 Tab키로 수동/자율 전환
// 자율주행 켜질때 PlayerController 꺼주는 식으로 이중입력 방지함
// 12주차 강의 Raycast 내용 활용
[RequireComponent(typeof(Rigidbody))]
public class AutoPilot : MonoBehaviour
{
    [Header("Toggle (Tab키로 수동/자율 전환)")]
    [SerializeField] private bool autoDrive = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Engine - PlayerController랑 같은 값으로 맞추기")]
    [SerializeField] private float forwardForce = 5.5f;
    [SerializeField] private float turnTorque = 1.2f;

    [Header("Sensors")]
    [SerializeField] private float sensorLength = 80f;       // 광선 최대 길이
    [SerializeField] private float previewDistance = 60f;    // 이 거리부터 조향 시작
    [SerializeField] private float avoidDistance = 30f;      // 이 거리면 회피 최대
    [SerializeField] private float sideSensorAngle = 25f;
    [SerializeField] private float wideSensorAngle = 55f;
    [SerializeField] private float sensorHeightOffset = 1.5f;
    [SerializeField] private float sensorForwardOffset = 1.0f;
    [SerializeField] private LayerMask sensorMask = 0;       // 비워두면 자동설정됨

    [Header("Control")]
    [SerializeField] private float lateralStrength = 0.75f;
    [SerializeField] private float autoSteerGain = 1.3f;
    [SerializeField] private float steerDamping = 0.6f;        // 회전 흔들림 잡는 값
    [SerializeField] private float avoidCommitDuration = 1.5f; // 회피 방향 유지시간

    [Header("Debug")]
    [SerializeField] private bool periodicLog = true;
    [SerializeField] private bool crashLog = true;
    [SerializeField] private bool drawGizmos = true;

    private Rigidbody rb;
    private Transform goalTarget;
    private PlayerController playerController;
    private float bowLocalZ;

    // 회피 방향 잠깐 기억해두는 변수들 (꼬리 흔들림 방지용)
    private float lastDangerTime = -10f;
    private float lastAvoidSign = 0f;

    // 매번 new 하면 GC 부담되니까 미리 할당
    private Collider[] overlapBuf = new Collider[24];

    // 디버그 로그용
    private float dbg_center, dbg_left, dbg_right, dbg_wLeft, dbg_wRight;
    private float dbg_mLeft, dbg_mRight, dbg_sLeft, dbg_sRight;
    private float dbg_urgency, dbg_avoidSign, dbg_steerAngle, dbg_steer, dbg_throttle;
    private float dbg_lastPeriodicLog = -10f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerController = GetComponent<PlayerController>();

        // Goal 자동으로 찾기
        var goal = FindObjectOfType<Goal>();
        if (goal != null) goalTarget = goal.transform;

        // 마스크 안 정해놓으면 Water, UI, IgnoreRaycast 빼고 다 잡도록 자동 설정
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

        // 뱃머리 자동으로 찾기 (자식 콜라이더 중 가장 앞쪽 점)
        bowLocalZ = ComputeBowLocalZ();

        Debug.Log($"[AutoPilot] 초기화. bowLocalZ={bowLocalZ:F1}");

        SyncPlayerControllerState();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            autoDrive = !autoDrive;
            Debug.Log("자율주행 " + (autoDrive ? "ON" : "OFF"));
            SyncPlayerControllerState();
        }
    }

    // 자율주행 켜지면 PlayerController 꺼주기 (입력 두번 먹는거 방지)
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

        // 추진력 (PlayerController랑 똑같이)
        if (Mathf.Abs(v) > 0.01f)
        {
            var f = transform.forward * v * forwardForce;
            rb.AddForce(f, ForceMode.Acceleration);
        }

        // 회전
        if (Mathf.Abs(h) > 0.01f)
        {
            rb.AddTorque(transform.up * h * turnTorque * autoSteerGain, ForceMode.Acceleration);
        }
    }

    // 자율주행 메인 로직
    void AutoControl(out float steer, out float throttle)
    {
        // 9방향으로 광선 쏘기
        float center = SensorClearDistance(0f);
        float l15 = SensorClearDistance(-15f);
        float r15 = SensorClearDistance(15f);
        float left = SensorClearDistance(-sideSensorAngle);
        float right = SensorClearDistance(sideSensorAngle);
        float mLeft = SensorClearDistance(-40f);
        float mRight = SensorClearDistance(40f);
        float wLeft = SensorClearDistance(-wideSensorAngle);
        float wRight = SensorClearDistance(wideSensorAngle);
        float sLeft = SensorClearDistance(-90f);
        float sRight = SensorClearDistance(90f);

        // urgency = 위험도 (가까우면 1, 멀면 0)
        // throttle용은 진로 안쪽만, lateral용은 더 넓게 포함
        float pathPure = Mathf.Min(center, Mathf.Min(Mathf.Min(l15, r15), Mathf.Min(left, right)));
        float pathWide = Mathf.Min(pathPure, Mathf.Min(mLeft, mRight));
        float urgencyThrottle = 1f - Mathf.Clamp01((pathPure - avoidDistance) / Mathf.Max(0.1f, previewDistance - avoidDistance));
        float urgency = 1f - Mathf.Clamp01((pathWide - avoidDistance) / Mathf.Max(0.1f, previewDistance - avoidDistance));

        // 어느쪽으로 피할지 결정 (왼쪽 vs 오른쪽 그룹 거리 비교)
        float leftClear = Mathf.Min(l15, Mathf.Min(left, Mathf.Min(mLeft, wLeft)));
        float rightClear = Mathf.Min(r15, Mathf.Min(right, Mathf.Min(mRight, wRight)));
        float avoidSign;
        if (Mathf.Abs(leftClear - rightClear) < 1f)
        {
            // 양쪽 비슷할때
            if (leftClear > 35f)
            {
                // 둘 다 충분히 멀면 목적지 방향으로 우회
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
                // 둘다 가까우면 그냥 직진 (lateral 없음)
                avoidSign = 0f;
            }
        }
        else
            avoidSign = (rightClear > leftClear) ? 1f : -1f;

        // 옆구리(90도) 너무 가까우면 강제로 반대로 피하기
        // 회전 도중에 옆면 박는 경우가 있어서 추가함
        const float SIDE_THRESHOLD = 12f;
        bool sideL = sLeft < SIDE_THRESHOLD;
        bool sideR = sRight < SIDE_THRESHOLD;
        if (sideL && sideR) { avoidSign = 0f; urgency = Mathf.Max(urgency, 0.4f); }
        else if (sideL) { avoidSign = 1f; urgency = Mathf.Max(urgency, 0.65f); }
        else if (sideR) { avoidSign = -1f; urgency = Mathf.Max(urgency, 0.65f); }

        // OverlapSphere로 측면 사각지대(50~140도) 한번 더 검사
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

        // 한번 회피 시작하면 1.5초 동안 같은 방향 유지
        // 안 그러면 좌우로 휙휙 흔들려서 보트 꼬리가 박힘
        if (urgency >= 0.3f)
        {
            lastDangerTime = Time.time;
            lastAvoidSign = avoidSign;
        }
        float sinceDanger = Time.time - lastDangerTime;
        if (sinceDanger < avoidCommitDuration && lastAvoidSign != 0f)
        {
            bool currentHasDirection = !float.IsInfinity(leftClear) || !float.IsInfinity(rightClear);
            if (!currentHasDirection) avoidSign = lastAvoidSign;
            // 점점 약해지게 (0.4 -> 0)
            float commitUrg = Mathf.Clamp01(1f - sinceDanger / avoidCommitDuration) * 0.4f;
            if (urgency < commitUrg) urgency = commitUrg;
        }

        // 1. 일단 목적지 방향이 기본
        Vector3 desiredDir = transform.forward;
        if (goalTarget != null)
        {
            Vector3 toGoal = goalTarget.position - transform.position;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude > 0.01f) desiredDir = toGoal.normalized;
        }

        // 2. 회피해야하면 옆걸이 더해서 방향 살짝 틀기
        if (urgency > 0f)
        {
            Vector3 lateral = transform.right * (avoidSign * urgency * lateralStrength);
            desiredDir = (desiredDir + lateral).normalized;
        }

        // 3. 조향각 계산. 댐핑 빼주는건 회전 관성 보정해서 흔들림 줄이려는거
        float steerAngle = Vector3.SignedAngle(transform.forward, desiredDir, Vector3.up);
        float omegaDeg = Vector3.Dot(rb.angularVelocity, transform.up) * Mathf.Rad2Deg;
        steer = Mathf.Clamp((steerAngle - steerDamping * omegaDeg) / 60f, -1f, 1f);

        // 4. 위험할수록 throttle 줄이기 (앞이 막혔는데 풀악셀이면 박음)
        if (urgencyThrottle >= 0.85f) throttle = 0.20f;
        else if (urgencyThrottle >= 0.60f) throttle = 0.40f;
        else if (urgencyThrottle >= 0.30f) throttle = 0.65f;
        else if (urgencyThrottle >= 0.05f) throttle = 0.85f;
        else throttle = 1.00f;

        // 5. 근데 너무 느려져서 멈추거나 뒤로 가면 강제로 속도 올려주기
        // (위험구역 측풍에 밀려서 후진하는 케이스 있었음)
        float speedFwd = Vector3.Dot(rb.velocity, fwdFlat);
        if (speedFwd < 0.5f) throttle = Mathf.Max(throttle, 0.90f);
        else if (speedFwd < 2f) throttle = Mathf.Max(throttle, 0.65f);
        else if (speedFwd < 4f) throttle = Mathf.Max(throttle, 0.40f);

        // 디버그용 스냅샷
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

    // 한 방향으로 광선 쏴서 막힌 거리 리턴. Scene뷰에서도 보이게 DrawRay 같이
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

    // 보트가 파도에 기울어져도 광선은 수평으로 가게
    Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.001f) return Vector3.forward;
        return f.normalized;
    }

    // 광선 시작 위치 (뱃머리 + 좀 앞 + 살짝 위)
    Vector3 GetSensorOrigin()
    {
        float bowZ = bowLocalZ > 0.01f ? bowLocalZ : ComputeBowLocalZ();
        return transform.position + FlatForward() * (bowZ + sensorForwardOffset) + Vector3.up * sensorHeightOffset;
    }

    // 자식 콜라이더 다 뒤져서 제일 앞 점 = 뱃머리로 잡기
    // 보트 모델마다 크기가 달라서 자동으로 계산하게 함
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

    // 에디터에서 광선 시각화
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

    // 화면 좌상단에 ON/OFF 표시
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        var style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
        GUI.color = autoDrive ? Color.cyan : Color.white;
        GUI.Label(new Rect(10, 10, 260, 30), autoDrive ? " AUTO-PILOT: ON  (Tab)" : " AUTO-PILOT: OFF (Tab)", style);
    }

    // 박았을때 직전 상태 로그로 남기기 (어디서 왜 박았는지 보려고)
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