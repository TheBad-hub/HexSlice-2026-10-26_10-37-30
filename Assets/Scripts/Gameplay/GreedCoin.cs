using System;
using System.Collections.Generic;
using UnityEngine;
using HexSlice.Math;

namespace HexSlice.Gameplay
{
    public class GreedCoin : MonoBehaviour
    {
        [Header("Визуал и Эффекты")]
        [SerializeField] private SpriteRenderer coinRenderer;
        [SerializeField] private ParticleSystem collectVfx;
        [SerializeField] private ParticleSystem shatterVfx;

        [Header("Параметры")]
        [SerializeField] private float hitRadius = 0.24f;
        [SerializeField] private float pulseSpeed = 4.5f;
        [SerializeField] private float pulseAmount = 0.08f;

        public Vector2 Position { get; private set; }
        public int Value { get; private set; } = 5;
        public bool IsActive { get; private set; }

        public event Action<GreedCoin, int> OnCollectedByCut;
        public event Action<GreedCoin> OnDestroyedByBubble;

        private Vector3 _baseScale;
        private float _pulsePhase;

        private void Awake()
        {
            _baseScale = transform.localScale;
            _pulsePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        }

        public void Initialize(Vector2 worldPos, int coinValue)
        {
            Position = worldPos;
            Value = coinValue;
            IsActive = true;
            transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            if (coinRenderer != null) coinRenderer.enabled = true;
        }

        private void Update()
        {
            if (!IsActive) return;

            _pulsePhase += Time.deltaTime * pulseSpeed;
            float scaleMul = 1f + Mathf.Sin(_pulsePhase) * pulseAmount;
            transform.localScale = _baseScale * scaleMul;
        }

        public bool TryCollectIfInSlicedPolygon(IReadOnlyList<Vector2> discardedPoly)
        {
            if (!IsActive || discardedPoly == null) return false;

            if (PolygonMath.PointInPolygon(Position, discardedPoly))
            {
                IsActive = false;
                if (coinRenderer != null) coinRenderer.enabled = false;
                if (collectVfx != null) collectVfx.Play();

                OnCollectedByCut?.Invoke(this, Value);
                Destroy(gameObject, collectVfx != null ? 1.0f : 0.05f);
                return true;
            }
            return false;
        }

        public bool CheckBubbleCollision(Vector2 bubblePos, float bubbleRadius)
        {
            if (!IsActive) return false;

            if (Vector2.Distance(Position, bubblePos) <= hitRadius + bubbleRadius)
            {
                IsActive = false;
                if (coinRenderer != null) coinRenderer.enabled = false;
                if (shatterVfx != null) shatterVfx.Play();

                OnDestroyedByBubble?.Invoke(this);
                Destroy(gameObject, shatterVfx != null ? 1.0f : 0.05f);
                return true;
            }
            return false;
        }
    }
}