using System;
using System.Collections.Generic;
using UnityEngine;
using HexSlice.Data;
using HexSlice.Math;

namespace HexSlice.Gameplay
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class HexBoard : MonoBehaviour
    {
        [Header("Контуры соты (LineRenderer)")]
        [SerializeField] private LineRenderer activeBorderLine;
        [SerializeField] private LineRenderer ghostBorderLine;
        [SerializeField] private EdgeCollider2D borderCollider;

        [Header("Настройки по умолчанию")]
        [SerializeField] private float defaultRadius = 3.2f;
        [SerializeField] private float defaultUvTiling = 0.25f;

        public EdgeCollider2D BorderCollider => borderCollider;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;

        private readonly List<Vector2> _origHexagon = new List<Vector2>();
        private List<Vector2> _polygon = new List<Vector2>();

        public IReadOnlyList<Vector2> Polygon => _polygon;
        public Vector2 Centroid { get; private set; }
        public float BaseRadius { get; private set; }
        public float InitialArea { get; private set; }
        public float CurrentArea { get; private set; }
        public float CurrentShrinkPercent { get; private set; }

        public event Action<float> OnShrinkChanged;
        public event Action<List<Vector2>> OnChunkSliced;

        private float _uvTiling;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mesh = new Mesh { name = "HexBoardDynamicMesh" };
            _meshFilter.mesh = _mesh;

            if (borderCollider == null && activeBorderLine != null)
                borderCollider = activeBorderLine.GetComponent<EdgeCollider2D>();

            EnsureDefaultMaterial();
        }

        private void EnsureDefaultMaterial()
        {
            if (_meshRenderer.sharedMaterial == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (s == null) s = Shader.Find("Sprites/Default");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");

                Material defaultMat = new Material(s)
                {
                    name = "M_HexBoard_AutoDefault",
                    color = new Color(0.06f, 0.08f, 0.16f, 0.95f)
                };
                _meshRenderer.material = defaultMat;
            }
        }

        public void Initialize(LevelConfigSO levelConfig = null)
        {
            BaseRadius = levelConfig != null ? levelConfig.hexRadius : defaultRadius;
            _uvTiling = levelConfig != null ? levelConfig.uvTiling : defaultUvTiling;

            if (levelConfig != null && levelConfig.boardFillMaterial != null)
            {
                _meshRenderer.material = levelConfig.boardFillMaterial;
            }
            else
            {
                EnsureDefaultMaterial();
            }

            if (levelConfig != null)
            {
                if (activeBorderLine != null)
                {
                    activeBorderLine.startColor = levelConfig.activeBorderColor;
                    activeBorderLine.endColor = levelConfig.activeBorderColor;
                }

                if (ghostBorderLine != null)
                {
                    ghostBorderLine.startColor = levelConfig.ghostBorderColor;
                    ghostBorderLine.endColor = levelConfig.ghostBorderColor;
                }
            }

            _origHexagon.Clear();
            _polygon.Clear();

            // Координаты полигона строго привязаны к позиции объекта HexBoard в мировом пространстве
            Vector2 origin = transform.position;

            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f + Mathf.PI / 6f;
                Vector2 pt = origin + new Vector2(BaseRadius * Mathf.Cos(angle), BaseRadius * Mathf.Sin(angle));
                _origHexagon.Add(pt);
                _polygon.Add(pt);
            }

            InitialArea = PolygonMath.PolygonArea(_polygon);
            UpdateLineRenderer(ghostBorderLine, _origHexagon);
            RebuildBoard();
        }

        public void ApplyCut(List<Vector2> retainedPoly, List<Vector2> discardedPoly)
        {
            _polygon = retainedPoly;
            RebuildBoard();
            OnChunkSliced?.Invoke(discardedPoly);
        }

        public float RegeneratePenalty(float minPct = 3.5f, float maxPct = 7.0f)
        {
            float sCurr = PolygonMath.PolygonArea(_polygon);
            float targetPct = UnityEngine.Random.Range(minPct, maxPct);
            float targetDelta = InitialArea * (targetPct / 100f);

            int n = _polygon.Count;
            float maxHexMidDist = BaseRadius * 0.866f;
            Vector2 origin = transform.position;
            List<(Vector2 mid, Vector2 normal)> cutEdges = new List<(Vector2, Vector2)>();

            for (int i = 0; i < n; i++)
            {
                Vector2 p1 = _polygon[i];
                Vector2 p2 = _polygon[(i + 1) % n];
                Vector2 mid = (p1 + p2) * 0.5f;
                float deficit = maxHexMidDist - (mid - origin).magnitude;

                Vector2 edge = p2 - p1;
                if (edge.sqrMagnitude < 1e-5f) continue;

                Vector2 normal = new Vector2(-edge.y, edge.x).normalized;
                if ((mid + normal * 0.1f - origin).sqrMagnitude < (mid - origin).sqrMagnitude)
                    normal = -normal;

                if (deficit > 0.08f)
                    cutEdges.Add((mid, normal));
            }

            if (cutEdges.Count == 0) return 0f;

            var chosen = cutEdges[UnityEngine.Random.Range(0, cutEdges.Count)];
            float low = 0.05f, high = BaseRadius * 1.4f;
            List<Vector2> bestPoly = _polygon;
            float bestDelta = 0f;

            for (int iter = 0; iter < 10; iter++)
            {
                float midDist = (low + high) * 0.5f;
                Vector2 testPt = chosen.mid + chosen.normal * midDist;
                if ((testPt - origin).magnitude > BaseRadius * 0.96f)
                    testPt = origin + (testPt - origin).normalized * (BaseRadius * 0.96f);

                List<Vector2> candidatePts = new List<Vector2>(_polygon) { testPt };
                List<Vector2> hull = PolygonMath.ConvexHull(candidatePts);
                float delta = PolygonMath.PolygonArea(hull) - sCurr;

                bestPoly = hull;
                bestDelta = delta;

                if (delta < targetDelta) low = midDist;
                else high = midDist;
            }

            _polygon = bestPoly;
            RebuildBoard();
            return Mathf.Clamp((bestDelta / InitialArea) * 100f, minPct, maxPct);
        }

        private void RebuildBoard()
        {
            Centroid = PolygonMath.GetCentroid(_polygon);
            CurrentArea = PolygonMath.PolygonArea(_polygon);
            CurrentShrinkPercent = InitialArea > 1e-4f
                ? Mathf.Clamp((1f - CurrentArea / InitialArea) * 100f, 0f, 100f)
                : 0f;

            Vector2 origin = transform.position;
            int n = _polygon.Count;
            Vector3[] verts = new Vector3[n + 1];
            Vector2[] uvs = new Vector2[n + 1];
            Color[] cols = new Color[n + 1];
            int[] tris = new int[n * 3];

            // Вершины меша строятся в локальных координатах HexBoard
            verts[0] = new Vector3(Centroid.x - origin.x, Centroid.y - origin.y, 0f);
            uvs[0] = Centroid * _uvTiling + new Vector2(0.5f, 0.5f);
            cols[0] = new Color(0.12f, 0.15f, 0.28f, 0.95f);

            for (int i = 0; i < n; i++)
            {
                Vector2 pt = _polygon[i];
                verts[i + 1] = new Vector3(pt.x - origin.x, pt.y - origin.y, 0f);
                uvs[i + 1] = pt * _uvTiling + new Vector2(0.5f, 0.5f);
                cols[i + 1] = new Color(0.06f, 0.08f, 0.16f, 0.95f);

                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % n + 1;
            }

            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.uv = uvs;
            _mesh.colors = cols;
            _mesh.triangles = tris;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            UpdateLineRenderer(activeBorderLine, _polygon);

            // Обновляем EdgeCollider2D соты (в локальных координатах объекта ActiveBorder)
            if (borderCollider != null && _polygon != null && _polygon.Count >= 3)
            {
                List<Vector2> edgePoints = new List<Vector2>();
                for (int i = 0; i < _polygon.Count; i++)
                    edgePoints.Add(_polygon[i] - origin);
                edgePoints.Add(_polygon[0] - origin);
                borderCollider.SetPoints(edgePoints);
            }

            OnShrinkChanged?.Invoke(CurrentShrinkPercent);
        }

        private void UpdateLineRenderer(LineRenderer lr, IReadOnlyList<Vector2> pts)
        {
            if (lr == null || pts == null) return;

            lr.useWorldSpace = false;
            lr.transform.localPosition = Vector3.zero;
            lr.transform.localRotation = Quaternion.identity;
            lr.transform.localScale = Vector3.one;

            lr.loop = true;
            lr.positionCount = pts.Count;
            Vector2 origin = transform.position;

            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 localPt = pts[i] - origin;
                lr.SetPosition(i, new Vector3(localPt.x, localPt.y, -0.02f));
            }
        }
    }
}