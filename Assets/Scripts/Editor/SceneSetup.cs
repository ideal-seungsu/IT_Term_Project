#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 해양 모빌리티 시뮬레이터 씬을 한 번에 셋업하는 에디터 메뉴.
/// 메뉴: Tools > Marine Sim > Build Main Scene
/// </summary>
public static class SceneSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string PrefabsDir = "Assets/Prefabs";

    [MenuItem("Tools/Marine Sim/Build Main Scene")]
    public static void BuildMainScene()
    {
        EnsureTags("Player", "Obstacle", "Goal", "DangerZone");
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory(PrefabsDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // --- 환경: Crest Ocean ---
        BuildCrestOcean();

        // 출발 항만 (Player 뒤편 시각적 기준)
        var startPort = GameObject.CreatePrimitive(PrimitiveType.Cube);
        startPort.name = "StartPort";
        startPort.transform.position = new Vector3(0f, 1f, -270f);
        startPort.transform.localScale = new Vector3(60f, 2f, 18f);
        ColorRenderer(startPort, new Color(0.55f, 0.45f, 0.30f));

        // 도착 항만 (Goal 트리거는 별도)
        var endPort = GameObject.CreatePrimitive(PrimitiveType.Cube);
        endPort.name = "EndPort";
        endPort.transform.position = new Vector3(0f, 1f, 270f);
        endPort.transform.localScale = new Vector3(60f, 2f, 18f);
        ColorRenderer(endPort, new Color(0.55f, 0.45f, 0.30f));

        // --- Player (Crest BoatProbes.prefab + cargo ship 색감으로 입힘) ---
        const string BoatPrefabPath = "Assets/3rdParty/Crest/Crest-Examples/BoatDev/Data/BoatProbes.prefab";
        var boatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoatPrefabPath);
        GameObject player;
        if (boatPrefab != null)
        {
            // 1) BoatProbes prefab 인스턴스 — 페리 셋업 (움직임/부력/12점 다 유지)
            player = (GameObject)PrefabUtility.InstantiatePrefab(boatPrefab);
            PrefabUtility.UnpackPrefabInstance(player, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            player.name = "Player";
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 4f, -250f);
            player.transform.rotation = Quaternion.identity;

            var bpComp = player.GetComponent("Crest.BoatProbes") as Component;
            if (bpComp != null)
            {
                var soBP = new SerializedObject(bpComp);
                soBP.FindProperty("_playerControlled").boolValue = false;
                soBP.FindProperty("_enginePower").floatValue = 0f;
                soBP.FindProperty("_turnPower").floatValue = 0f;
                soBP.ApplyModifiedProperties();
            }
            if (player.GetComponent<PlayerController>() == null)
                player.AddComponent<PlayerController>();

            // 2) 페리의 모든 visual mesh 숨김 (Renderer 만 끔 — collider/transform 은 유지)
            foreach (var r in player.GetComponentsInChildren<Renderer>())
            {
                r.enabled = false;
            }

            // 3) cargo ship visual 을 자식으로 — 페리 크기에 맞춰 scale 자동 계산
            const string CargoShipObjPath = "Assets/3rdParty/CargoShip/cargo1.obj";
            var cargoAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CargoShipObjPath);
            if (cargoAsset != null)
            {
                var shipVisual = (GameObject)PrefabUtility.InstantiatePrefab(cargoAsset);
                PrefabUtility.UnpackPrefabInstance(shipVisual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                shipVisual.name = "CargoShipVisual";
                shipVisual.transform.SetParent(player.transform, false);
                shipVisual.transform.localPosition = new Vector3(0f, -1f, 0f);
                shipVisual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // cargo ship forward 가 X 축이라 Y 90 회전
                shipVisual.transform.localScale = Vector3.one * 0.2f;
            }
            else { Debug.LogWarning("[SceneSetup] cargo1.obj not found"); }
        }
        else
        {
            Debug.LogWarning("[SceneSetup] BoatProbes.prefab not found — fallback to cube");
            player = GameObject.CreatePrimitive(PrimitiveType.Cube);
            player.name = "Player";
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 4f, -250f);
            player.transform.localScale = new Vector3(5f, 2f, 12f);
            var fbRb = player.AddComponent<Rigidbody>();
            fbRb.mass = 800f;
            player.AddComponent<PlayerController>();
        }

        // --- Camera ---
        var camGo = GameObject.Find("Main Camera");
        if (camGo == null)
        {
            camGo = new GameObject("Main Camera");
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";
        }
        var follow = camGo.GetComponent<CameraFollow>();
        if (follow == null) follow = camGo.AddComponent<CameraFollow>();
        SetPrivateField(follow, "target", player.transform);
        // 카메라를 더 높고 뒤로 — 장애물 멀리까지 보이도록
        SetPrivateField(follow, "offset", new Vector3(0f, 30f, -45f));
        SetPrivateField(follow, "smoothSpeed", 4f);
        SetPrivateField(follow, "lookAhead", 50f);

        // --- Goal (빛나는 원형 포털) ---
        var goal = new GameObject("Goal");
        goal.tag = "Goal";
        goal.transform.position = new Vector3(0f, 8f, 250f);

        // 비주얼: Cylinder 를 옆으로 눕혀 도넛 모양 원형 게이트
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "PortalRing";
        ring.transform.SetParent(goal.transform, false);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.transform.localScale = new Vector3(18f, 1f, 18f);
        Object.DestroyImmediate(ring.GetComponent<Collider>()); // 원판 collider 제거 (trigger 따로 작성)
        var portalMat = CreateOrLoadMaterial("Assets/Materials/GoalPortal.mat",
            new Color(0.20f, 0.95f, 0.45f), metallic: 0f, smoothness: 0.9f);
        if (portalMat.HasProperty("_EmissionColor"))
        {
            portalMat.EnableKeyword("_EMISSION");
            portalMat.SetColor("_EmissionColor", new Color(0.2f, 1.5f, 0.6f) * 2f);
        }
        ring.GetComponent<Renderer>().sharedMaterial = portalMat;

        // 내부 디스크: 살짝 투명 빛나는 광원판 (지나가는 느낌)
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "PortalGlow";
        disc.transform.SetParent(goal.transform, false);
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        disc.transform.localScale = new Vector3(15f, 0.1f, 15f);
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        var glowMat = CreateOrLoadMaterial("Assets/Materials/GoalGlow.mat",
            new Color(0.4f, 1f, 0.6f, 0.3f), metallic: 0f, smoothness: 0.5f);
        if (glowMat.HasProperty("_EmissionColor"))
        {
            glowMat.EnableKeyword("_EMISSION");
            glowMat.SetColor("_EmissionColor", new Color(0.3f, 1.8f, 0.7f) * 3f);
        }
        disc.GetComponent<Renderer>().sharedMaterial = glowMat;

        // 트리거: 원형 포털 안으로 보트가 들어가면 Win
        var goalCol = goal.AddComponent<SphereCollider>();
        goalCol.isTrigger = true;
        goalCol.radius = 9f;
        goal.AddComponent<Goal>();

        // 빛 효과 (점광)
        var goalLight = new GameObject("PortalLight");
        goalLight.transform.SetParent(goal.transform, false);
        var lt = goalLight.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = new Color(0.4f, 1f, 0.6f);
        lt.intensity = 4f;
        lt.range = 60f;

        // --- Obstacle Prefabs (3 종류: 어뢰 / 암초 / 부표) ---
        var obsPrefabs = new[] {
            CreateObstacleTorpedoPrefab(),
            CreateObstacleRockPrefab(),
            CreateObstacleBuoyPrefab(),
        };

        // 장애물 배치 — 항로 -220 ~ 220 영역에 약 30개를 난잡 분포 (min spacing 으로 겹침 방지)
        var rng = new System.Random(2026);
        var placed = new List<Vector3>();
        const float MinSpacing = 18f; // 장애물 간 최소 거리
        const int Target = 30;
        int attempts = 0;
        while (placed.Count < Target && attempts < Target * 30)
        {
            attempts++;
            float x = (float)(rng.NextDouble() * 110 - 55);
            float z = (float)(rng.NextDouble() * 440 - 220);
            // 항로 중앙 (출발/도착 근처) 은 비워두기
            if (Mathf.Abs(z) > 200f) continue;
            var p = new Vector3(x, 1.5f, z);
            bool ok = true;
            foreach (var pp in placed) if (Vector3.Distance(p, pp) < MinSpacing) { ok = false; break; }
            if (!ok) continue;
            placed.Add(p);
            var pick = obsPrefabs[rng.Next(obsPrefabs.Length)];
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pick);
            inst.transform.position = p;
            // 각 장애물 Y 축 랜덤 회전 (어뢰가 다양한 방향)
            inst.transform.rotation = inst.transform.rotation * Quaternion.Euler(0f, (float)(rng.NextDouble() * 360), 0f);
        }

        // --- Danger Zone (안개/암초 구역) ---
        var danger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        danger.name = "DangerZone";
        danger.tag = "DangerZone";
        danger.transform.position = new Vector3(55f, 2f, 50f);
        danger.transform.localScale = new Vector3(40f, 5f, 80f);
        ColorRenderer(danger, new Color(0.90f, 0.10f, 0.10f, 0.3f));
        danger.GetComponent<Collider>().isTrigger = true;
        danger.AddComponent<DangerZone>();

        // --- Lighting ---
        var sun = new GameObject("Directional Light");
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // --- UI Canvas ---
        var canvasGo = new GameObject("HUD Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        var distanceTxt = CreateText(canvasGo.transform, "DistanceText", "Distance: 0m",
            new Vector2(10, -10), TextAnchor.UpperLeft, anchor: new Vector2(0, 1));
        var timerTxt = CreateText(canvasGo.transform, "TimerText", "Time: 02:00",
            new Vector2(10, -40), TextAnchor.UpperLeft, anchor: new Vector2(0, 1));
        var scoreTxt = CreateText(canvasGo.transform, "ScoreText", "Score: 1000",
            new Vector2(10, -70), TextAnchor.UpperLeft, anchor: new Vector2(0, 1));
        var collisionTxt = CreateText(canvasGo.transform, "CollisionText", "Collisions: 0",
            new Vector2(10, -100), TextAnchor.UpperLeft, anchor: new Vector2(0, 1));

        // Start panel
        var startPanel = CreatePanel(canvasGo.transform, "StartPanel", new Color(0, 0, 0, 0.7f));
        CreateText(startPanel.transform, "Title", "Marine Mobility Simulator",
            new Vector2(0, 100), TextAnchor.MiddleCenter, fontSize: 48);
        CreateText(startPanel.transform, "Hint", "WASD / Arrow Keys to move\nReach the green goal!",
            new Vector2(0, 0), TextAnchor.MiddleCenter, fontSize: 24);
        var startBtn = CreateButton(startPanel.transform, "StartButton", "START",
            new Vector2(0, -100));

        // Win panel
        var winPanel = CreatePanel(canvasGo.transform, "WinPanel", new Color(0, 0.3f, 0, 0.8f));
        CreateText(winPanel.transform, "Title", "GOAL REACHED!", new Vector2(0, 120),
            TextAnchor.MiddleCenter, fontSize: 60);
        var winScoreTxt = CreateText(winPanel.transform, "ScoreText", "Score: 0",
            new Vector2(0, 20), TextAnchor.MiddleCenter, fontSize: 36);
        var winRestart = CreateButton(winPanel.transform, "RestartButton", "RESTART",
            new Vector2(0, -100));
        winPanel.SetActive(false);

        // Lose panel
        var losePanel = CreatePanel(canvasGo.transform, "LosePanel", new Color(0.3f, 0, 0, 0.8f));
        CreateText(losePanel.transform, "Title", "GAME OVER", new Vector2(0, 120),
            TextAnchor.MiddleCenter, fontSize: 60);
        var loseScoreTxt = CreateText(losePanel.transform, "ScoreText", "Score: 0",
            new Vector2(0, 20), TextAnchor.MiddleCenter, fontSize: 36);
        var loseRestart = CreateButton(losePanel.transform, "RestartButton", "RESTART",
            new Vector2(0, -100));
        losePanel.SetActive(false);

        // EventSystem
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // --- GameManager ---
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        SetPrivateField(gm, "distanceText", distanceTxt);
        SetPrivateField(gm, "timerText", timerTxt);
        SetPrivateField(gm, "scoreText", scoreTxt);
        SetPrivateField(gm, "collisionText", collisionTxt);
        SetPrivateField(gm, "winPanel", winPanel);
        SetPrivateField(gm, "losePanel", losePanel);
        SetPrivateField(gm, "startPanel", startPanel);
        SetPrivateField(gm, "winScoreText", winScoreTxt);
        SetPrivateField(gm, "loseScoreText", loseScoreTxt);
        SetPrivateField(gm, "player", player.transform);
        SetPrivateField(gm, "goal", goal.transform);

        // 버튼 와이어링: GameManager 메서드에 직접 persistent listener (UIManager 중간 단계 제거)
        UnityEventTools.AddPersistentListener(startBtn.onClick, gm.StartGame);
        UnityEventTools.AddPersistentListener(winRestart.onClick, gm.RestartGame);
        UnityEventTools.AddPersistentListener(loseRestart.onClick, gm.RestartGame);

        // Save
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[SceneSetup] Main scene built at " + ScenePath);
        Selection.activeGameObject = gmGo;
    }

    static void BuildCrestOcean()
    {
        const string OceanMatPath = "Assets/3rdParty/Crest/Crest/Materials/Ocean.mat";
        const string SpectrumAssetPath = "Assets/Settings/Ocean_Spectrum.asset";

        Directory.CreateDirectory("Assets/Settings");

        var oceanMat = AssetDatabase.LoadAssetAtPath<Material>(OceanMatPath);
        if (oceanMat == null)
        {
            Debug.LogError("[SceneSetup] Crest Ocean material not found at " + OceanMatPath);
            return;
        }

        // OceanWaveSpectrum 에셋 (없으면 생성)
        var spectrumType = System.Type.GetType("Crest.OceanWaveSpectrum, Crest");
        var spectrum = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SpectrumAssetPath);
        if (spectrum == null && spectrumType != null)
        {
            spectrum = ScriptableObject.CreateInstance(spectrumType);
            spectrum.name = "Ocean_Spectrum";
            AssetDatabase.CreateAsset(spectrum, SpectrumAssetPath);
        }

        // Ocean GameObject
        var oceanGo = new GameObject("Ocean");
        oceanGo.transform.position = Vector3.zero;

        var oceanRendererType = System.Type.GetType("Crest.OceanRenderer, Crest");
        var oceanRenderer = oceanGo.AddComponent(oceanRendererType);
        SetPrivateField(oceanRenderer, "_material", oceanMat);
        SetPrivateField(oceanRenderer, "_globalWindSpeed", 25f);
        SetPrivateField(oceanRenderer, "_globalWindTurbulence", 0.05f);

        // 자식: Wave generator (ShapeGerstner)
        var waveGo = new GameObject("Waves");
        waveGo.transform.SetParent(oceanGo.transform, false);
        var gerstnerType = System.Type.GetType("Crest.ShapeGerstner, Crest");
        var gerstner = waveGo.AddComponent(gerstnerType);
        if (spectrum != null) SetPrivateField(gerstner, "_spectrum", spectrum);

        Debug.Log("[SceneSetup] Crest Ocean setup complete.");
    }

    static GameObject CreateObstacleTorpedoPrefab()
    {
        var path = $"{PrefabsDir}/Obstacle_Torpedo.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        // 루트 — 어뢰 본체 (Capsule, X축 90° 회전 → Z 길게)
        var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "Obstacle_Torpedo";
        root.tag = "Obstacle";
        root.transform.localScale = new Vector3(2.4f, 6f, 2.4f);
        root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var bodyMat = CreateOrLoadMaterial("Assets/Materials/Obstacle_Torpedo.mat",
            new Color(0.18f, 0.20f, 0.24f), metallic: 0.85f, smoothness: 0.6f);
        root.GetComponent<Renderer>().sharedMaterial = bodyMat;
        var col = root.GetComponent<CapsuleCollider>();
        if (col != null) col.direction = 2;
        root.AddComponent<Obstacle>();

        // 꼬리 핀 4개 (X자) — 어뢰 후미 디테일
        var finMat = CreateOrLoadMaterial("Assets/Materials/Obstacle_TorpedoFin.mat",
            new Color(0.55f, 0.55f, 0.60f), metallic: 0.6f, smoothness: 0.5f);
        float[] finAngles = { 0f, 90f, 180f, 270f };
        foreach (var angle in finAngles)
        {
            var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.name = "Fin";
            // 회전이 적용된 root local에서: Z (mesh up=후미) 방향이 root의 -Y
            fin.transform.SetParent(root.transform, false);
            fin.transform.localPosition = new Vector3(0f, -0.42f, 0f); // root local -Y = 후미
            fin.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            fin.transform.localScale = new Vector3(0.06f, 0.08f, 0.5f);
            fin.GetComponent<Renderer>().sharedMaterial = finMat;
            // 핀의 collider는 제거 (본체 collider로 충돌 처리)
            Object.DestroyImmediate(fin.GetComponent<Collider>());
        }

        // 노즈 콘 (앞쪽 빨간 띠)
        var noseBand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        noseBand.name = "NoseBand";
        noseBand.transform.SetParent(root.transform, false);
        noseBand.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        noseBand.transform.localScale = new Vector3(1.05f, 0.04f, 1.05f);
        var bandMat = CreateOrLoadMaterial("Assets/Materials/Obstacle_TorpedoBand.mat",
            new Color(0.85f, 0.10f, 0.10f), metallic: 0.2f, smoothness: 0.5f);
        noseBand.GetComponent<Renderer>().sharedMaterial = bandMat;
        Object.DestroyImmediate(noseBand.GetComponent<Collider>());

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject CreateObstacleRockPrefab() => MakeObstaclePrefab(
        path: $"{PrefabsDir}/Obstacle_Rock.prefab",
        name: "Obstacle_Rock",
        primitive: PrimitiveType.Sphere,
        scale: new Vector3(8f, 7f, 8f),
        rotation: Quaternion.identity,
        color: new Color(0.38f, 0.30f, 0.22f),
        metallic: 0f, smoothness: 0.15f);

    static GameObject CreateObstacleBuoyPrefab() => MakeObstaclePrefab(
        path: $"{PrefabsDir}/Obstacle_Buoy.prefab",
        name: "Obstacle_Buoy",
        primitive: PrimitiveType.Cylinder,
        scale: new Vector3(5f, 5f, 5f),
        rotation: Quaternion.identity,
        color: new Color(0.90f, 0.10f, 0.10f),
        metallic: 0.2f, smoothness: 0.6f);

    static GameObject MakeObstaclePrefab(string path, string name, PrimitiveType primitive,
        Vector3 scale, Quaternion rotation, Color color, float metallic, float smoothness,
        int capsuleDirection = 1)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var tmp = GameObject.CreatePrimitive(primitive);
        tmp.name = name;
        tmp.tag = "Obstacle";
        tmp.transform.localScale = scale;
        tmp.transform.localRotation = rotation;

        var mat = CreateOrLoadMaterial($"Assets/Materials/{name}.mat", color, metallic, smoothness);
        tmp.GetComponent<Renderer>().sharedMaterial = mat;

        if (primitive == PrimitiveType.Capsule)
        {
            var col = tmp.GetComponent<CapsuleCollider>();
            if (col != null) col.direction = capsuleDirection;
        }
        tmp.AddComponent<Obstacle>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(tmp, path);
        Object.DestroyImmediate(tmp);
        return prefab;
    }

    static Material CreateOrLoadMaterial(string path, Color color, float metallic, float smoothness)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string text, Vector2 pos,
        TextAnchor align, int fontSize = 28, Vector2? anchor = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var pivot = anchor ?? new Vector2(0.5f, 0.5f);
        rt.anchorMin = rt.anchorMax = pivot;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(500, 60);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.alignment = AlignmentFromAnchor(align);
        tmp.enableWordWrapping = true;
        return tmp;
    }

    static GameObject CreatePanel(Transform parent, string name, Color bg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = bg;
        return go;
    }

    static Button CreateButton(Transform parent, string name, string label, Vector2 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(240, 70);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.45f, 0.85f);
        var btn = go.AddComponent<Button>();

        var txtGo = new GameObject("Label");
        txtGo.transform.SetParent(go.transform, false);
        var trt = txtGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 32;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    static TextAlignmentOptions AlignmentFromAnchor(TextAnchor anchor)
    {
        return anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center
        };
    }

    static void ColorRenderer(GameObject go, Color c)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = c;
        r.sharedMaterial = mat;
    }

    static void EnsureTags(params string[] tags)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tagsProp = tagManager.FindProperty("tags");
        foreach (var t in tags)
        {
            bool found = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == t) { found = true; break; }
            }
            if (!found)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = t;
            }
        }
        tagManager.ApplyModifiedProperties();
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == path))
        {
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        if (field != null) field.SetValue(target, value);
        else Debug.LogWarning($"[SceneSetup] Field '{fieldName}' not found on {target.GetType().Name}");
    }
}
#endif

