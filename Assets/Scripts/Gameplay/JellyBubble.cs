using System;
using System.Collections.Generic;
using UnityEngine;
using HexSlice.Data;
using HexSlice.Math;

namespace HexSlice.Gameplay
{
    public class JellyBubble : MonoBehaviour
    {
        [Header("Слои префаба (SpriteRenderer)")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer glossRenderer;
        [SerializeField] private SpriteRenderer shadowRenderer;
        [SerializeField] private SpriteRenderer panicSweatRenderer;

        [Header("Глаза и Зрачки (Transform / SpriteRenderer)")]
        [SerializeField] private Transform eyesContainer;
        [SerializeField] private SpriteRenderer leftEyeRenderer;
        [SerializeField] private SpriteRenderer rightEyeRenderer;
        [SerializeField] private SpriteRenderer leftPupilRenderer;
        [SerializeField] private SpriteRenderer rightPupilRenderer;

        [Header("Пропорции слоев относительно диаметра капельки")]
        [SerializeField] private float eyeSizeRatio = 0.28f;
        [SerializeField] private float pupilSizeRatio = 0.52f;
        [SerializeField] private float eyeSpacingRatio = 0.30f;
        [SerializeField] private float eyeHeightOffsetRatio = 0.10f;

        [Header("Эффекты")]
        [SerializeField] private ParticleSystem bounceVfx;

        public Vector2 Position { get; private set; }
        public Vector2 Velocity { get; private set; }
        public float Radius { get; private set; }
        public bool IsPanicked { get; private set; }

        public event Action<Vector2> OnWallBounced;

        private BubbleConfigSO _config;
        private float _wobblePhase;
        private float _squash = 1f;
        private float _squashAngle;
        private float _blinkTimer;
        private bool _isBlinking;
        private float _behaviorTimer;
        private float _currentSpeedMultiplier = 1f;

        private Vector3 _bodyBaseScale = Vector3.one;
        private Vector3 _glossBaseScale = Vector3.one;
        private Vector3 _leftEyeBaseScale = Vector3.one;
        private Vector3 _rightEyeBaseScale = Vector3.one;

        public void Initialize(BubbleConfigSO config, Vector2 startPos)
        {
            _config = config;
            Position = startPos;
            Radius = config != null ? config.radius : 0.42f;
            float speed = config != null ? config.baseSpeed : 3.2f;

            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;

            _wobblePhase = UnityEngine.Random.Range(0f, 10f);
            _blinkTimer = UnityEngine.Random.Range(1.5f, 3.5f);
            transform.position = new Vector3(Position.x, Position.y, 0f);
            transform.localScale = Vector3.one;

            ApplySkinAndNormalizeSizes();
        }

        private void ApplySkinAndNormalizeSizes()
        {
            float targetDiameter = Radius * 2f;

            if (bodyRenderer != null)
            {
                if (_config != null && _config.bodySprite != null)
                    bodyRenderer.sprite = _config.bodySprite;
                if (_config != null)
                    bodyRenderer.color = _config.bodyTint;

                bodyRenderer.transform.localPosition = Vector3.zero;
                _bodyBaseScale = CalcScaleForWorldSize(bodyRenderer.sprite, targetDiameter);
                bodyRenderer.transform.localScale = _bodyBaseScale;
            }

            if (glossRenderer != null)
            {
                if (_config != null && _config.glossSprite != null)
                    glossRenderer.sprite = _config.glossSprite;

                glossRenderer.transform.localPosition = Vector3.zero;
                _glossBaseScale = CalcScaleForWorldSize(glossRenderer.sprite, targetDiameter * 0.92f);
                glossRenderer.transform.localScale = _glossBaseScale;
            }

            if (shadowRenderer != null)
            {
                if (_config != null && _config.shadowSprite != null)
                    shadowRenderer.sprite = _config.shadowSprite;

                shadowRenderer.transform.localPosition = new Vector3(0f, -Radius * 0.1f, 0f);
                shadowRenderer.transform.localScale = CalcScaleForWorldSize(shadowRenderer.sprite, targetDiameter * 1.08f);
            }

            // Выставляем глаза и зрачки пропорционально радиусу капельки
            float eyeWorldSize = targetDiameter * eyeSizeRatio;
            float eyeX = Radius * eyeSpacingRatio;
            float eyeY = Radius * eyeHeightOffsetRatio;

            if (leftEyeRenderer != null)
            {
                if (_config != null && _config.eyeWhiteSprite != null)
                    leftEyeRenderer.sprite = _config.eyeWhiteSprite;

                leftEyeRenderer.transform.localPosition = new Vector3(-eyeX, eyeY, 0f);
                _leftEyeBaseScale = CalcScaleForWorldSize(leftEyeRenderer.sprite, eyeWorldSize);
                leftEyeRenderer.transform.localScale = _leftEyeBaseScale;
            }

            if (rightEyeRenderer != null)
            {
                if (_config != null && _config.eyeWhiteSprite != null)
                    rightEyeRenderer.sprite = _config.eyeWhiteSprite;

                rightEyeRenderer.transform.localPosition = new Vector3(eyeX, eyeY, 0f);
                _rightEyeBaseScale = CalcScaleForWorldSize(rightEyeRenderer.sprite, eyeWorldSize);
                rightEyeRenderer.transform.localScale = _rightEyeBaseScale;
            }

            SetupPupil(leftPupilRenderer, leftEyeRenderer, eyeWorldSize * pupilSizeRatio);
            SetupPupil(rightPupilRenderer, rightEyeRenderer, eyeWorldSize * pupilSizeRatio);

            if (panicSweatRenderer != null)
            {
                if (_config != null && _config.sweatDropSprite != null)
                    panicSweatRenderer.sprite = _config.sweatDropSprite;

                panicSweatRenderer.transform.localPosition = new Vector3(Radius * 0.75f, Radius * 0.55f, 0f);
                panicSweatRenderer.transform.localScale = CalcScaleForWorldSize(panicSweatRenderer.sprite, targetDiameter * 0.22f);
                panicSweatRenderer.enabled = false;
            }
        }

        private void SetupPupil(SpriteRenderer pupilSr, SpriteRenderer parentEyeSr, float targetPupilWorldSize)
        {
            if (pupilSr == null) return;
            if (_config != null && _config.pupilSprite != null)
                pupilSr.sprite = _config.pupilSprite;

            // Если зрачок оставили чисто белым в префабе, делаем его темно-синим, чтобы он читался на белке глаза
            if (pupilSr.color.r > 0.9f && pupilSr.color.g > 0.9f && pupilSr.color.b > 0.9f)
                pupilSr.color = new Color(0.06f, 0.09f, 0.16f, 1f);

            float parentScaleX = parentEyeSr != null ? Mathf.Max(0.001f, parentEyeSr.transform.localScale.x) : 1f;
            Vector3 worldScale = CalcScaleForWorldSize(pupilSr.sprite, targetPupilWorldSize);
            pupilSr.transform.localScale = worldScale / parentScaleX;
            pupilSr.transform.localPosition = Vector3.zero;
        }

        private static Vector3 CalcScaleForWorldSize(Sprite sprite, float targetWorldDiameter)
        {
            if (sprite == null) return Vector3.one * targetWorldDiameter;
            float spriteWidth = Mathf.Max(0.001f, sprite.bounds.size.x);
            float scale = targetWorldDiameter / spriteWidth;
            return new Vector3(scale, scale, 1f);
        }

        public void Tick(
            float dt,
            float globalStasisMultiplier,
            IReadOnlyList<Vector2> polygon,
            Vector2 polyCentroid,
            bool isCutting,
            Vector2 cutStart,
            Vector2 cutEnd)
        {
            if (polygon == null || polygon.Count < 3) return;

            UpdateSpecialBehavior(dt);

            float totalSpeedMul = globalStasisMultiplier * _currentSpeedMultiplier;
            float wobbleSpd = _config != null ? _config.idleWobbleSpeed : 6f;
            float recoverySpd = _config != null ? _config.squashRecoverySpeed : 9f;

            _wobblePhase += dt * wobbleSpd * totalSpeedMul;
            _squash = Mathf.Lerp(_squash, 1f, dt * recoverySpd);

            IsPanicked = isCutting && PolygonMath.DistToSegment(Position, cutStart, cutEnd) < Radius + 0.65f;
            if (panicSweatRenderer != null)
                panicSweatRenderer.enabled = IsPanicked;

            UpdateBlinking(dt);

            Position += Velocity * (totalSpeedMul * dt);
            ResolvePolygonCollisions(polygon, polyCentroid);

            UpdateVisualTransforms();
        }

        private void UpdateSpecialBehavior(float dt)
        {
            if (_config == null)
            {
                _currentSpeedMultiplier = 1f;
                return;
            }

            switch (_config.behavior)
            {
                case BubbleBehaviorType.Standard:
                case BubbleBehaviorType.HeavySlow:
                    _currentSpeedMultiplier = 1f;
                    break;

                case BubbleBehaviorType.ImpulseDash:
                    _behaviorTimer += dt;
                    float period = Mathf.Max(0.3f, _config.dashPeriod);
                    float phase = (_behaviorTimer % period) / period;
                    _currentSpeedMultiplier = phase < 0.28f ? 1.85f : 0.55f;
                    break;

                case BubbleBehaviorType.WallBoost:
                    _currentSpeedMultiplier = Mathf.Lerp(_currentSpeedMultiplier, 1f, dt * 3.5f);
                    break;
            }
        }

        private void ResolvePolygonCollisions(IReadOnlyList<Vector2> polygon, Vector2 polyCentroid)
        {
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p1 = polygon[i];
                Vector2 p2 = polygon[(i + 1) % n];
                Vector2 edge = p2 - p1;
                float len = edge.magnitude;
                if (len < 1e-5f) continue;

                Vector2 normal = new Vector2(-edge.y / len, edge.x / len);
                Vector2 mid = (p1 + p2) * 0.5f;
                if (Vector2.Dot(normal, polyCentroid - mid) < 0f)
                    normal = -normal;

                float d = PolygonMath.DistToSegment(Position, p1, p2);
                if (d < Radius)
                {
                    float dot = Vector2.Dot(Velocity, normal);
                    if (dot < 0f)
                    {
                        Velocity -= 2f * dot * normal;
                        Position += normal * (Radius - d);

                        _squash = _config != null ? _config.impactSquash : 0.65f;
                        _squashAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;

                        if (_config != null && _config.behavior == BubbleBehaviorType.WallBoost)
                            _currentSpeedMultiplier = _config.wallBoostMultiplier;

                        if (bounceVfx != null) bounceVfx.Play();
                        OnWallBounced?.Invoke(Position);
                    }
                }
            }
        }

        private void UpdateBlinking(float dt)
        {
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _isBlinking = !_isBlinking;
                _blinkTimer = _isBlinking ? 0.11f : UnityEngine.Random.Range(1.8f, 3.8f);
            }
        }

        private void UpdateVisualTransforms()
        {
            transform.position = new Vector3(Position.x, Position.y, 0f);

            float wobbleAmp = _config != null ? _config.idleWobbleAmount : 0.045f;
            if (IsPanicked) wobbleAmp *= 2f;

            float wobble = Mathf.Sin(_wobblePhase) * wobbleAmp;
            float sx = _squash + wobble;
            float sy = (1f / Mathf.Max(0.45f, _squash)) - wobble;

            if (bodyRenderer != null)
            {
                bodyRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, _squashAngle);
                bodyRenderer.transform.localScale = new Vector3(_bodyBaseScale.x * sx, _bodyBaseScale.y * sy, 1f);
            }

            if (glossRenderer != null)
            {
                glossRenderer.transform.rotation = Quaternion.identity;
                glossRenderer.transform.localScale = new Vector3(_glossBaseScale.x * sx, _glossBaseScale.y * sy, 1f);
            }

            Vector2 dir = Velocity.sqrMagnitude > 1e-4f ? Velocity.normalized : Vector2.right;
            if (eyesContainer != null)
            {
                eyesContainer.localPosition = new Vector3(dir.x * (Radius * 0.12f), dir.y * (Radius * 0.12f), 0f);
            }

            Vector3 pupilLocalOffset = new Vector3(dir.x * 0.18f, dir.y * 0.18f, 0f);
            if (leftPupilRenderer != null) leftPupilRenderer.transform.localPosition = pupilLocalOffset;
            if (rightPupilRenderer != null) rightPupilRenderer.transform.localPosition = pupilLocalOffset;

            float eyeYMul = _isBlinking ? 0.12f : (IsPanicked ? 1.22f : 1f);
            if (leftEyeRenderer != null)
                leftEyeRenderer.transform.localScale = new Vector3(_leftEyeBaseScale.x, _leftEyeBaseScale.y * eyeYMul, 1f);
            if (rightEyeRenderer != null)
                rightEyeRenderer.transform.localScale = new Vector3(_rightEyeBaseScale.x, _rightEyeBaseScale.y * eyeYMul, 1f);
        }
    }
}