# Marine Mobility Simulator — Unity Team Project

> 인하대학교 "IT 기반 해양 모빌리티 시스템" 11주차 텀프로젝트 (지능형 설계 자동화 연구실, 조교 남영욱 — ideal_nam@inha.edu, 2026-1학기)

## 즉시 시작 가이드

1. `/mcp` 입력해서 `unity-mcp` 서버가 connected 인지 확인. 없으면 Unity Editor가 켜져있는지 + `~/.claude.json` 에 `unity-mcp` 엔트리 있는지 확인.
2. Unity Editor가 안 켜져있으면 `C:\Program Files\Unity\Hub\Editor\6000.0.74f1\Editor\Unity.exe -projectPath "C:\Users\WIN10\Desktop\유니티 팀플\TeamProject"` 로 실행.
3. 첫 작업으로 아래 "빌드 순서" Step 1부터 진행.

## 게임 컨셉

해양 모빌리티(선박/무인수상정/ROV)가 출발 지점에서 목적지까지 **장애물·위험 구역을 회피하면서 제한 시간 내 도착**하는 Unity 시뮬레이터.

- 성공: Goal 지점 도착
- 실패: 충돌 (감점/재시작 중 1개 이상)
- 차별화 포인트: 항로 구성, 장애물 배치, UI 연출

## 필수 구현 8가지 (체크리스트)

- [ ] **GameManager**: 시작 / 성공 / 실패 / 재시작 흐름 관리 (싱글톤)
- [ ] **Player 조작**: 키보드 좌우 이동 + 전진 (해양 모빌리티 = 선박)
- [ ] **해상 환경**: 바다, 항로, 부표, 암초, 항만, 안개 중 다수
- [ ] **장애물 / 위험요소**: 해상 장애물 + 위험 구역 배치
- [ ] **충돌 처리**: 충돌 시 실패 / 감점 / 재시작 중 1개 이상
- [ ] **Goal (목적지)**: 도착 시 성공 처리
- [ ] **UI**: 거리 / 남은 시간 / 점수 / 충돌 횟수 중 **2개 이상** 표시
- [ ] **Prefab화**: 장애물·부표·아이템 등 반복 오브젝트 재사용

## 가산점 (시간 남으면)

- 자동 회피 기능
- 난이도 Level 구분
- 제한 시간 또는 충돌 횟수 기반 감점
- 해양 Asset, 안개, 파도, 조명 효과
- 시작 / 종료 화면, 사운드, 미니맵 등

## 추천 빌드 순서

1. **GameManager 싱글톤** (`Assets/Scripts/GameManager.cs`) — 게임 상태(Playing/Win/Lose) enum, 시작/종료/재시작 메서드
2. **Player 컨트롤러** (`Assets/Scripts/PlayerController.cs`) — Rigidbody 기반 좌우 + 전진, 키보드 입력
3. **해상 환경 기본**: 바다 평면 (Plane + 머티리얼) + 큰 항만 영역
4. **Goal 트리거** (`Assets/Scripts/Goal.cs`) — OnTriggerEnter 로 GameManager.Win() 호출
5. **장애물 Prefab** (`Assets/Prefabs/Obstacle.prefab`) + 충돌 처리 (`Assets/Scripts/Obstacle.cs`)
6. **UI Canvas**: Distance 텍스트 + Timer 텍스트 (TMPro)
7. **항로 + 위험구역 배치** — 씬에 장애물 인스턴스 여러 개
8. **시작 / 종료 화면 / 효과 음향** (가산점)

## 핵심 평가 기준

1. **실행 가능한 게임 흐름** — 빌드 안 되면 큰 폭 감점
2. **해양 모빌리티 주제성** — 큐브 굴리기 ❌, 선박 항해 ✅
3. **기능 및 코드 설명 가능 여부** — 코드 주석 + 변수명 명확하게

## 제약 사항

- ⚠️ **C# 스크립트, GameObject, Prefab, 머티리얼, 씬 이름 모두 영문만 사용** (한글 금지). 한국어 주석은 OK.
- Asset Store 사용 가능 — 단 `Assets/3rdParty/<asset_name>/CREDIT.md` 에 출처 명시
- 실행 불가 프로젝트는 큰 폭 감점 → 빌드 테스트 자주 할 것
- 타 팀 파일 복사·수정 시 부정행위 처리
- 라이브 시연 실패 대비 영상 필수 제출

## 제출물

- Unity 프로젝트 zip 파일
- 발표 자료 PDF 또는 PPT
- 실행 영상 1분 내외
- 팀원별 역할 분담표
- 주요 코드 설명

발표: 팀 당 10분 (총 90분: 안내 5 / 8팀 발표 80 / 마무리 5)

## 환경 정보

| 항목 | 값 |
|------|------|
| Unity 버전 | 6.0.74f1 LTS |
| 프로젝트 경로 | `C:\Users\WIN10\Desktop\유니티 팀플\TeamProject` |
| AI Assistant 패키지 | `com.unity.ai.assistant@2.7.0-pre.3` |
| MCP Relay 바이너리 | `C:\Users\WIN10\.unity\relay\relay_win.exe` |
| Code Editor | Cursor (Visual Studio 미설치) |
| Unity AI 무료 체험 만료 | **2026-05-27** (이후 월 $10 자동 결제) |

## MCP 도구로 할 일

새 세션에서 `mcp__unity-mcp__*` 도구가 로드되면 다음 같은 명령으로 작업 가능:
- "씬에 GameManager GameObject 만들고 GameManager.cs 컴포넌트 붙여줘"
- "Player 큐브 만들고 PlayerController 컴포넌트 + Rigidbody 붙여줘"
- "Main Camera를 Player 자식으로 이동시키고 위치 조정"
- "Cube 5개를 X축 따라 배치해서 장애물처럼 만들어줘"

코드 작성 / 파일 편집은 항상 MCP로 가능. 인스펙터 드래그·드롭만 가끔 사용자 클릭 필요.

## 최우선 다음 행동

새 세션 시작 시 가장 먼저:
1. 위 "체크리스트" 의 GameManager / Player / Goal 3개 스크립트 작성
2. 작성 후 Unity 씬에 GameObject 만들고 컴포넌트 연결
3. Play 모드로 기본 흐름 (이동 → Goal 도달 → Win 표시) 검증
