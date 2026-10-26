using System.Collections.Generic;
using UnityEngine;
using HexSlice.Math;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HexSlice.Gameplay
{
    public class HexSliceGame : MonoBehaviour
    {
        [Header("Настройки поля (Сота)")]
        public float baseRadius = 3.2f;
        public float targetShrinkPercent = 75f;
        public float maxCutDuration = 5.0f;

        [Header("Игровые параметры")]
        public int maxLives = 3;
        public float stasisDuration = 4.0f;
        public float stasisSpeedMultiplier = 0.35f;

        private Camera _cam;
        private Material _sharedMat;
        private Sprite _circleSprite;

        private MeshFilter _boardMeshFilter;
        private MeshRenderer _boardMeshRenderer;
        private Mesh _boardMesh;
        private LineRenderer _ghostOutline;
        private LineRenderer _activeOutline;
        private LineRenderer _sliceLine;

        private List<Vector2> _origHexagon = new List<Vector2>();
        private List<Vector2> _polygon = new List<Vector2>();
        private Vector2 _polyCentroid;
        private float _initialArea;
        private float _currentShrink;

        private int _level = 1;
        private int _lives = 3;
        private int _coins = 0;
        private int _relicParts = 0;
        private float _stasisCharge = 0f;
        private bool _isStasisActive = false;
        private float _stasisTimer = 0f;

        private bool _isCutting = false;
        private Vector2 _cutStart;
        private Vector2 _cutCurrent;
        private float _cutTimer = 0f;

        private string _bannerText = "Свайпай насквозь через соту";
        private Color _bannerColor = new Color(0.14f, 0.75f, 0.98f);
        private float _toastTimer = 0f;
        private string _toastText = "";
        private float _shakeTimer = 0f;
        private Vector3 _camBasePos = new Vector3(0f, 0f, -10f);

        private class BubbleEntity
        {
            public Transform root;
            public SpriteRenderer bodySr;
            public Transform leftEye, rightEye;
            public Transform leftPupil, rightPupil;
            public Vector2 pos;
            public Vector2 vel;
            public float radius;
            public float wobblePhase;
            public float wobbleSpeed;
            public float squash = 1f;
            public float squashAngle = 0f;
            public float blinkTimer;
            public bool isBlinking;
            public bool panicked;
        }

        private class CoinEntity
        {
            public Transform tr;
            public SpriteRenderer sr;
            public Vector2 pos;
            public float radius = 0.22f;
            public float pulsePhase;
        }

        private readonly List<BubbleEntity> _bubbles = new List<BubbleEntity>();
        private readonly List<CoinEntity> _activeCoins = new List<CoinEntity>();

        private readonly Color[] _bubblePalette = new Color[]
        {
            new Color(0.93f, 0.28f, 0.60f), // Розовый
            new Color(0.02f, 0.71f, 0.83f), // Циан
            new Color(0.92f, 0.70f, 0.03f), // Золотой
            new Color(0.66f, 0.33f, 0.97f)  // Фиолетовый
        };

        private void Awake()
        {
            SetupCamera();
            CreateProceduralAssets();
            CreateBoardObjects();
        }

        private void Start()
        {
            StartLevel(1);
        }

        private void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                _cam = camObj.AddComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.backgroundColor = new Color(0.043f, 0.051f, 0.078f); // #0b0d14
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.transform.position = _camBasePos;
            AdjustCameraOrthoSize();
        }

        private void AdjustCameraOrthoSize()
        {
            if (_cam == null) return;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float minHalfWidth = baseRadius * 1.45f;
            float neededOrthoByWidth = minHalfWidth / Mathf.Max(0.2f, aspect);
            _cam.orthographicSize = Mathf.Max(5.2f, neededOrthoByWidth);
        }

        private void CreateProceduralAssets()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            _sharedMat = new Material(shader);

            int res = 128;
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float center = (res - 1) * 0.5f;
            float maxR = center - 1f;

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01((maxR - dist) * 0.75f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res * 0.5f);
        }

        private void CreateBoardObjects()
        {
            GameObject boardObj = new GameObject("HexBoard");
            boardObj.transform.SetParent(transform, false);
            _boardMeshFilter = boardObj.AddComponent<MeshFilter>();
            _boardMeshRenderer = boardObj.AddComponent<MeshRenderer>();
            _boardMesh = new Mesh { name = "HexPolygonMesh" };
            _boardMeshFilter.mesh = _boardMesh;

            Material boardMat = new Material(_sharedMat);
            boardMat.color = new Color(0.11f, 0.14f, 0.24f, 0.92f);
            _boardMeshRenderer.material = boardMat;

            _ghostOutline = CreateLine("GhostHexOutline", 0.04f, new Color(1f, 1f, 1f, 0.12f), true, 1);
            _activeOutline = CreateLine("ActiveHexOutline", 0.075f, new Color(0f, 0.94f, 1f, 0.95f), true, 2);
            _sliceLine = CreateLine("SliceLaserLine", 0.06f, new Color(0.22f, 0.74f, 0.97f, 1f), false, 10);
            _sliceLine.enabled = false;
        }

        private LineRenderer CreateLine(string objName, float width, Color col, bool loop, int sortOrder)
        {
            GameObject go = new GameObject(objName);
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(_sharedMat);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = col;
            lr.endColor = col;
            lr.loop = loop;
            lr.useWorldSpace = true;
            lr.numCapVertices = 6;
            lr.numCornerVertices = 6;
            lr.sortingOrder = sortOrder;
            return lr;
        }

        public void StartLevel(int lvl)
        {
            _level = lvl;
            _lives = maxLives;
            _isStasisActive = false;
            _stasisTimer = 0f;
            if (_relicParts >= 3) _stasisCharge = 1f;

            InitHexagon();
            SpawnBubblesForLevel(lvl);
            SpawnGreedCoins(2);

            _bannerText = "Свайпай насквозь за края соты (Цель: 75%)";
            _bannerColor = new Color(0.22f, 0.74f, 0.97f);
        }

        private void InitHexagon()
        {
            _origHexagon.Clear();
            _polygon.Clear();

            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f + Mathf.PI / 6f;
                Vector2 pt = new Vector2(baseRadius * Mathf.Cos(angle), baseRadius * Mathf.Sin(angle));
                _origHexagon.Add(pt);
                _polygon.Add(pt);
            }

            _initialArea = PolygonMath.PolygonArea(_polygon);
            UpdateBoardVisuals();
            UpdateLinesFromPolygon(_ghostOutline, _origHexagon);
        }

        private void UpdateBoardVisuals()
        {
            _polyCentroid = PolygonMath.GetCentroid(_polygon);
            float currentArea = PolygonMath.PolygonArea(_polygon);
            _currentShrink = Mathf.Clamp((1f - currentArea / _initialArea) * 100f, 0f, 100f);

            int n = _polygon.Count;
            Vector3[] verts = new Vector3[n + 1];
            Color[] cols = new Color[n + 1];
            int[] tris = new int[n * 3];

            verts[0] = new Vector3(_polyCentroid.x, _polyCentroid.y, 0f);
            cols[0] = new Color(0.16f, 0.12f, 0.34f, 0.95f);

            for (int i = 0; i < n; i++)
            {
                verts[i + 1] = new Vector3(_polygon[i].x, _polygon[i].y, 0f);
                cols[i + 1] = new Color(0.08f, 0.10f, 0.18f, 0.95f);

                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % n + 1;
            }

            _boardMesh.Clear();
            _boardMesh.vertices = verts;
            _boardMesh.colors = cols;
            _boardMesh.triangles = tris;
            _boardMesh.RecalculateBounds();

            UpdateLinesFromPolygon(_activeOutline, _polygon);
        }

        private void UpdateLinesFromPolygon(LineRenderer lr, List<Vector2> pts)
        {
            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                lr.SetPosition(i, new Vector3(pts[i].x, pts[i].y, -0.05f));
            }
        }

        private void SpawnBubblesForLevel(int lvl)
        {
            foreach (var b in _bubbles)
            {
                if (b.root != null) Destroy(b.root.gameObject);
            }
            _bubbles.Clear();

            int count = lvl == 1 ? 1 : Mathf.Min(2 + lvl, 5);
            float speed = lvl == 1 ? 2.6f : 3.4f + lvl * 0.35f;
            float bRadius = baseRadius * 0.125f;

            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f;
                float r = lvl == 1 ? baseRadius * 0.25f : baseRadius * 0.32f;
                Vector2 startPos = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                Color col = _bubblePalette[i % _bubblePalette.Length];

                BubbleEntity b = CreateBubbleVisual(startPos, bRadius, col, speed);
                _bubbles.Add(b);
            }
        }

        private BubbleEntity CreateBubbleVisual(Vector2 pos, float radius, Color col, float speed)
        {
            GameObject root = new GameObject("JellyBubble");
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(pos.x, pos.y, -0.1f);

            GameObject bodyObj = new GameObject("Body");
            bodyObj.transform.SetParent(root.transform, false);
            bodyObj.transform.localScale = Vector3.one * radius;
            SpriteRenderer bodySr = bodyObj.AddComponent<SpriteRenderer>();
            bodySr.sprite = _circleSprite;
            bodySr.color = col;
            bodySr.sortingOrder = 5;

            Transform leftEye = CreateCircleChild(bodyObj.transform, "LeftEye", new Vector2(-0.28f, 0.12f), 0.28f, Color.white, 6);
            Transform rightEye = CreateCircleChild(bodyObj.transform, "RightEye", new Vector2(0.28f, 0.12f), 0.28f, Color.white, 6);
            Transform leftPupil = CreateCircleChild(leftEye, "Pupil", Vector2.zero, 0.52f, new Color(0.06f, 0.09f, 0.16f), 7);
            Transform rightPupil = CreateCircleChild(rightEye, "Pupil", Vector2.zero, 0.52f, new Color(0.06f, 0.09f, 0.16f), 7);

            float dirAngle = Random.Range(0f, Mathf.PI * 2f);
            return new BubbleEntity
            {
                root = root.transform,
                bodySr = bodySr,
                leftEye = leftEye,
                rightEye = rightEye,
                leftPupil = leftPupil,
                rightPupil = rightPupil,
                pos = pos,
                vel = new Vector2(Mathf.Cos(dirAngle), Mathf.Sin(dirAngle)) * speed,
                radius = radius,
                wobblePhase = Random.Range(0f, 10f),
                wobbleSpeed = Random.Range(5f, 7f),
                blinkTimer = Random.Range(1.5f, 3.5f)
            };
        }

        private Transform CreateCircleChild(Transform parent, string name, Vector2 localPos, float scale, Color col, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, -0.01f);
            go.transform.localScale = Vector3.one * scale;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _circleSprite;
            sr.color = col;
            sr.sortingOrder = order;
            return go.transform;
        }

        private void SpawnGreedCoins(int count)
        {
            foreach (var c in _activeCoins)
            {
                if (c.tr != null) Destroy(c.tr.gameObject);
            }
            _activeCoins.Clear();

            if (_polygon.Count < 3) return;
            for (int i = 0; i < count; i++)
            {
                int vIdx = (i * 3) % _polygon.Count;
                Vector2 corner = _polygon[vIdx];
                Vector2 coinPos = Vector2.Lerp(corner, _polyCentroid, 0.24f);

                GameObject go = new GameObject("GreedCoin");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(coinPos.x, coinPos.y, -0.08f);
                go.transform.localScale = Vector3.one * 0.22f;

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _circleSprite;
                sr.color = new Color(0.98f, 0.75f, 0.14f); // Золотой
                sr.sortingOrder = 4;

                _activeCoins.Add(new CoinEntity
                {
                    tr = go.transform,
                    sr = sr,
                    pos = coinPos,
                    radius = 0.22f,
                    pulsePhase = i * 1.5f
                });
            }
        }

        private void Update()
        {
            AdjustCameraOrthoSize();
            HandlePointerInput();
            UpdateStasis(Time.deltaTime);
            UpdateBubbles(Time.deltaTime);
            UpdateCoins(Time.deltaTime);
            UpdateCameraShake(Time.deltaTime);

            if (_toastTimer > 0f) _toastTimer -= Time.deltaTime;
        }

        private void HandlePointerInput()
        {
            bool down = false;
            bool held = false;
            bool up = false;
            Vector2 screenPos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Pointer.current != null)
            {
                screenPos = Pointer.current.position.ReadValue();
                down = Pointer.current.press.wasPressedThisFrame;
                held = Pointer.current.press.isPressed;
                up = Pointer.current.press.wasReleasedThisFrame;
            }
#else
            screenPos = Input.mousePosition;
            down = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            up = Input.GetMouseButtonUp(0);
#endif

            // Игнорируем клики по нижней кнопке стазиса (нижние 12% экрана)
            if (down && screenPos.y < Screen.height * 0.13f) return;

            Vector3 worldPt3 = _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -_camBasePos.z));
            Vector2 worldPos = new Vector2(worldPt3.x, worldPt3.y);

            if (down)
            {
                if (PolygonMath.PointInPolygon(worldPos, _polygon))
                {
                    ShowToast("Начинайте свайп ЗА пределами соты!");
                    _bannerText = "Начинайте свайп снаружи соты!";
                    _bannerColor = new Color(0.96f, 0.62f, 0.04f);
                    _isCutting = false;
                    return;
                }

                _isCutting = true;
                _cutStart = worldPos;
                _cutCurrent = worldPos;
                _cutTimer = maxCutDuration;
                _sliceLine.enabled = true;
            }
            else if (held && _isCutting)
            {
                _cutCurrent = worldPos;
                _cutTimer -= Time.deltaTime;

                _sliceLine.positionCount = 2;
                _sliceLine.SetPosition(0, new Vector3(_cutStart.x, _cutStart.y, -0.2f));
                _sliceLine.SetPosition(1, new Vector3(_cutCurrent.x, _cutCurrent.y, -0.2f));

                UpdateLiveSliceFeedback();

                if (_cutTimer <= 0f)
                {
                    _isCutting = false;
                    _sliceLine.enabled = false;
                    ShowToast("Время удержания истекло (5.0 с)");
                }
            }
            else if (up && _isCutting)
            {
                _cutCurrent = worldPos;
                _isCutting = false;
                _sliceLine.enabled = false;
                ExecuteCut();
            }
        }

        private void UpdateLiveSliceFeedback()
        {
            bool danger = false;
            foreach (var b in _bubbles)
            {
                if (PolygonMath.DistToSegment(b.pos, _cutStart, _cutCurrent) < b.radius + 0.08f)
                {
                    danger = true;
                    break;
                }
            }

            if (danger)
            {
                _bannerText = $"ОПАСНОСТЬ: ШАРИК НА ЛИНИИ! ({_cutTimer:F1}с)";
                _bannerColor = new Color(0.94f, 0.27f, 0.27f);
                _sliceLine.startColor = _bannerColor;
                _sliceLine.endColor = _bannerColor;
                return;
            }

            if (PolygonMath.PointInPolygon(_cutCurrent, _polygon))
            {
                _bannerText = $"Ведите насквозь за край соты ({_cutTimer:F1}с)";
                _bannerColor = new Color(0.22f, 0.74f, 0.97f);
                _sliceLine.startColor = _bannerColor;
                _sliceLine.endColor = _bannerColor;
                return;
            }

            SplitResult preview = PolygonMath.SplitConvexPolygonStrictly(_polygon, _cutStart, _cutCurrent);
            if (preview.success)
            {
                _bannerText = $"ОТПУСКАЙТЕ! Срез готов ({_cutTimer:F1}с)";
                _bannerColor = new Color(0.13f, 0.77f, 0.37f);
                _sliceLine.startColor = _bannerColor;
                _sliceLine.endColor = _bannerColor;
            }
            else
            {
                _bannerText = $"Проведите линию через соту ({_cutTimer:F1}с)";
                _bannerColor = new Color(0.58f, 0.64f, 0.72f);
                _sliceLine.startColor = _bannerColor;
                _sliceLine.endColor = _bannerColor;
            }
        }

        private void ExecuteCut()
        {
            if ((_cutCurrent - _cutStart).magnitude < 0.35f) return;

            if (PolygonMath.PointInPolygon(_cutCurrent, _polygon))
            {
                ShowToast("Рез отменен: ведите насквозь за соту!");
                return;
            }

            SplitResult split = PolygonMath.SplitConvexPolygonStrictly(_polygon, _cutStart, _cutCurrent);
            if (!split.success) return;

            float minBubbleDist = float.MaxValue;
            foreach (var b in _bubbles)
            {
                float d = PolygonMath.DistToSegment(b.pos, _cutStart, _cutCurrent);
                if (d < minBubbleDist) minBubbleDist = d;

                if (d < b.radius + 0.08f)
                {
                    HandleBubbleHitPenalty();
                    return;
                }
            }

            int inA = 0, inB = 0;
            foreach (var b in _bubbles)
            {
                if (PolygonMath.PointInPolygon(b.pos, split.polyA)) inA++;
                if (PolygonMath.PointInPolygon(b.pos, split.polyB)) inB++;
            }

            if (inA > 0 && inB > 0)
            {
                _shakeTimer = 0.25f;
                ShowToast("Нельзя разделять шарики на разные части!");
                return;
            }

            List<Vector2> retained = inA > 0 ? split.polyA : split.polyB;
            List<Vector2> discarded = inA > 0 ? split.polyB : split.polyA;

            float discArea = PolygonMath.PolygonArea(discarded);
            if (discArea / _initialArea < 0.015f) return;

            // Проверка сбора Greed-монеток в отрезанном секторе
            for (int i = _activeCoins.Count - 1; i >= 0; i--)
            {
                if (PolygonMath.PointInPolygon(_activeCoins[i].pos, discarded))
                {
                    _coins += 5;
                    Destroy(_activeCoins[i].tr.gameObject);
                    _activeCoins.RemoveAt(i);
                    ShowToast("+5 монет за смелый срез угла!");
                }
            }

            // Зарядка Хроно-стазиса за Near-Miss (срез вплотную к шарику)
            float avgRadius = _bubbles[0].radius;
            if (minBubbleDist <= avgRadius + 0.55f)
            {
                _stasisCharge = Mathf.Clamp01(_stasisCharge + 0.45f);
                ShowToast("NEAR-MISS! +45% заряда Стазиса!");
            }

            _polygon = retained;
            UpdateBoardVisuals();

            if (_currentShrink >= targetShrinkPercent)
            {
                _relicParts = Mathf.Min(3, _relicParts + 1);
                _coins += 10;
                ShowToast($"УРОВЕНЬ {_level} ПРОЙДЕН! +10 монет");
                StartLevel(_level + 1);
            }
        }

        private void HandleBubbleHitPenalty()
        {
            _lives--;
            _shakeTimer = 0.35f;

            if (_lives <= 0)
            {
                ShowToast("GAME OVER! 3 ошибки — перезапуск уровня");
                StartLevel(_level);
                return;
            }

            RegenerateField();
        }

        private void RegenerateField()
        {
            float sCurr = PolygonMath.PolygonArea(_polygon);
            float targetPct = Random.Range(3.5f, 7.0f);
            float targetDelta = _initialArea * (targetPct / 100f);

            int n = _polygon.Count;
            float maxHexMidDist = baseRadius * 0.866f;
            List<(Vector2 mx, Vector2 n)> cutEdges = new List<(Vector2, Vector2)>();

            for (int i = 0; i < n; i++)
            {
                Vector2 p1 = _polygon[i];
                Vector2 p2 = _polygon[(i + 1) % n];
                Vector2 mid = (p1 + p2) * 0.5f;
                float deficit = maxHexMidDist - mid.magnitude;

                Vector2 edge = p2 - p1;
                if (edge.sqrMagnitude < 1e-5f) continue;

                Vector2 normal = new Vector2(-edge.y, edge.x).normalized;
                if ((mid + normal * 0.1f).sqrMagnitude < mid.sqrMagnitude)
                    normal = -normal;

                if (deficit > 0.08f)
                    cutEdges.Add((mid, normal));
            }

            if (cutEdges.Count == 0)
            {
                ShowToast($"Задет шарик! Осталось жизней: {_lives}");
                return;
            }

            var chosen = cutEdges[Random.Range(0, cutEdges.Count)];
            float low = 0.05f, high = baseRadius * 1.4f;
            List<Vector2> bestPoly = _polygon;
            float bestDelta = 0f;

            for (int iter = 0; iter < 10; iter++)
            {
                float midDist = (low + high) * 0.5f;
                Vector2 testPt = chosen.mx + chosen.n * midDist;
                if (testPt.magnitude > baseRadius * 0.96f)
                    testPt = testPt.normalized * (baseRadius * 0.96f);

                List<Vector2> candidatePts = new List<Vector2>(_polygon) { testPt };
                List<Vector2> hull = PolygonMath.ConvexHull(candidatePts);
                float delta = PolygonMath.PolygonArea(hull) - sCurr;

                bestPoly = hull;
                bestDelta = delta;

                if (delta < targetDelta) low = midDist;
                else high = midDist;
            }

            _polygon = bestPoly;
            UpdateBoardVisuals();
            float actualPct = Mathf.Clamp((bestDelta / _initialArea) * 100f, 3.0f, 7.0f);
            ShowToast($"Штраф -1 жизнь! Регенерация +{actualPct:F1}%");
        }

        private void UpdateStasis(float dt)
        {
            if (!_isStasisActive) return;
            _stasisTimer -= dt;
            if (_stasisTimer <= 0f)
            {
                _isStasisActive = false;
                ShowToast("Действие Хроно-стазиса завершено");
            }
        }

        private void ActivateStasis()
        {
            if (_isStasisActive || _stasisCharge < 1f) return;
            _isStasisActive = true;
            _stasisTimer = stasisDuration;
            _stasisCharge = 0f;
            ShowToast("ХРОНО-СТАЗИС АКТИВИРОВАН (-65% скорости)!");
        }

        private void UpdateBubbles(float dt)
        {
            float speedMul = _isStasisActive ? stasisSpeedMultiplier : 1f;
            int n = _polygon.Count;

            foreach (var b in _bubbles)
            {
                b.wobblePhase += dt * b.wobbleSpeed * speedMul;
                b.squash = Mathf.Lerp(b.squash, 1f, dt * 9f);

                b.panicked = _isCutting && PolygonMath.DistToSegment(b.pos, _cutStart, _cutCurrent) < b.radius + 0.65f;

                b.blinkTimer -= dt;
                if (b.blinkTimer <= 0f)
                {
                    b.isBlinking = !b.isBlinking;
                    b.blinkTimer = b.isBlinking ? 0.12f : Random.Range(2.0f, 4.0f);
                }

                b.pos += b.vel * (speedMul * dt);

                for (int i = 0; i < n; i++)
                {
                    Vector2 p1 = _polygon[i];
                    Vector2 p2 = _polygon[(i + 1) % n];
                    Vector2 edge = p2 - p1;
                    float len = edge.magnitude;
                    if (len < 1e-5f) continue;

                    Vector2 normal = new Vector2(-edge.y / len, edge.x / len);
                    Vector2 mid = (p1 + p2) * 0.5f;
                    if (Vector2.Dot(normal, _polyCentroid - mid) < 0f)
                        normal = -normal;

                    float d = PolygonMath.DistToSegment(b.pos, p1, p2);
                    if (d < b.radius)
                    {
                        float dot = Vector2.Dot(b.vel, normal);
                        if (dot < 0f)
                        {
                            b.vel -= 2f * dot * normal;
                            b.pos += normal * (b.radius - d);
                            b.squash = 0.65f;
                            b.squashAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
                        }
                    }
                }

                b.root.position = new Vector3(b.pos.x, b.pos.y, -0.1f);
                float wobble = Mathf.Sin(b.wobblePhase) * (b.panicked ? 0.1f : 0.045f);
                float sx = (b.squash + wobble) * b.radius * 2f;
                float sy = ((1f / Mathf.Max(0.5f, b.squash)) - wobble) * b.radius * 2f;

                b.bodySr.transform.rotation = Quaternion.Euler(0f, 0f, b.squashAngle);
                b.bodySr.transform.localScale = new Vector3(sx, sy, 1f);

                Vector2 dir = b.vel.normalized * 0.16f;
                b.leftPupil.localPosition = new Vector3(dir.x, dir.y, -0.01f);
                b.rightPupil.localPosition = new Vector3(dir.x, dir.y, -0.01f);

                float eyeScaleY = b.isBlinking ? 0.05f : (b.panicked ? 0.34f : 0.28f);
                b.leftEye.localScale = new Vector3(0.28f, eyeScaleY, 1f);
                b.rightEye.localScale = new Vector3(0.28f, eyeScaleY, 1f);
            }
        }

        private void UpdateCoins(float dt)
        {
            for (int i = _activeCoins.Count - 1; i >= 0; i--)
            {
                var c = _activeCoins[i];
                c.pulsePhase += dt * 4f;
                float s = 0.22f + Mathf.Sin(c.pulsePhase) * 0.03f;
                c.tr.localScale = new Vector3(s, s, 1f);

                // Если шарик задел монетку раньше среза — монетка сгорает
                foreach (var b in _bubbles)
                {
                    if (Vector2.Distance(b.pos, c.pos) < b.radius + c.radius * 0.6f)
                    {
                        Destroy(c.tr.gameObject);
                        _activeCoins.RemoveAt(i);
                        ShowToast("Шарик разбил Greed-монетку!");
                        break;
                    }
                }
            }
        }

        private void UpdateCameraShake(float dt)
        {
            if (_shakeTimer > 0f)
            {
                _shakeTimer -= dt;
                Vector2 offset = Random.insideUnitCircle * 0.12f;
                _cam.transform.position = _camBasePos + new Vector3(offset.x, offset.y, 0f);
            }
            else
            {
                _cam.transform.position = _camBasePos;
            }
        }

        private void ShowToast(string msg)
        {
            _toastText = msg;
            _toastTimer = 2.2f;
        }

        private void OnGUI()
        {
            float w = Screen.width;
            float h = Screen.height;
            int fontSize = Mathf.Max(14, Mathf.RoundToInt(h * 0.022f));

            GUIStyle cardStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            cardStyle.normal.textColor = Color.white;

            // Верхний HUD
            string livesStr = new string('♥', _lives) + new string('♡', Mathf.Max(0, maxLives - _lives));
            GUI.Box(new Rect(16, 14, w - 32, h * 0.06f),
                $"Срезано: {_currentShrink:F0}% / {targetShrinkPercent:F0}%   |   Ур. {_level}   |   {livesStr}   |   Монеты: {_coins}",
                cardStyle);

            // Баннер статуса реза
            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = _bannerColor;
            GUI.Box(new Rect(16, 18 + h * 0.065f, w - 32, h * 0.05f), _bannerText, cardStyle);

            // Всплывающий тост
            if (_toastTimer > 0f)
            {
                GUI.backgroundColor = new Color(0.95f, 0.35f, 0.2f);
                GUI.Box(new Rect(w * 0.1f, h * 0.80f, w * 0.8f, h * 0.05f), _toastText, cardStyle);
            }

            // Нижняя кнопка Хроно-стазиса
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold
            };

            string stasisLabel;
            if (_isStasisActive)
            {
                GUI.backgroundColor = new Color(0.02f, 0.71f, 0.83f);
                stasisLabel = $"⚡ СТАЗИС АКТИВЕН ({_stasisTimer:F1}с)";
            }
            else if (_stasisCharge >= 1f)
            {
                GUI.backgroundColor = new Color(0.96f, 0.62f, 0.04f);
                stasisLabel = "⏳ АКТИВИРОВАТЬ ХРОНО-СТАЗИС (-65% скорости)";
            }
            else
            {
                GUI.backgroundColor = new Color(0.25f, 0.28f, 0.38f);
                stasisLabel = $"⏳ ХРОНО-РЕЛИКТ N: Заряд {Mathf.RoundToInt(_stasisCharge * 100f)}% (Режь ближе к шарикам!)";
            }

            if (GUI.Button(new Rect(16, h - h * 0.085f - 14, w - 32, h * 0.08f), stasisLabel, btnStyle))
            {
                ActivateStasis();
            }

            GUI.backgroundColor = prevColor;
        }
    }
}