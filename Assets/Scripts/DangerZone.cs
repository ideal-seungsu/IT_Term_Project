using UnityEngine;

public class DangerZone : MonoBehaviour
{
    [Header("조류/측풍 설정")]
    public Vector3 driftForce = new Vector3(50000f, 0, 0);

    [Header("경고 UI 설정")]
    public GameObject warningUI;

    // 부딪힌 녀석의 제일 꼭대기 부모가 Player 태그인지 확인하는 함수
    private bool IsPlayer(Collider other)
    {
        return other.transform.root.CompareTag("Player");
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            if (warningUI != null) warningUI.SetActive(true);
            Debug.Log("위험 구역 진입 성공!");
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (IsPlayer(other))
        {
            Rigidbody playerRb = other.transform.root.GetComponent<Rigidbody>();
            if (playerRb != null)
            {
                // ForceMode.Force 대신 ForceMode.Acceleration을 사용하면
                // 배의 질량(무게)을 완전히 무시하고 설정한 수치만큼 무조건 가속시킵니다!
                playerRb.AddForce(driftForce * Time.fixedDeltaTime, ForceMode.Acceleration);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            if (warningUI != null) warningUI.SetActive(false);
            Debug.Log("위험 구역 이탈!");
        }
    }
}