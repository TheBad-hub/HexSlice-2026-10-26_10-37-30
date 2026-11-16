using System.Collections.Generic;
using UnityEngine;
using HexSlice.Math;

namespace HexSlice.Gameplay
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SlicedChunk : MonoBehaviour
    {
        [Header("Анимация разрушения")]
        [SerializeField] private float lifetime = 0.45f;
        [SerializeField] private float impulseSpeed = 4.2f;
        [SerializeField] private float rotationSpeed = 160f;
        [SerializeField] private float shrinkScaleSpeed = 1.8f;

        [Header("Неоновый контур осколка")]
        [SerializeField] private LineRenderer chunkOutline;
        [SerializeField] private Color outlineColor = new Color(0f, 0.94f, 1f, 1f);

        [Header("Частицы осколков")]
        [SerializeField] private ParticleSystem shatterParticles;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _instancedMat;

        private Vector2 _velocity;
        private float _rotSpeedSigned;
        private float _timer;

        private bool _hasColorProp;
        private int _colorPropId;
        private Color _baseColor = Color.white;
        private Color[] _initialColors;
        private Color[] _currentColors;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mesh = new Mesh { name = "SlicedChunkMesh" };
            _meshFilter.mesh = _mesh;

            if (chunkOutline == null)
            {
                chunkOutline = GetComponent<LineRenderer>();
                if (chunkOutline == null)
                    chunkOutline = gameObject.AddComponent<LineRenderer>();
            }

            SetupOutlineComponent();
        }

        private void SetupOutlineComponent()
        {
            if (chunkOutline == null) return;

            Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (s == null) s = Shader.Find("Sprites/Default");

            chunkOutline.material = new Material(s);
            chunkOutline.startWidth = 0.06f;
            chunkOutline.endWidth = 0.06f;
            chunkOutline.startColor = outlineColor;
            chunkOutline.endColor = outlineColor;
            chunkOutline.loop = true;
            chunkOutline.useWorldSpace = false;
            chunkOutline.sortingOrder = 3;
        }

        public void Initialize(List<Vector2> chunkPoly, Vector2 cutStart, Vector2 cutEnd, Material boardMat = null)
        {
            if (chunkPoly == null || chunkPoly.Count < 3)
            {
                Destroy(gameObject);
                return;
            }

            // Если материала нет у соты — создаем свой процедурный материал
            Material srcMat = boardMat != null ? boardMat : _meshRenderer.sharedMaterial;
            if (srcMat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (s == null) s = Shader.Find("Sprites/Default");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");

                srcMat = new Material(s)
                {
                    name = "M_SlicedChunk_Procedural",
                    color = new Color(0.14f, 0.18f, 0.35f, 0.95f)
                };
            }

            _instancedMat = new Material(srcMat);
            _meshRenderer.material = _instancedMat;
            SetupColorProperty(_instancedMat);

            Vector2 centroid = PolygonMath.GetCentroid(chunkPoly);
            transform.position = new Vector3(centroid.x, centroid.y, -0.05f);

            // Построение полигонального меша относительно центроида
            int n = chunkPoly.Count;
            Vector3[] verts = new Vector3[n + 1];
            Vector2[] uvs = new Vector2[n + 1];
            int[] tris = new int[n * 3];

            _initialColors = new Color[n + 1];
            _currentColors = new Color[n + 1];

            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            _initialColors[0] = Color.white;
            _currentColors[0] = Color.white;

            List<Vector3> outlinePts = new List<Vector3>();

            for (int i = 0; i < n; i++)
            {
                Vector2 localPt = chunkPoly[i] - centroid;
                verts[i + 1] = new Vector3(localPt.x, localPt.y, 0f);
                uvs[i + 1] = localPt * 0.25f + new Vector2(0.5f, 0.5f);

                _initialColors[i + 1] = Color.white;
                _currentColors[i + 1] = Color.white;

                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % n + 1;

                outlinePts.Add(new Vector3(localPt.x, localPt.y, -0.01f));
            }

            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.uv = uvs;
            _mesh.colors = _currentColors;
            _mesh.triangles = tris;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            // Контур куска (LineRenderer)
            if (chunkOutline != null)
            {
                chunkOutline.positionCount = outlinePts.Count;
                chunkOutline.SetPositions(outlinePts.ToArray());
            }

            // Импульс от линии среза наружу
            Vector2 cutDir = (cutEnd - cutStart).normalized;
            Vector2 cutNormal = new Vector2(-cutDir.y, cutDir.x);

            Vector2 midCut = (cutStart + cutEnd) * 0.5f;
            if (Vector2.Dot(cutNormal, centroid - midCut) < 0f)
                cutNormal = -cutNormal;

            _velocity = cutNormal * impulseSpeed + UnityEngine.Random.insideUnitCircle * 0.6f;
            _rotSpeedSigned = (UnityEngine.Random.value > 0.5f ? 1f : -1f) * rotationSpeed;
            _timer = lifetime;

            if (shatterParticles != null)
            {
                shatterParticles.transform.position = transform.position;
                shatterParticles.Play();
            }

            Destroy(gameObject, lifetime + 0.05f);
        }

        private void SetupColorProperty(Material mat)
        {
            if (mat == null) return;

            if (mat.HasProperty(BaseColorId))
            {
                _hasColorProp = true;
                _colorPropId = BaseColorId;
                _baseColor = mat.GetColor(BaseColorId);
            }
            else if (mat.HasProperty(ColorId))
            {
                _hasColorProp = true;
                _colorPropId = ColorId;
                _baseColor = mat.GetColor(ColorId);
            }
            else
            {
                _hasColorProp = false;
                _baseColor = Color.white;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _timer -= dt;

            // Движение, вращение и уменьшение
            transform.position += (Vector3)(_velocity * dt);
            transform.Rotate(0f, 0f, _rotSpeedSigned * dt);

            float scale = Mathf.Max(0.01f, transform.localScale.x - shrinkScaleSpeed * dt);
            transform.localScale = new Vector3(scale, scale, 1f);

            float alphaPct = lifetime > 0f ? Mathf.Clamp01(_timer / lifetime) : 0f;

            // Растворение меша через цвет вершин
            if (_currentColors != null && _mesh != null)
            {
                for (int i = 0; i < _currentColors.Length; i++)
                {
                    Color c = _initialColors[i];
                    c.a = _initialColors[i].a * alphaPct;
                    _currentColors[i] = c;
                }
                _mesh.colors = _currentColors;
            }

            // Растворение через материал
            if (_hasColorProp && _instancedMat != null)
            {
                Color c = _baseColor;
                c.a = _baseColor.a * alphaPct;
                _instancedMat.SetColor(_colorPropId, c);
            }

            // Растворение контура
            if (chunkOutline != null)
            {
                Color c = outlineColor;
                c.a = outlineColor.a * alphaPct;
                chunkOutline.startColor = c;
                chunkOutline.endColor = c;
            }
        }

        private void OnDestroy()
        {
            if (_instancedMat != null)
            {
                Destroy(_instancedMat);
            }
        }
    }
}