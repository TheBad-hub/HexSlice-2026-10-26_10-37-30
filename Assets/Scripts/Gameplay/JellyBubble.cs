using System;
using System.Collections.Generic;
using UnityEngine;
using HexSlice.Data;
using HexSlice.Math;

namespace HexSlice.Gameplay
{
    public class JellyBubble : MonoBehaviour
    {
        [Header("Слои визуала (SpriteRenderer из Figma)")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer glossRenderer;
        [SerializeField] private SpriteRenderer shadowRenderer;
        [SerializeField] private SpriteRenderer panicSweatRenderer;

        [Header("Левый глаз и зрачок")]
        [SerializeField] private SpriteRenderer leftEyeRenderer;
        [SerializeField] private SpriteRenderer leftPupilRenderer;

        [Header("Правый глаз и зрачок")]
        [SerializeField] private SpriteRenderer rightEyeRenderer;
        [SerializeField] private SpriteRenderer rightPupilRenderer;

        [Header("Движение зрачков")]
        [Tooltip("Максимальный сдвиг зрачка внутри глаза при движении")]
        [SerializeField] private float maxPupilShift = 0.05f;

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

        private Vector3 _bodyInitialLocalScale = Vector3.one;
        private Vector3 _leftPupilInitialLocalPos = Vector3.zero;
        private Vector3 _rightPupilInitialLocalPos = Vector3.zero;

        private void Awake()
        {
            if (bodyRenderer != null)
                _bodyInitialLocalScale = bodyRenderer.transform.localScale;

            if (leftPupilRenderer != null)
                _leftPupilInitialLocalPos = leftPupilRenderer.transform.localPosition;

            if (rightPupilRenderer != null)
                _rightPupilInitialLocalPos = rightPupilRenderer.transform.localPosition;
        }

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

            ApplySkinSprites();
            FitScaleToPhysicalRadius();
        }

        private void ApplySkinSprites()
        {
            if (_config == null) return;

            if (bodyRenderer != null)
            {
                if (_config.bodySprite != null) bodyRenderer.sprite = _config.bodySprite;
                bodyRenderer.color = _config.bodyTint;
            }

            if (glossRenderer != null && _config.glossSprite != null)
                glossRenderer.sprite = _config.glossSprite;

            if (shadowRenderer != null && _config.shadowSprite != null)
                shadowRenderer.sprite = _config.shadowSprite;

            // Левый глаз и зрачок
            if (leftEyeRenderer != null && _config.leftEyeSprite != null)
                leftEyeRenderer.sprite = _config.leftEyeSprite;

            if (leftPupilRenderer != null)
            {
                if (_config.leftPupilSprite != null)
                    leftPupilRenderer.sprite = _config.leftPupilSprite;

                _leftPupilInitialLocalPos = _config.leftPupilOffset;
                leftPupilRenderer.transform.localPosition = _leftPupilInitialLocalPos;
                leftPupilRenderer.transform.localScale = Vector3.one * _config.pupilScale;
            }

            // Правый глаз и зрачок
            if (rightEyeRenderer != null && _config.rightEyeSprite != null)
                rightEyeRenderer.sprite = _config.rightEyeSprite;

            if (rightPupilRenderer != null)
            {
                if (_config.rightPupilSprite != null)
                    rightPupilRenderer.sprite = _config.rightPupilSprite;

                _rightPupilInitialLocalPos = _config.rightPupilOffset;
                rightPupilRenderer.transform.localPosition = _rightPupilInitialLocalPos;
                rightPupilRenderer.transform.localScale = Vector3.one * _config.pupilScale;
            }

            // Капля пота при панике
            if (panicSweatRenderer != null)
            {
                if (_config.sweatDropSprite != null) panicSweatRenderer.sprite = _config.sweatDropSprite;
                panicSweatRenderer.enabled = false;
            }
        }

        private void FitScaleToPhysicalRadius()
        {
            // Масштабируем весь корень префаба так, чтобы видимый спрайт тела идеально совпал с физическим диаметром соты
            if (bodyRenderer != null && bodyRenderer.sprite != null)
            {
                float spriteWidth = Mathf.Max(0.01f, bodyRenderer.sprite.bounds.size.x);
                float targetDiameter = Radius * 2f;
                float rootScale = targetDiameter / spriteWidth;
                transform.localScale = new Vector3(rootScale, rootScale, 1f);
            }
            else
            {
                transform.localScale = Vector3.one * (Radius * 2f);
            }
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

                        // Добавь при ударе:
                        if (VFXManager.Instance != null)
                            VFXManager.Instance.PlayBubbleBounce(Position, normal);

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

            // Деформируем тело капли вдоль нормали удара о соту
            if (bodyRenderer != null)
            {
                bodyRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, _squashAngle);
                bodyRenderer.transform.localScale = new Vector3(_bodyInitialLocalScale.x * sx, _bodyInitialLocalScale.y * sy, 1f);
            }

            // Блик сверху не крутится вместе с телом, сохраняя направление света
            if (glossRenderer != null)
            {
                glossRenderer.transform.rotation = Quaternion.identity;
            }

            // Зрачки плавно смотрят в сторону вектора полета
            Vector2 dir = Velocity.sqrMagnitude > 1e-4f ? Velocity.normalized : Vector2.right;
            Vector3 pupilShift = new Vector3(dir.x * maxPupilShift, dir.y * maxPupilShift, 0f);

            if (leftPupilRenderer != null)
                leftPupilRenderer.transform.localPosition = _leftPupilInitialLocalPos + pupilShift;

            if (rightPupilRenderer != null)
                rightPupilRenderer.transform.localPosition = _rightPupilInitialLocalPos + pupilShift;

            // Моргание: сплющивание глаз по оси Y
            float eyeScaleY = _isBlinking ? 0.1f : (IsPanicked ? 1.25f : 1f);
            if (leftEyeRenderer != null)
            {
                Vector3 s = leftEyeRenderer.transform.localScale;
                leftEyeRenderer.transform.localScale = new Vector3(s.x, eyeScaleY, s.z);
            }
            if (rightEyeRenderer != null)
            {
                Vector3 s = rightEyeRenderer.transform.localScale;
                rightEyeRenderer.transform.localScale = new Vector3(s.x, eyeScaleY, s.z);
            }
        }
    }
}