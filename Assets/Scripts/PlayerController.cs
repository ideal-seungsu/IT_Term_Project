using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Engine")]
    [SerializeField] private float forwardForce = 5.5f;
    [SerializeField] private float turnTorque = 1.2f;
    [SerializeField] private Vector3 enginePosLocal = new Vector3(0f, 0f, 0f);
    [SerializeField] private bool applyAtEngine = false;

    [Header("Booster Settings")]
    [SerializeField] private float boostMultiplier = 2.0f;
    [SerializeField] private KeyCode boostKey = KeyCode.LeftShift;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (GameManager.Instance == null) return;
        if (GameManager.Instance.CurrentState != GameManager.GameState.Playing) return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // 부스터 키(Left Shift)를 누르고 있는지 팩트 체크
        bool isBoosting = Input.GetKey(boostKey);

        // 부스터 상태에 따른 힘 계산
        float currentForce = isBoosting ? forwardForce * boostMultiplier : forwardForce;

        // 추진력 적용
        if (Mathf.Abs(v) > 0.01f)
        {
            var f = transform.forward * v * currentForce;

            if (applyAtEngine)
                rb.AddForceAtPosition(f, transform.TransformPoint(enginePosLocal), ForceMode.Acceleration);
            else
                rb.AddForce(f, ForceMode.Acceleration);
        }

        // 좌우 회전
        if (Mathf.Abs(h) > 0.01f)
        {
            rb.AddTorque(transform.up * h * turnTorque, ForceMode.Acceleration);
        }
    }

    // 벽 충돌 처리
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Boundary"))
        {
            if (GameManager.Instance != null) GameManager.Instance.Lose();
        }
    }

    // 태풍(Obstacle) 및 결승선(Finish) 처리
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle"))
        {
            if (GameManager.Instance != null) GameManager.Instance.Lose();
        }
        else if (other.CompareTag("Finish"))
        {
            if (GameManager.Instance != null) GameManager.Instance.Win();
        }
    }
}