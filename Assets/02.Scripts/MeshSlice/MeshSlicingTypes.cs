using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeMeshSlicing
{
    public enum BladeAxis
    {
        LocalX,
        LocalY,
        LocalZ
    }

    public enum SliceFailureReason
    {
        None,
        InvalidMesh,
        InvalidPlane,
        PlaneDidNotSplitMesh,
        InvalidContour,
        MeshCreationFailed
    }

    [Serializable]
    public struct SliceSettings
    {
        [Min(0.0000001f)]
        public float DistanceEpsilon;

        [Min(0.0000001f)]
        public float DuplicatePositionEpsilon;

        [Min(0.000000000001f)]
        public float MinimumTriangleAreaSquared;

        public static SliceSettings Default
        {
            get
            {
                return new SliceSettings
                {
                    DistanceEpsilon = 0.00001f,
                    DuplicatePositionEpsilon = 0.0001f,
                    MinimumTriangleAreaSquared = 0.000000000001f
                };
            }
        }

        public SliceSettings Sanitized()
        {
            return new SliceSettings
            {
                DistanceEpsilon = Mathf.Max(DistanceEpsilon, 0.0000001f),
                DuplicatePositionEpsilon = Mathf.Max(DuplicatePositionEpsilon, 0.0000001f),
                MinimumTriangleAreaSquared = Mathf.Max(MinimumTriangleAreaSquared, 0.000000000001f)
            };
        }
    }

    public readonly struct SliceRequest
    {
        public readonly Vector3 ContactPointWorld;
        public readonly Vector3 CladeAxisWorld;
        public readonly Vector3 CwingDirectionWorld;
        public readonly Vector3 PlaneNormalWorld;
        public readonly float ImpactSpeed;

        public SliceRequest(Vector3 contactPointWorld, Vector3 bladeAxisWorld, Vector3 swingDirectionWorld, Vector3 planeNormalWorld, float impactSpeed)
        {
            ContactPointWorld = contactPointWorld;
            CladeAxisWorld = bladeAxisWorld;
            CwingDirectionWorld = swingDirectionWorld;
            PlaneNormalWorld = planeNormalWorld;
            ImpactSpeed = impactSpeed;
        }
    }

    public readonly struct SliceVertex
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector2 Uv;

        public SliceVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Position = position;
            Normal = normal;
            Uv = uv;
        }

        public SliceVertex WithPosition(Vector3 newPosition)
        {
            return new SliceVertex(newPosition, Normal, Uv);
        }

        public static SliceVertex Interpolate(SliceVertex start, SliceVertex end, float interpolationT)
        {
            Vector3 interpolatedPosition = Vector3.LerpUnclamped(
                start.Position,
                end.Position,
                interpolationT);

            Vector3 interpolatedNormal = Vector3.LerpUnclamped(
                start.Normal,
                end.Normal,
                interpolationT);

            if (interpolatedNormal.sqrMagnitude > 0.00000001f)
            {
                interpolatedNormal.Normalize();
            }
            else
            {
                interpolatedNormal = start.Normal.normalized;
            }

            Vector2 interpolatedUv = Vector2.LerpUnclamped(
                start.Uv,
                end.Uv,
                interpolationT);

            return new SliceVertex(interpolatedPosition, interpolatedNormal, interpolatedUv);
        }
    }

    internal readonly struct IntersectionSegment
    {
        public readonly Vector3 pointA;
        public readonly Vector3 pointB;

        public IntersectionSegment(Vector3 pointA, Vector3 pointB)
        {
            this.pointA = pointA;
            this.pointB = pointB;
        }
    }

    public sealed class MeshSideData
    {
        internal readonly List<Vector3> _vertices = new();
        internal readonly List<Vector3> _normals = new();
        internal readonly List<Vector2> _uvs = new();
        internal readonly List<int> _outerTriangles = new();
        internal readonly List<int> _capTriangles = new();

        private readonly float _minimumTriangleAreaSquared;

        internal MeshSideData(float minimumTriangleAreaSquared)
        {
            _minimumTriangleAreaSquared = minimumTriangleAreaSquared;
        }

        public int VertexCount => _vertices.Count;
        public int OuterTriangleCount => _outerTriangles.Count / 3;
        public int CapTriangleCount => _capTriangles.Count / 3;
        public int TotalTriangleCount => OuterTriangleCount + CapTriangleCount;

        internal bool AddOuterTriangle(SliceVertex vertexA, SliceVertex vertexB, SliceVertex vertexC)
        {
            return AddTriangle(vertexA, vertexB, vertexC, _outerTriangles);
        }

        internal bool AddCapTriangle(SliceVertex vertexA, SliceVertex vertexB, SliceVertex vertexC)
        {
            return AddTriangle(vertexA, vertexB, vertexC, _capTriangles);
        }

        internal bool AddSurfaceTriangle(SliceVertex vertexA, SliceVertex vertexB, SliceVertex vertexC, bool isCutSurface)
        {
            return isCutSurface ? AddCapTriangle(vertexA, vertexB, vertexC) : AddOuterTriangle(vertexA, vertexB, vertexC);
        }

        private bool AddTriangle(SliceVertex vertexA, SliceVertex vertexB, SliceVertex vertexC, List<int> destinationTriangles)
        {
            if (!SlicingMath.IsFinite(vertexA.Position) ||
                !SlicingMath.IsFinite(vertexB.Position) ||
                !SlicingMath.IsFinite(vertexC.Position))
            {
                return false;
            }

            Vector3 edgeAB = vertexB.Position - vertexA.Position;
            Vector3 edgeAC = vertexC.Position - vertexA.Position;

            if (Vector3.Cross(edgeAB, edgeAC).sqrMagnitude <= _minimumTriangleAreaSquared)
            {
                return false;
            }

            int firstIndex = _vertices.Count;

            _vertices.Add(vertexA.Position);
            _vertices.Add(vertexB.Position);
            _vertices.Add(vertexC.Position);

            _normals.Add(vertexA.Normal);
            _normals.Add(vertexB.Normal);
            _normals.Add(vertexC.Normal);

            _uvs.Add(vertexA.Uv);
            _uvs.Add(vertexB.Uv);
            _uvs.Add(vertexC.Uv);

            destinationTriangles.Add(firstIndex);
            destinationTriangles.Add(firstIndex + 1);
            destinationTriangles.Add(firstIndex + 2);

            return true;
        }
    }

    public sealed class SliceDiagnostics
    {
        public SliceFailureReason FailureReason { get; internal set; }
        public string Message { get; internal set; } = string.Empty;

        public int PositiveSourceTriangles { get; internal set; }
        public int NegativeSourceTriangles { get; internal set; }
        public int CrossingSourceTriangles { get; internal set; }
        public int OnPlaneSourceTriangles { get; internal set; }

        public int RawIntersectionSegmentCount { get; internal set; }
        public int UniqueContourPointCount { get; internal set; }

        public int PositiveOutputTriangles { get; internal set; }
        public int NegativeOutputTriangles { get; internal set; }

        public bool Succeeded => FailureReason == SliceFailureReason.None;

        internal void Fail(SliceFailureReason failureReason, string message)
        {
            FailureReason = failureReason;
            Message = message;
        }
    }

    public sealed class SliceGeometry
    {
        public MeshSideData Positive { get; }
        public MeshSideData Negative { get; }
        public IReadOnlyList<Vector3> ContourLocal { get; }
        public Plane LocalPlane { get; }
        public SliceDiagnostics Diagnostics { get; }

        internal SliceGeometry(MeshSideData positive, MeshSideData negative, List<Vector3> contourLocal, Plane localPlane, SliceDiagnostics diagnostics)
        {
            Positive = positive;
            Negative = negative;
            ContourLocal = contourLocal;
            LocalPlane = localPlane;
            Diagnostics = diagnostics;
        }
    }

    internal static class SlicingMath
    {
        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
