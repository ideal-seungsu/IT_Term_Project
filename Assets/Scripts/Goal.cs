using UnityEngine;


/// 목적지(빛나는 원형 포털) 트리거. Player(보트)가 도달하면 GameManager.Win() 호출.
/// BoatProbes 처럼 root 에만 Player tag 가 있는 보트도 잡도록 attachedRigidbody.gameObject 의 tag 를 확인.

public class Goal : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        var rootGo = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
        if (rootGo.CompareTag("Player") && GameManager.Instance != null)
        {
            GameManager.Instance.Win();
        }
    }
}
