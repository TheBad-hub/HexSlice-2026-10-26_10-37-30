using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexSlice.Math
{
    public struct SplitResult
    {
        public bool success;
        public string reason;
        public List<Vector2> polyA;
        public List<Vector2> polyB;
        public Vector2 entryPt;
        public Vector2 exitPt;
    }

    public static class PolygonMath
    {
        public static float PolygonArea(IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return 0f;
            float area = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += poly[i].x * poly[j].y - poly[j].x * poly[i].y;
            }
            return Mathf.Abs(area) * 0.5f;
        }

        public static Vector2 GetCentroid(IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count == 0) return Vector2.zero;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < poly.Count; i++)
            {
                sum += poly[i];
            }
            return sum / poly.Count;
        }

        public static bool PointInPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = poly[i].x, yi = poly[i].y;
                float xj = poly[j].x, yj = poly[j].y;

                bool intersect = ((yi > p.y) != (yj > p.y)) &&
                                 (p.x < (xj - xi) * (p.y - yi) / (yj - yi + 1e-12f) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        public static float DistToSegmentSquared(Vector2 p, Vector2 v, Vector2 w)
        {
            float l2 = (v - w).sqrMagnitude;
            if (l2 == 0f) return (p - v).sqrMagnitude;
            float t = Vector2.Dot(p - v, w - v) / l2;
            t = Mathf.Clamp01(t);
            Vector2 projection = v + t * (w - v);
            return (p - projection).sqrMagnitude;
        }

        public static float DistToSegment(Vector2 p, Vector2 v, Vector2 w)
        {
            return Mathf.Sqrt(DistToSegmentSquared(p, v, w));
        }

        private static float CrossPoints(Vector2 a, Vector2 b, Vector2 o)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        public static List<Vector2> ConvexHull(List<Vector2> points)
        {
            if (points == null || points.Count <= 3)
                return points != null ? new List<Vector2>(points) : new List<Vector2>();

            List<Vector2> pts = new List<Vector2>(points);
            pts.Sort((a, b) => Mathf.Approximately(a.x, b.x) ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            List<Vector2> lower = new List<Vector2>();
            foreach (Vector2 p in pts)
            {
                while (lower.Count >= 2 && CrossPoints(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0f)
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(p);
            }

            List<Vector2> upper = new List<Vector2>();
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                Vector2 p = pts[i];
                while (upper.Count >= 2 && CrossPoints(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0f)
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(p);
            }

            upper.RemoveAt(upper.Count - 1);
            lower.RemoveAt(lower.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        public static List<Vector2> CleanPoly(List<Vector2> poly)
        {
            List<Vector2> res = new List<Vector2>();
            if (poly == null || poly.Count == 0) return res;

            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 next = poly[(i + 1) % n];
                if ((poly[i] - next).sqrMagnitude > 1e-5f)
                {
                    res.Add(poly[i]);
                }
            }
            return res;
        }

        public static SplitResult SplitConvexPolygonStrictly(IReadOnlyList<Vector2> poly, Vector2 p1, Vector2 p2)
        {
            SplitResult result = new SplitResult { success = false };

            if (PointInPolygon(p1, poly))
            {
                result.reason = "START_INSIDE";
                return result;
            }
            if (PointInPolygon(p2, poly))
            {
                result.reason = "END_INSIDE";
                return result;
            }

            float dx = p2.x - p1.x;
            float dy = p2.y - p1.y;
            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f))
            {
                result.reason = "ZERO_LENGTH";
                return result;
            }

            int n = poly.Count;
            float[] signs = new float[n];
            bool hasPos = false;
            bool hasNeg = false;

            for (int i = 0; i < n; i++)
            {
                float cross = (poly[i].x - p1.x) * dy - (poly[i].y - p1.y) * dx;
                signs[i] = cross;
                if (cross > 1e-4f) hasPos = true;
                if (cross < -1e-4f) hasNeg = true;
            }

            if (!hasPos || !hasNeg)
            {
                result.reason = "NO_INTERSECT";
                return result;
            }

            List<Vector2> polyA = new List<Vector2>();
            List<Vector2> polyB = new List<Vector2>();
            List<(Vector2 pt, float t)> intersections = new List<(Vector2 pt, float t)>();

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 vi = poly[i];
                Vector2 vj = poly[j];
                float si = signs[i];
                float sj = signs[j];

                if (si >= -1e-4f) polyA.Add(vi);
                if (si <= 1e-4f) polyB.Add(vi);

                if ((si > 1e-4f && sj < -1e-4f) || (si < -1e-4f && sj > 1e-4f))
                {
                    float tau = si / (si - sj);
                    Vector2 inter = Vector2.Lerp(vi, vj, tau);
                    float t = Mathf.Abs(dx) > Mathf.Abs(dy)
                        ? (inter.x - p1.x) / dx
                        : (inter.y - p1.y) / dy;

                    intersections.Add((inter, t));
                    polyA.Add(inter);
                    polyB.Add(inter);
                }
            }

            if (intersections.Count != 2)
            {
                result.reason = "NOT_TWO_EDGES";
                return result;
            }

            float t1 = intersections[0].t;
            float t2 = intersections[1].t;
            if (t1 < 0f || t1 > 1f || t2 < 0f || t2 > 1f)
            {
                result.reason = "NOT_THROUGH";
                return result;
            }

            List<Vector2> cleanA = CleanPoly(polyA);
            List<Vector2> cleanB = CleanPoly(polyB);
            if (cleanA.Count < 3 || cleanB.Count < 3)
            {
                result.reason = "DEGENERATE";
                return result;
            }

            result.success = true;
            result.polyA = cleanA;
            result.polyB = cleanB;
            result.entryPt = intersections[0].pt;
            result.exitPt = intersections[1].pt;
            return result;
        }
    }
}