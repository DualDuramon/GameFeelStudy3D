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
        public float distanceEpsilon;

        [Min(0.0000001f)]
        public float duplicatePositionEpsilon;

        [Min(0.000000000001f)]
        public float minimumTriangleAreaSquared;

        public static SliceSettings Default
        {
            get
            {
                return new SliceSettings
                {
                    distanceEpsilon = 0.00001f,
                    duplicatePositionEpsilon = 0.0001f,
                    minimumTriangleAreaSquared = 0.000000000001f
                };
            }
        }

        public SliceSettings Sanitized()
        {
            return new SliceSettings
            {
                distanceEpsilon = Mathf.Max(distanceEpsilon, 0.0000001f),
                duplicatePositionEpsilon =
                    Mathf.Max(duplicatePositionEpsilon, 0.0000001f),
                minimumTriangleAreaSquared =
                    Mathf.Max(minimumTriangleAreaSquared, 0.000000000001f)
            };
        }
    }

    public readonly struct SliceRequest
    {
        public readonly Vector3 contactPointWorld;
        public readonly Vector3 bladeAxisWorld;
        public readonly Vector3 swingDirectionWorld;
        public readonly Vector3 planeNormalWorld;
        public readonly float impactSpeed;

        public SliceRequest(
            Vector3 contactPointWorld,
            Vector3 bladeAxisWorld,
            Vector3 swingDirectionWorld,
            Vector3 planeNormalWorld,
            float impactSpeed)
        {
            this.contactPointWorld = contactPointWorld;
            this.bladeAxisWorld = bladeAxisWorld;
            this.swingDirectionWorld = swingDirectionWorld;
            this.planeNormalWorld = planeNormalWorld;
            this.impactSpeed = impactSpeed;
        }
    }

    public readonly struct SliceVertex
    {
        public readonly Vector3 position;
        public readonly Vector3 normal;
        public readonly Vector2 uv;

        public SliceVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            this.position = position;
            this.normal = normal;
            this.uv = uv;
        }

        public SliceVertex WithPosition(Vector3 newPosition)
        {
            return new SliceVertex(newPosition, normal, uv);
        }

        public static SliceVertex Interpolate(
            SliceVertex start,
            SliceVertex end,
            float interpolationT)
        {
            Vector3 interpolatedPosition = Vector3.LerpUnclamped(
                start.position,
                end.position,
                interpolationT);

            Vector3 interpolatedNormal = Vector3.LerpUnclamped(
                start.normal,
                end.normal,
                interpolationT);

            if (interpolatedNormal.sqrMagnitude > 0.00000001f)
            {
                interpolatedNormal.Normalize();
            }
            else
            {
                interpolatedNormal = start.normal.normalized;
            }

            Vector2 interpolatedUv = Vector2.LerpUnclamped(
                start.uv,
                end.uv,
                interpolationT);

            return new SliceVertex(
                interpolatedPosition,
                interpolatedNormal,
                interpolatedUv);
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
        internal readonly List<Vector3> vertices = new();
        internal readonly List<Vector3> normals = new();
        internal readonly List<Vector2> uvs = new();
        internal readonly List<int> outerTriangles = new();
        internal readonly List<int> capTriangles = new();

        private readonly float minimumTriangleAreaSquared;

        internal MeshSideData(float minimumTriangleAreaSquared)
        {
            this.minimumTriangleAreaSquared = minimumTriangleAreaSquared;
        }

        public int VertexCount => vertices.Count;
        public int OuterTriangleCount => outerTriangles.Count / 3;
        public int CapTriangleCount => capTriangles.Count / 3;
        public int TotalTriangleCount =>
            OuterTriangleCount + CapTriangleCount;

        internal bool AddOuterTriangle(
            SliceVertex vertexA,
            SliceVertex vertexB,
            SliceVertex vertexC)
        {
            return AddTriangle(
                vertexA,
                vertexB,
                vertexC,
                outerTriangles);
        }

        internal bool AddCapTriangle(
            SliceVertex vertexA,
            SliceVertex vertexB,
            SliceVertex vertexC)
        {
            return AddTriangle(
                vertexA,
                vertexB,
                vertexC,
                capTriangles);
        }

        internal bool AddSurfaceTriangle(SliceVertex vertexA, SliceVertex vertexB, SliceVertex vertexC, bool isCutSurface)
        {
            return isCutSurface ? AddCapTriangle(vertexA, vertexB, vertexC) : AddOuterTriangle(vertexA, vertexB, vertexC);
        }

        private bool AddTriangle(
            SliceVertex vertexA,
            SliceVertex vertexB,
            SliceVertex vertexC,
            List<int> destinationTriangles)
        {
            if (!SlicingMath.IsFinite(vertexA.position) ||
                !SlicingMath.IsFinite(vertexB.position) ||
                !SlicingMath.IsFinite(vertexC.position))
            {
                return false;
            }

            Vector3 edgeAB = vertexB.position - vertexA.position;
            Vector3 edgeAC = vertexC.position - vertexA.position;

            if (Vector3.Cross(edgeAB, edgeAC).sqrMagnitude <=
                minimumTriangleAreaSquared)
            {
                return false;
            }

            int firstIndex = vertices.Count;

            vertices.Add(vertexA.position);
            vertices.Add(vertexB.position);
            vertices.Add(vertexC.position);

            normals.Add(vertexA.normal);
            normals.Add(vertexB.normal);
            normals.Add(vertexC.normal);

            uvs.Add(vertexA.uv);
            uvs.Add(vertexB.uv);
            uvs.Add(vertexC.uv);

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

        internal void Fail(
            SliceFailureReason failureReason,
            string message)
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

        internal SliceGeometry(
            MeshSideData positive,
            MeshSideData negative,
            List<Vector3> contourLocal,
            Plane localPlane,
            SliceDiagnostics diagnostics)
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
