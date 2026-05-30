using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Ready, Playing, Win, Lose }
    public GameState CurrentState { get; private set; } = GameState.Ready;

    [Header("Game Settings")]
    [SerializeField] private float totalTime = 120f;
    [SerializeField] private int crashLimit = 1;

    // ★ 점수 설정 - 인스펙터에서 밸런싱 가능
    [Header("Scoring Settings")]
    [SerializeField] private int baseScore = 1000;            // 시작 점수
    [SerializeField] private int collisionPenalty = 100;      // 충돌 1회당 차감
    [SerializeField] private int timeBonusPerSecond = 10;     // 남은 1초당 보너스 점수

    [Header("Boat Skin Settings")]
    [SerializeField] private GameObject[] boatModels;
    private static int currentBoatIndex = 0;

    // ★ 씬이 넘어가도 유지되어야 하는 데이터들 (static)
    private static int score = 1000;
    private static int totalCollisionCount = 0;
    private static int totalTimeBonus = 0;        // ★ 신규: 누적 시간 보너스 (스테이지 클리어 시 합산)
    private static bool isRestarting = false;     // 리스타트 버튼 진행 중인지 기억하는 플래그
    private static bool isFirstStart = true;      // ★ 신규: 게임 첫 시작인지 판별

    [Header("UI Panels")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject skinShopPanel;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;

    [Header("UI Text References")]
    [SerializeField] private TextMeshProUGUI distanceText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI collisionText;
    [SerializeField] private TextMeshProUGUI winScoreText;
    [SerializeField] private TextMeshProUGUI loseScoreText;

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform goal;

    private float remainingTime;
    private int collisionCount = 0; // 현재 스테이지 전용 충돌 횟수

    [SerializeField] private bool startImmediately = false;

    void Awake()
    {
        // 씬마다 새 GameManager가 Instance로 등록 (DontDestroyOnLoad 미사용)
        Instance = this;
    }

    void Start()
    {
        InitStage();
    }

    private void InitStage()
    {
        remainingTime = totalTime;
        UpdateBoatSkin();

        // 완전 처음 시작(메인 씬에서 리스타트도 아님)일 때만 모든 점수 초기화
        if (!isRestarting && isFirstStart && SceneManager.GetActiveScene().buildIndex == 0)
        {
            score = baseScore;
            totalCollisionCount = 0;
            totalTimeBonus = 0;
            isFirstStart = false;
        }

        // 리스타트면 점수 다시 baseScore로
        if (isRestarting)
        {
            score = baseScore;
            totalCollisionCount = 0;
            totalTimeBonus = 0;
        }

        // 리스타트/즉시시작/스테이지2 이상이면 시작 화면 스킵
        if (isRestarting || startImmediately || SceneManager.GetActiveScene().buildIndex > 0)
        {
            isRestarting = false;
            StartGame();
        }
        else
        {
            SetState(GameState.Ready);
            ShowStartPanel();
        }
    }

    void Update()
    {
        if (CurrentState != GameState.Playing) return;
        remainingTime -= Time.deltaTime;
        if (remainingTime <= 0f) { remainingTime = 0f; Lose(); return; }
        UpdateUI();
    }

    public void OpenSkinShop()
    {
        if (startPanel != null) startPanel.SetActive(false);
        if (skinShopPanel != null) skinShopPanel.SetActive(true);
    }

    public void CloseSkinShop()
    {
        if (skinShopPanel != null) skinShopPanel.SetActive(false);
        if (startPanel != null) startPanel.SetActive(true);
    }

    public void NextBoat()
    {
        if (boatModels == null || boatModels.Length == 0) return;
        currentBoatIndex = (currentBoatIndex + 1) % boatModels.Length;
        UpdateBoatSkin();
    }

    public void PrevBoat()
    {
        if (boatModels == null || boatModels.Length == 0) return;
        currentBoatIndex--;
        if (currentBoatIndex < 0) currentBoatIndex = boatModels.Length - 1;
        UpdateBoatSkin();
    }

    private void UpdateBoatSkin()
    {
        if (boatModels == null || boatModels.Length == 0) return;
        for (int i = 0; i < boatModels.Length; i++)
        {
            if (boatModels[i] != null) boatModels[i].SetActive(i == currentBoatIndex);
        }
    }

    public void StartGame()
    {
        Time.timeScale = 1f;
        collisionCount = 0;          // 스테이지별 충돌 횟수만 리셋
        remainingTime = totalTime;
        SetState(GameState.Playing);
        HideAllPanels();
    }

    // ★ 스테이지 클리어 시점에 시간 보너스 누적
    public void Win()
    {
        if (CurrentState != GameState.Playing) return;
        SetState(GameState.Win);

        // 이번 스테이지의 시간 보너스 계산해서 누적
        int stageTimeBonus = Mathf.RoundToInt(remainingTime * timeBonusPerSecond);
        totalTimeBonus += stageTimeBonus;

        StartCoroutine(AutoNextStageRoutine());
    }

    private System.Collections.IEnumerator AutoNextStageRoutine()
    {
        yield return new WaitForSeconds(1.5f);
        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;

        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            // 다음 스테이지가 있으면 그냥 넘어감
            SceneManager.LoadScene(nextSceneIndex);
        }
        else
        {
            // ★ 모든 스테이지 클리어! 최종 결과 표시
            Debug.Log("모든 스테이지 클리어!");

            int finalScore = score + totalTimeBonus;

            if (winPanel != null)
            {
                winPanel.SetActive(true);
                Time.timeScale = 0f;
            }

            if (winScoreText != null)
            {
                winScoreText.text =
                    $"CLEAR!\n" +
                    $"Base Score: {score}\n" +
                    $"Time Bonus: +{totalTimeBonus}\n" +
                    $"Total Collisions: {totalCollisionCount}\n" +
                    $"Final Score: {finalScore}";
            }
        }
    }

    // ★ Lose 시 점수와 통계 표시 (기존 버그 해결)
    public void Lose()
    {
        if (CurrentState != GameState.Playing) return;
        SetState(GameState.Lose);

        int timeUsed = (int)(totalTime - remainingTime);

        if (losePanel != null) losePanel.SetActive(true);
        if (loseScoreText != null)
        {
            loseScoreText.text =
                $"GAME OVER\n" +
                $"Score: {score}\n" +
                $"Total Collisions: {totalCollisionCount}\n" +
                $"Time Used: {timeUsed}s";
        }

        Time.timeScale = 0f;
    }

    public void RegisterCollision()
    {
        if (CurrentState != GameState.Playing) return;
        collisionCount++;
        totalCollisionCount++;
        score = Mathf.Max(0, score - collisionPenalty);
        if (collisionCount >= crashLimit) Lose();
    }

    private void UpdateUI()
    {
        if (player == null || goal == null) return;

        if (distanceText != null) distanceText.text = $"Distance: {Vector3.Distance(player.position, goal.position):F1}m";
        if (timerText != null) timerText.text = $"Time: {(int)remainingTime / 60:00}:{(int)remainingTime % 60:00}";
        if (scoreText != null) scoreText.text = $"Score: {score}";
        if (collisionText != null) collisionText.text = $"Total Collisions: {totalCollisionCount}";
    }

    private void ShowStartPanel()
    {
        HideAllPanels();
        if (startPanel != null) startPanel.SetActive(true);
    }

    private void HideAllPanels()
    {
        if (startPanel != null) startPanel.SetActive(false);
        if (skinShopPanel != null) skinShopPanel.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        isRestarting = true;
        SceneManager.LoadScene(0); // ★ 메인(첫 스테이지)으로 돌아가게 변경
    }

    private void SetState(GameState newState) { CurrentState = newState; }
}
