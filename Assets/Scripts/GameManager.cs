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

    [Header("Boat Skin Settings")]
    [SerializeField] private GameObject[] boatModels;
    // ★ 씬 전환 시 기억하기 위해 static(정적 변수)으로 변경하여 보존합니다.
    private static int currentBoatIndex = 0;

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
    private int score = 1000;
    private int collisionCount = 0;

    [SerializeField] private bool startImmediately = false; // 켜면 시작 화면 없이 바로 게임 시작

    void Awake()
    {
        // ★ [핵심 고침] 씬이 넘어가도 데이터가 파괴되지 않도록 뼈대를 수정합니다.
        if (Instance != null && Instance != this)
        {
            // 새로 로드된 씬의 게임매니저는 파괴하되, 그 씬에 배치된 새로운 배와 UI 참조를 기존 매니저에게 토스합니다.
            Instance.RefreshReferencesInNewScene(this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // 이 오브젝트를 파괴되지 않는 무적 방패로 지정
    }

    void Start()
    {
        InitStage();
    }

    // ★ [새로 추가] 스테이지가 바뀔 때마다 실행될 초기화 함수
    private void InitStage()
    {
        remainingTime = totalTime;
        UpdateBoatSkin();

        // 1스테이지(Main 씬, buildIndex 0)가 아니거나 즉시 시작이 켜져 있다면 대기 없이 바로 시작
        if (startImmediately || SceneManager.GetActiveScene().buildIndex > 0)
        {
            StartGame();
        }
        else
        {
            SetState(GameState.Ready);
            ShowStartPanel();
        }
    }

    // ★ [새로 추가] 다음 스테이지로 넘어갔을 때 새 씬의 UI와 배 오브젝트들을 다시 매핑해주는 무적의 방어 코드
    public void RefreshReferencesInNewScene(GameManager newSceneManager)
    {
        this.startPanel = newSceneManager.startPanel;
        this.skinShopPanel = newSceneManager.skinShopPanel;
        this.winPanel = newSceneManager.winPanel;
        this.losePanel = newSceneManager.losePanel;

        this.distanceText = newSceneManager.distanceText;
        this.timerText = newSceneManager.timerText;
        this.scoreText = newSceneManager.scoreText;
        this.collisionText = newSceneManager.collisionText;
        this.winScoreText = newSceneManager.winScoreText;
        this.loseScoreText = newSceneManager.loseScoreText;

        this.player = newSceneManager.player;
        this.goal = newSceneManager.goal;
        this.boatModels = newSceneManager.boatModels;
        this.startImmediately = newSceneManager.startImmediately;

        // 새 오브젝트들 기준으로 배 스킨 세팅 재시동
        InitStage();
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
        score = 1000;
        collisionCount = 0;
        remainingTime = totalTime;
        SetState(GameState.Playing);
        HideAllPanels();
    }

    public void Win()
    {
        if (CurrentState != GameState.Playing) return;
        SetState(GameState.Win);
        StartCoroutine(AutoNextStageRoutine());
    }

    private System.Collections.IEnumerator AutoNextStageRoutine()
    {
        yield return new WaitForSeconds(1.5f);
        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;

        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(nextSceneIndex);
        }
        else
        {
            Debug.Log("모든 스테이지 클리어! 메인 화면으로 돌아갑니다.");
            SceneManager.LoadScene(0);
        }
    }

    public void Lose()
    {
        if (CurrentState != GameState.Playing) return;
        SetState(GameState.Lose);
        if (losePanel != null) losePanel.SetActive(true);
        Time.timeScale = 0f;
    }

    public void RegisterCollision()
    {
        if (CurrentState != GameState.Playing) return;
        collisionCount++;
        score = Mathf.Max(0, score - 100);
        if (collisionCount >= crashLimit) Lose();
    }

    private void UpdateUI()
    {
        // Null 검사를 추가하여 씬 전환 직후 미세한 딜레이 타이밍 에러를 완전 방지합니다.
        if (player == null || goal == null) return;

        if (distanceText != null) distanceText.text = $"Distance: {Vector3.Distance(player.position, goal.position):F1}m";
        if (timerText != null) timerText.text = $"Time: {(int)remainingTime / 60:00}:{(int)remainingTime % 60:00}";
        if (scoreText != null) scoreText.text = $"Score: {score}";
        if (collisionText != null) collisionText.text = $"Collisions: {collisionCount}";
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
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void SetState(GameState newState) { CurrentState = newState; }
}