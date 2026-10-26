using System;
using System.Collections.Generic;
using UnityEngine;
using HexSlice.Data;
using HexSlice.Math;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HexSlice.Gameplay
{
    public class GameManager : MonoBehaviour
    {
        [Header("Ссылки на компоненты сцены")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private HexBoard hexBoard;
        [SerializeField] private SliceInputController sliceInput;
        [SerializeField] private Transform entitiesContainer;

        [Header("Префабы сущностей (Assets/Prefabs/Gameplay)")]
        [SerializeField] private JellyBubble bubblePrefab;
        [SerializeField] private GreedCoin greedCoinPrefab;

        [Header("Конфиги уровней (Assets/Data)")]
        [SerializeField] private LevelConfigSO[] levelConfigs;
        [SerializeField] private BubbleConfigSO fallbackBubbleConfig;

        [Header("Параметры Хроно-стазиса (Реликт N)")]
        [SerializeField] private float stasisDuration = 4.0f;
        [SerializeField] private float stasisSpeedMultiplier = 0.35f;
        [SerializeField] private float nearMissDistanceMargin = 0.55f;
        [SerializeField] private float nearMissChargeGain = 0.45f;
        [SerializeField] private float bubbleHitBuffer = 0.08f;

        // События для UI (Этап 4: HUDView подписывается на них)
        public event Action<int> OnLevelStarted;
        public event Action<float, float> OnProgressChanged;      // (currentShrink%, targetShrink%)
        public event Action<int, int> OnLivesChanged;             // (currentLives, maxLives)
        public event Action<int> OnCoinsChanged;                  // (totalCoins)
        public event Action<float, bool, float> OnStasisChanged;  // (charge01, isActive, remainingSec)
        public event Action<string> OnToastMessage;
        public event Action<int> OnLevelCompleted;
        public event Action<int> OnGameOver;

        public int CurrentLevelIndex { get; private set; }
        public int CurrentLives { get; private set; }
        public int MaxLives { get; private set; } = 3;
        public int TotalCoins { get; private set; }
        public int RelicShards { get; private set; }
        public float StasisCharge { get; private set; }
        public bool IsStasisActive { get; private set; }

        private readonly List<JellyBubble> _activeBubbles = new List<JellyBubble>();
        private readonly List<GreedCoin> _activeCoins = new List<GreedCoin>();

        private LevelConfigSO _currentConfig;
        private float _targetShrink = 75f;
        private float _stasisTimer;
        private float _shakeTimer;
        private Vector3 _camBasePos = new Vector3(0f, 0f, -10f);

        private void Awake()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (entitiesContainer == null) entitiesContainer = transform;

            if (mainCamera != null)
            {
                mainCamera.orthographic = true;
                _camBasePos = mainCamera.transform.position;
            }
        }

        private void OnEnable()
        {
            if (sliceInput != null)
            {
                sliceInput.OnCutExecuted += HandleCutExecuted;
                sliceInput.OnInputRejected += EmitToast;
            }
            if (hexBoard != null)
            {
                hexBoard.OnShrinkChanged += HandleBoardShrinkChanged;
            }
        }

        private void OnDisable()
        {
            if (sliceInput != null)
            {
                sliceInput.OnCutExecuted -= HandleCutExecuted;
                sliceInput.OnInputRejected -= EmitToast;
            }
            if (hexBoard != null)
            {
                hexBoard.OnShrinkChanged -= HandleBoardShrinkChanged;
            }
        }

        private void Start()
        {
            LoadLevel(0);
        }

        public void LoadLevel(int index)
        {
            CurrentLevelIndex = index;
            _currentConfig = (levelConfigs != null && levelConfigs.Length > 0)
                ? levelConfigs[index % levelConfigs.Length]
                : null;

            _targetShrink = _currentConfig != null ? _currentConfig.targetShrinkPercent : 75f;
            MaxLives = _currentConfig != null ? _currentConfig.maxLives : 3;
            CurrentLives = MaxLives;
            IsStasisActive = false;
            _stasisTimer = 0f;

            if (RelicShards >= 3)
                StasisCharge = 1f;

            if (hexBoard != null)
            {
                hexBoard.Initialize(_currentConfig);
                AdjustCameraToFitBoard(hexBoard.BaseRadius);
            }

            SpawnLevelEntities();

            if (sliceInput != null)
            {
                sliceInput.Initialize(mainCamera, hexBoard, _activeBubbles);
                sliceInput.SetInputEnabled(true);
            }

            int displayLevel = _currentConfig != null ? _currentConfig.levelIndex : (index + 1);
            OnLevelStarted?.Invoke(displayLevel);
            OnLivesChanged?.Invoke(CurrentLives, MaxLives);
            OnCoinsChanged?.Invoke(TotalCoins);
            OnProgressChanged?.Invoke(hexBoard != null ? hexBoard.CurrentShrinkPercent : 0f, _targetShrink);
            OnStasisChanged?.Invoke(StasisCharge, IsStasisActive, _stasisTimer);

            Debug.Log($"[HexSlice] Запущен уровень {displayLevel} | Цель: {_targetShrink}% | Жизни: {CurrentLives}");
        }

        private void SpawnLevelEntities()
        {
            ClearEntities();
            if (hexBoard == null) return;

            float boardRadius = hexBoard.BaseRadius;

            // 1. Спавн капелек (JellyBubble) из LevelConfigSO
            if (bubblePrefab != null)
            {
                List<BubbleConfigSO> spawnList = new List<BubbleConfigSO>();
                if (_currentConfig != null && _currentConfig.bubbles != null)
                {
                    foreach (var entry in _currentConfig.bubbles)
                    {
                        for (int c = 0; c < entry.count; c++)
                            spawnList.Add(entry.bubbleConfig != null ? entry.bubbleConfig : fallbackBubbleConfig);
                    }
                }

                if (spawnList.Count == 0)
                    spawnList.Add(fallbackBubbleConfig);

                for (int i = 0; i < spawnList.Count; i++)
                {
                    float angle = (i / (float)spawnList.Count) * Mathf.PI * 2f;
                    float dist = spawnList.Count == 1 ? boardRadius * 0.25f : boardRadius * 0.32f;
                    Vector2 startPos = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);

                    JellyBubble bubble = Instantiate(bubblePrefab, entitiesContainer);
                    bubble.Initialize(spawnList[i], startPos);
                    _activeBubbles.Add(bubble);
                }
            }

            // 2. Спавн Greed-монеток в углах соты
            if (greedCoinPrefab != null && hexBoard.Polygon.Count >= 3)
            {
                int coinCount = _currentConfig != null ? _currentConfig.greedCoinsCount : 2;
                int coinValue = _currentConfig != null ? _currentConfig.coinValue : 5;
                int polyCount = hexBoard.Polygon.Count;

                for (int i = 0; i < coinCount; i++)
                {
                    int vertexIdx = (i * (polyCount / Mathf.Max(1, coinCount))) % polyCount;
                    Vector2 corner = hexBoard.Polygon[vertexIdx];
                    Vector2 coinPos = Vector2.Lerp(corner, hexBoard.Centroid, 0.24f);

                    GreedCoin coin = Instantiate(greedCoinPrefab, entitiesContainer);
                    coin.Initialize(coinPos, coinValue);
                    coin.OnCollectedByCut += HandleCoinCollected;
                    coin.OnDestroyedByBubble += HandleCoinDestroyed;
                    _activeCoins.Add(coin);
                }
            }
        }

        private void ClearEntities()
        {
            for (int i = 0; i < _activeBubbles.Count; i++)
            {
                if (_activeBubbles[i] != null)
                    Destroy(_activeBubbles[i].gameObject);
            }
            _activeBubbles.Clear();

            for (int i = 0; i < _activeCoins.Count; i++)
            {
                if (_activeCoins[i] != null)
                {
                    _activeCoins[i].OnCollectedByCut -= HandleCoinCollected;
                    _activeCoins[i].OnDestroyedByBubble -= HandleCoinDestroyed;
                    Destroy(_activeCoins[i].gameObject);
                }
            }
            _activeCoins.Clear();
        }

        private void Update()
        {
            if (hexBoard == null) return;

            float dt = Time.deltaTime;

            // Горячая клавиша Space для теста Хроно-стазиса до подключения UI-кнопки
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                TryActivateStasis();
#else
            if (Input.GetKeyDown(KeyCode.Space))
                TryActivateStasis();
#endif

            UpdateStasis(dt);
            UpdateBubblesAndCoins(dt);
            UpdateCameraShake(dt);
        }

        private void UpdateBubblesAndCoins(float dt)
        {
            float stasisMul = IsStasisActive ? stasisSpeedMultiplier : 1f;
            bool isCutting = sliceInput != null && sliceInput.IsCutting;
            Vector2 cutStart = sliceInput != null ? sliceInput.CutStart : Vector2.zero;
            Vector2 cutEnd = sliceInput != null ? sliceInput.CutCurrent : Vector2.zero;

            for (int i = 0; i < _activeBubbles.Count; i++)
            {
                JellyBubble b = _activeBubbles[i];
                if (b == null) continue;

                b.Tick(dt, stasisMul, hexBoard.Polygon, hexBoard.Centroid, isCutting, cutStart, cutEnd);

                for (int c = _activeCoins.Count - 1; c >= 0; c--)
                {
                    GreedCoin coin = _activeCoins[c];
                    if (coin != null && coin.IsActive)
                    {
                        coin.CheckBubbleCollision(b.Position, b.Radius);
                    }
                }
            }
        }

        private void HandleCutExecuted(Vector2 cutStart, Vector2 cutEnd)
        {
            if (hexBoard == null) return;

            if (PolygonMath.PointInPolygon(cutEnd, hexBoard.Polygon))
            {
                EmitToast("Рез не закончен: ведите насквозь за соту!");
                return;
            }

            SplitResult split = PolygonMath.SplitConvexPolygonStrictly(hexBoard.Polygon, cutStart, cutEnd);
            if (!split.success) return;

            float minBubbleDist = float.MaxValue;
            for (int i = 0; i < _activeBubbles.Count; i++)
            {
                JellyBubble b = _activeBubbles[i];
                if (b == null) continue;

                float d = PolygonMath.DistToSegment(b.Position, cutStart, cutEnd);
                if (d < minBubbleDist) minBubbleDist = d;

                if (d < b.Radius + bubbleHitBuffer)
                {
                    ApplyBubbleHitPenalty();
                    return;
                }
            }

            int inA = 0, inB = 0;
            for (int i = 0; i < _activeBubbles.Count; i++)
            {
                JellyBubble b = _activeBubbles[i];
                if (b == null) continue;

                if (PolygonMath.PointInPolygon(b.Position, split.polyA)) inA++;
                if (PolygonMath.PointInPolygon(b.Position, split.polyB)) inB++;
            }

            if (inA > 0 && inB > 0)
            {
                TriggerCameraShake(0.25f);
                EmitToast("Нельзя разделять капельки на разные части!");
                return;
            }

            List<Vector2> retained = inA > 0 ? split.polyA : split.polyB;
            List<Vector2> discarded = inA > 0 ? split.polyB : split.polyA;

            float discArea = PolygonMath.PolygonArea(discarded);
            if (discArea / hexBoard.InitialArea < 0.015f) return;

            // Проверяем сбор Greed-монеток в отсеченном секторе
            for (int i = _activeCoins.Count - 1; i >= 0; i--)
            {
                GreedCoin coin = _activeCoins[i];
                if (coin != null && coin.IsActive)
                {
                    coin.TryCollectIfInSlicedPolygon(discarded);
                }
            }

            // Проверяем Near-Miss (срез вплотную к капельке заряжает Хроно-стазис)
            float refRadius = _activeBubbles.Count > 0 && _activeBubbles[0] != null ? _activeBubbles[0].Radius : 0.42f;
            if (minBubbleDist <= refRadius + nearMissDistanceMargin)
            {
                StasisCharge = Mathf.Clamp01(StasisCharge + nearMissChargeGain);
                OnStasisChanged?.Invoke(StasisCharge, IsStasisActive, _stasisTimer);
                EmitToast($"NEAR-MISS! +{Mathf.RoundToInt(nearMissChargeGain * 100f)}% заряда Стазиса!");
            }

            hexBoard.ApplyCut(retained, discarded);

            if (hexBoard.CurrentShrinkPercent >= _targetShrink)
            {
                CompleteCurrentLevel();
            }
        }

        private void ApplyBubbleHitPenalty()
        {
            CurrentLives = Mathf.Max(0, CurrentLives - 1);
            OnLivesChanged?.Invoke(CurrentLives, MaxLives);
            TriggerCameraShake(0.35f);

            if (CurrentLives <= 0)
            {
                EmitToast("GAME OVER! Потеряны все 3 жизни — перезапуск уровня");
                OnGameOver?.Invoke(CurrentLevelIndex + 1);
                LoadLevel(CurrentLevelIndex);
                return;
            }

            float regenPct = hexBoard.RegeneratePenalty(3.5f, 7.0f);
            EmitToast($"Задета капелька (-1 жизнь)! Сота регенерировала на +{regenPct:F1}%");
        }

        private void CompleteCurrentLevel()
        {
            int reward = _currentConfig != null ? _currentConfig.levelClearCoinReward : 10;
            TotalCoins += reward;
            OnCoinsChanged?.Invoke(TotalCoins);

            if (_currentConfig == null || _currentConfig.grantsRelicShard)
            {
                RelicShards = Mathf.Min(3, RelicShards + 1);
            }

            int displayLevel = _currentConfig != null ? _currentConfig.levelIndex : (CurrentLevelIndex + 1);
            EmitToast($"УРОВЕНЬ {displayLevel} ПРОЙДЕН! +{reward} монет");
            OnLevelCompleted?.Invoke(displayLevel);

            LoadLevel(CurrentLevelIndex + 1);
        }

        public bool TryActivateStasis()
        {
            if (IsStasisActive || StasisCharge < 1f) return false;

            IsStasisActive = true;
            _stasisTimer = stasisDuration;
            StasisCharge = 0f;
            OnStasisChanged?.Invoke(StasisCharge, IsStasisActive, _stasisTimer);
            EmitToast("ХРОНО-СТАЗИС АКТИВИРОВАН (-65% скорости)!");
            return true;
        }

        private void UpdateStasis(float dt)
        {
            if (!IsStasisActive) return;

            _stasisTimer -= dt;
            if (_stasisTimer <= 0f)
            {
                _stasisTimer = 0f;
                IsStasisActive = false;
                EmitToast("Действие Хроно-стазиса завершено");
            }
            OnStasisChanged?.Invoke(StasisCharge, IsStasisActive, _stasisTimer);
        }

        private void HandleBoardShrinkChanged(float shrinkPct)
        {
            OnProgressChanged?.Invoke(shrinkPct, _targetShrink);
        }

        private void HandleCoinCollected(GreedCoin coin, int val)
        {
            _activeCoins.Remove(coin);
            TotalCoins += val;
            OnCoinsChanged?.Invoke(TotalCoins);
            EmitToast($"+{val} монет за смелый срез угла!");
        }

        private void HandleCoinDestroyed(GreedCoin coin)
        {
            _activeCoins.Remove(coin);
            EmitToast("Капелька разбила Greed-монетку!");
        }

        private void AdjustCameraToFitBoard(float boardRadius)
        {
            if (mainCamera == null) return;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float minHalfWidth = boardRadius * 1.42f;
            float orthoByWidth = minHalfWidth / Mathf.Max(0.25f, aspect);
            mainCamera.orthographicSize = Mathf.Max(5.2f, orthoByWidth);
        }

        private void TriggerCameraShake(float duration)
        {
            _shakeTimer = duration;
        }

        private void UpdateCameraShake(float dt)
        {
            if (mainCamera == null) return;
            if (_shakeTimer > 0f)
            {
                _shakeTimer -= dt;
                Vector2 offset = UnityEngine.Random.insideUnitCircle * 0.12f;
                mainCamera.transform.position = _camBasePos + new Vector3(offset.x, offset.y, 0f);
            }
            else
            {
                mainCamera.transform.position = _camBasePos;
            }
        }

        private void EmitToast(string msg)
        {
            Debug.Log($"[HexSlice] {msg}");
            OnToastMessage?.Invoke(msg);
        }
    }
}