using UnityEngine;
/// UI 버튼 이벤트를 GameManager에 연결하는 헬퍼.
/// Start/Restart 버튼의 OnClick에 이 스크립트의 메서드를 연결.
public class UIManager : MonoBehaviour
{
    public void OnStartButton()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.StartGame();
    }

    public void OnRestartButton()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.RestartGame();
    }
}

