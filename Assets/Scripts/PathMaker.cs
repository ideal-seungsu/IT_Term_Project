using UnityEngine;
using System.Collections.Generic;

public class PathMaker : MonoBehaviour
{
    public List<Transform> waypoints = new List<Transform>(); // 항로 점들
    public float lineWidth = 3.0f; // 선 굵기
    private LineRenderer lr;

    void Start()
    {
        lr = GetComponent<LineRenderer>();
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        // 선이 바다 물결 위로 살짝 뜨게 설정 (중요!)
        transform.position = new Vector3(0, 2.0f, 0);
    }

    void Update()
    {
        if (waypoints.Count < 2) return;
        lr.positionCount = waypoints.Count;
        for (int i = 0; i < waypoints.Count; i++)
        {
            // 각 점의 위치를 선의 좌표로 입력
            lr.SetPosition(i, waypoints[i].position);
        }
    }
}