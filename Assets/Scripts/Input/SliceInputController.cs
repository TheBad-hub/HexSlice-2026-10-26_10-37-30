using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using HexSlice.Math;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HexSlice.Gameplay
{
    public enum SliceBannerState
    {
        Hidden,
        Neutral,
        Cutting,
        Ready,
        Danger
    }

    public struct SliceBannerData
    {
        public SliceBannerState state;
        public string message;
        public float timeLeft;
    }

    public class SliceInputController : MonoBehaviour
    {
        [Header("Линия среза (LineRenderer)")]
        [SerializeField] private LineRenderer sliceLineRenderer;

        [Header("Параметры свайпа")]
        [SerializeField] private float maxCutDuration = 5.0f;
        [SerializeField] private float minSwipeWorldLength = 0.35f;
        [SerializeField] private float bubbleDangerBuffer = 0.08f;

        [Header("Цвета линии по состояниям")]
        [SerializeField] private Color cuttingColor = new Color(0.22f, 0.74f, 0.97f, 1f);
        [SerializeField] private Color readyColor = new Color(0.13f, 0.77f, 0.37f, 1f);
        [SerializeField] private Color dangerColor = new Color(0.94f, 0.27f, 0.27f, 1f);

        public bool IsCutting { get; private set; }
        public Vector2 CutStart { get; private set; }
        public Vector2 CutCurrent { get; private set; }

        public event Action<Vector2, Vector2> OnCutExecuted;
        public event Action<SliceBannerData> OnBannerChanged;
        public event Action<string> OnInputRejected;

        private Camera _cam;
        private HexBoard _board;
        private IReadOnlyList<JellyBubble> _bubbles;
        private float _cutTimer;
        private bool _inputEnabled = true;

        public void Initialize(Camera cam, HexBoard board, IReadOnlyList<JellyBubble> bubbles)
        {
            _cam = cam != null ? cam : Camera.main;
            _board = board;
            _bubbles = bubbles;
            CancelActiveCut();

            if (sliceLineRenderer != null)
            {
                sliceLineRenderer.useWorldSpace = true;
                sliceLineRenderer.loop = false;
                sliceLineRenderer.enabled = false;
            }
        }

        public void SetInputEnabled(bool enabled)
        {
            _inputEnabled = enabled;
            if (!enabled) CancelActiveCut();
        }

        public void CancelActiveCut()
        {
            IsCutting = false;
            if (sliceLineRenderer != null)
                sliceLineRenderer.enabled = false;
        }

        private void Update()
        {
            if (!_inputEnabled || _board == null || _cam == null) return;

            ReadPointerState(out bool down, out bool held, out bool up, out Vector2 screenPos);

            if (down)
            {
                if (IsPointerOverUI()) return;

                Vector2 worldPos = ScreenToWorld(screenPos);
                if (PolygonMath.PointInPolygon(worldPos, _board.Polygon))
                {
                    CancelActiveCut();
                    OnInputRejected?.Invoke("Начинайте свайп ЗА пределами соты!");
                    EmitBanner(SliceBannerState.Neutral, "Начинайте свайп ЗА пределами соты!", 0f);
                    return;
                }

                IsCutting = true;
                CutStart = worldPos;
                CutCurrent = worldPos;
                _cutTimer = maxCutDuration;

                if (sliceLineRenderer != null)
                {
                    sliceLineRenderer.enabled = true;
                    UpdateLinePositions();
                }
            }
            else if (held && IsCutting)
            {
                CutCurrent = ScreenToWorld(screenPos);
                _cutTimer -= Time.deltaTime;

                UpdateLinePositions();
                EvaluateLiveCutState();

                if (_cutTimer <= 0f)
                {
                    CancelActiveCut();
                    OnInputRejected?.Invoke("Время удержания истекло (5.0 с)");
                    EmitBanner(SliceBannerState.Hidden, string.Empty, 0f);
                }
            }
            else if (up && IsCutting)
            {
                CutCurrent = ScreenToWorld(screenPos);
                Vector2 start = CutStart;
                Vector2 end = CutCurrent;

                CancelActiveCut();
                EmitBanner(SliceBannerState.Hidden, string.Empty, 0f);

                if ((end - start).magnitude >= minSwipeWorldLength)
                {
                    OnCutExecuted?.Invoke(start, end);
                }
            }
        }

        private void EvaluateLiveCutState()
        {
            bool danger = false;
            if (_bubbles != null)
            {
                for (int i = 0; i < _bubbles.Count; i++)
                {
                    JellyBubble b = _bubbles[i];
                    if (b == null) continue;
                    if (PolygonMath.DistToSegment(b.Position, CutStart, CutCurrent) < b.Radius + bubbleDangerBuffer)
                    {
                        danger = true;
                        break;
                    }
                }
            }

            if (danger)
            {
                SetLineColor(dangerColor);
                EmitBanner(SliceBannerState.Danger, "ОПАСНОСТЬ: ШАРИК НА ЛИНИИ!", _cutTimer);
                return;
            }

            if (PolygonMath.PointInPolygon(CutCurrent, _board.Polygon))
            {
                SetLineColor(cuttingColor);
                EmitBanner(SliceBannerState.Cutting, "Ведите насквозь за край соты", _cutTimer);
                return;
            }

            SplitResult preview = PolygonMath.SplitConvexPolygonStrictly(_board.Polygon, CutStart, CutCurrent);
            if (preview.success)
            {
                SetLineColor(readyColor);
                EmitBanner(SliceBannerState.Ready, "ОТПУСКАЙТЕ! Срез готов", _cutTimer);
            }
            else
            {
                SetLineColor(cuttingColor);
                EmitBanner(SliceBannerState.Neutral, "Ведите насквозь через соту", _cutTimer);
            }
        }

        private void UpdateLinePositions()
        {
            if (sliceLineRenderer == null) return;
            sliceLineRenderer.positionCount = 2;
            sliceLineRenderer.SetPosition(0, new Vector3(CutStart.x, CutStart.y, -0.15f));
            sliceLineRenderer.SetPosition(1, new Vector3(CutCurrent.x, CutCurrent.y, -0.15f));
        }

        private void SetLineColor(Color col)
        {
            if (sliceLineRenderer == null) return;
            sliceLineRenderer.startColor = col;
            sliceLineRenderer.endColor = col;
        }

        private void EmitBanner(SliceBannerState state, string msg, float timeLeft)
        {
            OnBannerChanged?.Invoke(new SliceBannerData
            {
                state = state,
                message = msg,
                timeLeft = timeLeft
            });
        }

        private Vector2 ScreenToWorld(Vector2 screenPos)
        {
            float zDist = Mathf.Abs(_cam.transform.position.z);
            Vector3 w = _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, zDist));
            return new Vector2(w.x, w.y);
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static void ReadPointerState(out bool down, out bool held, out bool up, out Vector2 screenPos)
        {
            down = false;
            held = false;
            up = false;
            screenPos = Vector2.zero;

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
        }
    }
}