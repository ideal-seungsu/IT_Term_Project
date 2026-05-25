using UnityEngine;

// 인스펙터에서 스테이지별 설정을 쉽게 묶어서 볼 수 있도록 하는 클래스
[System.Serializable]
public class StageSetting
{
    public string stageName;               // 예: "Stage 1", "Stage 2"
    public float spawnInterval;            // 스폰 주기 (짧을수록 자주 나옴 = 어려움)
    public GameObject[] allowedObstacles;  // 이 스테이지에서 등장할 수 있는 장애물들
}

public class ObstacleSpawner : MonoBehaviour
{
    [Header("스테이지 세팅")]
    public StageSetting[] stages;          // 1, 2, 3 스테이지 설정을 넣을 배열
    public int currentStageIndex = 0;      // 현재 스테이지 인덱스 (0 = 1스테이지)

    [Header("스폰 위치 설정")]
    public Transform player;
    public float spawnDistance = 150f;
    public float spawnWidth = 50f;

    private float timer = 0f;

    void Update()
    {
        // 설정된 스테이지가 없으면 작동 안 함
        if (stages.Length == 0) return;

        // 현재 스테이지의 세팅값을 가져옴
        StageSetting currentSetting = stages[currentStageIndex];

        timer += Time.deltaTime;
        // 현재 스테이지의 스폰 주기에 따라 장애물 생성
        if (timer >= currentSetting.spawnInterval)
        {
            SpawnObstacle(currentSetting);
            timer = 0f;
        }
    }

    void SpawnObstacle(StageSetting setting)
    {
        if (setting.allowedObstacles.Length == 0 || player == null) return;

        // 현재 스테이지에 허용된 장애물 중 랜덤 선택
        int randIndex = Random.Range(0, setting.allowedObstacles.Length);
        GameObject selectedPrefab = setting.allowedObstacles[randIndex];

        Vector3 spawnPos = player.position + player.forward * spawnDistance;
        spawnPos.x += Random.Range(-spawnWidth, spawnWidth);
        spawnPos.y = 0;

        Instantiate(selectedPrefab, spawnPos, Quaternion.identity);
    }

    // 외부(GameManager 등)에서 스테이지를 올릴 때 호출할 함수
    public void LevelUp()
    {
        if (currentStageIndex < stages.Length - 1)
        {
            currentStageIndex++;
            Debug.Log("스테이지 업! 현재 스테이지: " + stages[currentStageIndex].stageName);
        }
    }
}