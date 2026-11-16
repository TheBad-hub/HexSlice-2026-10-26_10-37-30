using UnityEngine;

namespace HexSlice.Gameplay
{
    public class VFXManager : MonoBehaviour
    {
        public static VFXManager Instance { get; private set; }

        [Header("Префабы эффектов (Опционально)")]
        [Tooltip("Если префаб не назначен, эффект сгенерируется процедурно")]
        [SerializeField] private ParticleSystem sliceShatterPrefab;
        [SerializeField] private ParticleSystem coinCollectPrefab;
        [SerializeField] private ParticleSystem coinDestroyedPrefab;
        [SerializeField] private ParticleSystem bubbleBouncePrefab;
        [SerializeField] private ParticleSystem regenPenaltyPrefab;

        [Header("Палитра эффектов")]
        [SerializeField] private Color sliceCyanColor = new Color(0f, 0.94f, 1f, 1f);
        [SerializeField] private Color sliceMagentaColor = new Color(0.93f, 0.28f, 0.60f, 1f);
        [SerializeField] private Color coinGoldColor = new Color(1f, 0.82f, 0.12f, 1f);
        [SerializeField] private Color coinShatterColor = new Color(0.95f, 0.35f, 0.15f, 1f);
        [SerializeField] private Color bounceSparkColor = new Color(0.9f, 0.95f, 1f, 0.9f);
        [SerializeField] private Color regenWarningColor = new Color(0.98f, 0.60f, 0.08f, 1f);

        private Material _particleMat;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(gameObject);

            CreateParticleMaterial();
        }

        private void CreateParticleMaterial()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Particles/Standard Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");

            _particleMat = new Material(s);
        }

        /// <summary>
        /// Всплеск неоновых осколков при срезе соты
        /// </summary>
        public void PlaySliceShatter(Vector3 position, Vector2 cutNormal)
        {
            if (sliceShatterPrefab != null)
            {
                ParticleSystem ps = Instantiate(sliceShatterPrefab, position, Quaternion.identity);
                Destroy(ps.gameObject, ps.main.duration + 0.5f);
                return;
            }

            SpawnProceduralBurst(position, 22, 0.35f, 4.5f, 0.15f, sliceCyanColor, sliceMagentaColor, cutNormal);
        }

        /// <summary>
        /// Золотой взрыв при сборе Greed-монетки
        /// </summary>
        public void PlayCoinCollect(Vector3 position)
        {
            if (coinCollectPrefab != null)
            {
                ParticleSystem ps = Instantiate(coinCollectPrefab, position, Quaternion.identity);
                Destroy(ps.gameObject, ps.main.duration + 0.5f);
                return;
            }

            SpawnProceduralBurst(position, 18, 0.45f, 5.0f, 0.16f, coinGoldColor, Color.white, Vector2.zero);
        }

        /// <summary>
        /// Разрушение Greed-монетки при ударе капелькой (осколки золота и темный пепел)
        /// </summary>
        public void PlayCoinDestroyed(Vector3 position)
        {
            if (coinDestroyedPrefab != null)
            {
                ParticleSystem ps = Instantiate(coinDestroyedPrefab, position, Quaternion.identity);
                Destroy(ps.gameObject, ps.main.duration + 0.5f);
                return;
            }

            SpawnProceduralBurst(position, 14, 0.35f, 3.8f, 0.14f, coinShatterColor, new Color(0.35f, 0.35f, 0.4f, 1f), Vector2.zero);
        }

        /// <summary>
        /// Искры при отскоке капельки от стенки соты
        /// </summary>
        public void PlayBubbleBounce(Vector3 position, Vector2 wallNormal)
        {
            if (bubbleBouncePrefab != null)
            {
                ParticleSystem ps = Instantiate(bubbleBouncePrefab, position, Quaternion.identity);
                Destroy(ps.gameObject, ps.main.duration + 0.5f);
                return;
            }

            SpawnProceduralBurst(position, 6, 0.20f, 2.8f, 0.08f, bounceSparkColor, Color.white, wallNormal);
        }

        /// <summary>
        /// Штрафные оранжево-желтые искры при регенерации соты
        /// </summary>
        public void PlayRegenPenalty(Vector3 position)
        {
            if (regenPenaltyPrefab != null)
            {
                ParticleSystem ps = Instantiate(regenPenaltyPrefab, position, Quaternion.identity);
                Destroy(ps.gameObject, ps.main.duration + 0.5f);
                return;
            }

            SpawnProceduralBurst(position, 16, 0.40f, 4.0f, 0.14f, regenWarningColor, Color.red, Vector2.zero);
        }

        private void SpawnProceduralBurst(
            Vector3 center,
            int count,
            float lifetime,
            float speed,
            float startSize,
            Color colorA,
            Color colorB,
            Vector2 biasDirection)
        {
            GameObject go = new GameObject("VFX_ProceduralBurst");
            go.transform.position = new Vector3(center.x, center.y, -0.2f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
            psr.material = _particleMat;
            psr.sortingOrder = 15;

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = lifetime;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = startSize;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(colorA, 0.0f), new GradientColorKey(colorB, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
            );
            main.startColor = new ParticleSystem.MinMaxGradient(grad);

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, count) });

            var shape = ps.shape;
            if (biasDirection.sqrMagnitude > 0.01f)
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 45f;
                shape.radius = 0.05f;
                float rotAngle = Mathf.Atan2(biasDirection.y, biasDirection.x) * Mathf.Rad2Deg;
                go.transform.rotation = Quaternion.Euler(0f, 0f, rotAngle - 90f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.08f;
            }

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(1f, 0f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

            ps.Play();
            Destroy(go, lifetime + 0.1f);
        }
    }
}