using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeMeshSlicing
{
    public static class SphereMeshSlicer
    {
        private readonly struct SourceTriangle
        {
            public readonly int indexA;
            public readonly int indexB;
            public readonly int indexC;
            public readonly bool isCutSurface;

            public SourceTriangle(int indexA, int indexB, int indexC, bool isCutSurface)
            {
                this.indexA = indexA;
                this.indexB = indexB;
                this.indexC = indexC;
                this.isCutSurface = isCutSurface;
            }
        }

        private readonly struct ProjectedContourPoint
        {
            public readonly Vector3 position;
            public readonly Vector2 projected;
            public readonly float angle;

            public ProjectedContourPoint(Vector3 position, Vector2 projected, float angle)
            {
                this.position = position;
                this.projected = projected;
                this.angle = angle;
            }
        }

        public static bool TrySlice(Mesh sourceMesh, Plane localPlane, SliceSettings settings, out SliceGeometry result, out SliceDiagnostics diagnostics)
        {
            result = null;
            diagnostics = new SliceDiagnostics();
            settings = settings.Sanitized();

            if (!TryReadSourceMesh(sourceMesh, diagnostics, out Vector3[] positions, out Vector3[] normals, out Vector2[] uvs, out List<SourceTriangle> triangles))
            {
                return false;
            }

            if (!SlicingMath.IsFinite(localPlane.normal) || localPlane.normal.sqrMagnitude < 0.999f)
            {
                diagnostics.Fail(SliceFailureReason.InvalidPlane, "The local slice plane has an invalid normal.");
                return false;
            }

            MeshSideData positiveSide = new(settings.minimumTriangleAreaSquared);
            MeshSideData negativeSide = new(settings.minimumTriangleAreaSquared);
            List<IntersectionSegment> intersectionSegments = new();

            for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                SourceTriangle sourceTriangle = triangles[triangleIndex];
                int indexA = sourceTriangle.indexA;
                int indexB = sourceTriangle.indexB;
                int indexC = sourceTriangle.indexC;

                if (!IsValidVertexIndex(indexA, positions.Length) || !IsValidVertexIndex(indexB, positions.Length) || !IsValidVertexIndex(indexC, positions.Length))
                {
                    diagnostics.Fail(SliceFailureReason.InvalidMesh, $"Triangle {triangleIndex / 3} contains an invalid vertex index.");
                    return false;
                }

                SliceVertex[] triangleVertices = {
                    new(positions[indexA], normals[indexA], uvs[indexA]),
                    new(positions[indexB], normals[indexB], uvs[indexB]),
                    new(positions[indexC], normals[indexC], uvs[indexC])
                };

                float[] signedDistances = {
                    SnapDistance(localPlane.GetDistanceToPoint(triangleVertices[0].position), settings.distanceEpsilon),
                    SnapDistance(localPlane.GetDistanceToPoint(triangleVertices[1].position), settings.distanceEpsilon),
                    SnapDistance(localPlane.GetDistanceToPoint(triangleVertices[2].position), settings.distanceEpsilon)
                };
                SnapOnPlaneVertices(triangleVertices, signedDistances, localPlane);
                TryCollectIntersectionSegment(triangleVertices, signedDistances, localPlane, settings.duplicatePositionEpsilon, intersectionSegments);
                bool hasPositive = signedDistances[0] > 0f || signedDistances[1] > 0f || signedDistances[2] > 0f;
                bool hasNegative = signedDistances[0] < 0f || signedDistances[1] < 0f || signedDistances[2] < 0f;

                if (hasPositive && !hasNegative)
                {
                    positiveSide.AddSurfaceTriangle(triangleVertices[0], triangleVertices[1], triangleVertices[2], sourceTriangle.isCutSurface);
                    diagnostics.PositiveSourceTriangles++;
                    continue;
                }

                if (hasNegative && !hasPositive)
                {
                    negativeSide.AddSurfaceTriangle(triangleVertices[0], triangleVertices[1], triangleVertices[2], sourceTriangle.isCutSurface);
                    diagnostics.NegativeSourceTriangles++;
                    continue;
                }

                if (!hasPositive && !hasNegative)
                {
                    AddCoplanarTriangleToOneSide(triangleVertices, localPlane.normal, positiveSide, negativeSide, sourceTriangle.isCutSurface);
                    diagnostics.OnPlaneSourceTriangles++;
                    continue;
                }

                diagnostics.CrossingSourceTriangles++;
                List<SliceVertex> positivePolygon = ClipTriangleToHalfSpace(triangleVertices, signedDistances, localPlane, keepPositive: true, settings.duplicatePositionEpsilon);
                List<SliceVertex> negativePolygon = ClipTriangleToHalfSpace(triangleVertices, signedDistances, localPlane, keepPositive: false, settings.duplicatePositionEpsilon);
                TriangulatePolygon(positivePolygon, positiveSide, sourceTriangle.isCutSurface);
                TriangulatePolygon(negativePolygon, negativeSide, sourceTriangle.isCutSurface);
            }

            diagnostics.RawIntersectionSegmentCount = intersectionSegments.Count;

            if (positiveSide.TotalTriangleCount == 0 || negativeSide.TotalTriangleCount == 0 || intersectionSegments.Count == 0)
            {
                diagnostics.Fail(SliceFailureReason.PlaneDidNotSplitMesh, "The plane did not produce two non-empty mesh sides.");
                return false;
            }

            if (!TryBuildConvexContour(intersectionSegments, localPlane, settings.duplicatePositionEpsilon, out List<Vector3> sortedContour, out string contourFailure))
            {
                diagnostics.Fail(SliceFailureReason.InvalidContour, contourFailure);
                return false;
            }

            diagnostics.UniqueContourPointCount = sortedContour.Count;

            if (!AppendCaps(sortedContour, localPlane, positiveSide, negativeSide, settings.duplicatePositionEpsilon))
            {
                diagnostics.Fail(SliceFailureReason.InvalidContour, "The cap polygon was degenerate and could not be triangulated.");
                return false;
            }

            diagnostics.PositiveOutputTriangles = positiveSide.TotalTriangleCount;
            diagnostics.NegativeOutputTriangles = negativeSide.TotalTriangleCount;
            diagnostics.Message = "Slice completed successfully.";
            result = new SliceGeometry(positiveSide, negativeSide, sortedContour, localPlane, diagnostics);

            return true;
        }

        private static bool TryReadSourceMesh(Mesh sourceMesh, SliceDiagnostics diagnostics, out Vector3[] positions, out Vector3[] normals, out Vector2[] uvs, out List<SourceTriangle> triangles)
        {
            positions = Array.Empty<Vector3>();
            normals = Array.Empty<Vector3>();
            uvs = Array.Empty<Vector2>();
            triangles = new List<SourceTriangle>();

            if (sourceMesh == null)
            {
                diagnostics.Fail(SliceFailureReason.InvalidMesh, "The source Mesh is missing.");
                return false;
            }

            if (!sourceMesh.isReadable)
            {
                diagnostics.Fail(SliceFailureReason.InvalidMesh, $"Mesh '{sourceMesh.name}' is not readable.");
                return false;
            }

            if (sourceMesh.subMeshCount < 1 || sourceMesh.subMeshCount > 2)
            {
                diagnostics.Fail(SliceFailureReason.InvalidMesh, "This prototype only supports source meshes with one outer submesh and one optional cut-surface submesh.");
                return false;
            }

            try
            {
                positions = sourceMesh.vertices;
                normals = sourceMesh.normals;
                uvs = sourceMesh.uv;
                for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
                {
                    int[] subMeshTriangles = sourceMesh.GetTriangles(subMeshIndex);

                    if (subMeshTriangles.Length % 3 != 0)
                    {
                        diagnostics.Fail(SliceFailureReason.InvalidMesh, $"Submesh {subMeshIndex} does not contain complete triangles.");
                        return false;
                    }

                    bool isCutSurface = subMeshIndex == 1;

                    for (int triangleIndex = 0; triangleIndex < subMeshTriangles.Length; triangleIndex += 3)
                    {
                        triangles.Add(new SourceTriangle(subMeshTriangles[triangleIndex], subMeshTriangles[triangleIndex + 1], subMeshTriangles[triangleIndex + 2], isCutSurface));
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostics.Fail(SliceFailureReason.InvalidMesh, $"Could not read mesh data: {exception.Message}");

                return false;
            }

            if (positions.Length < 3 || triangles.Count == 0)
            {
                diagnostics.Fail(SliceFailureReason.InvalidMesh, "The source mesh does not contain valid triangles.");

                return false;
            }

            if (normals.Length != positions.Length)
            {
                diagnostics.Fail(SliceFailureReason.InvalidMesh, "The source mesh must contain one normal per vertex.");

                return false;
            }

            if (uvs.Length != positions.Length)
            {
                diagnostics.Fail(SliceFailureReason.InvalidMesh, "The source mesh must contain one UV per vertex.");

                return false;
            }

            return true;
        }

        private static bool IsValidVertexIndex(int vertexIndex, int vertexCount)
        {
            return vertexIndex >= 0 && vertexIndex < vertexCount;
        }

        private static float SnapDistance(float signedDistance, float distanceEpsilon)
        {
            return Mathf.Abs(signedDistance) <= distanceEpsilon ? 0f : signedDistance;
        }

        private static void SnapOnPlaneVertices(SliceVertex[] vertices, float[] signedDistances, Plane localPlane)
        {
            for (int index = 0; index < 3; index++)
            {
                if (signedDistances[index] != 0f)
                {
                    continue;
                }

                Vector3 snappedPosition = localPlane.ClosestPointOnPlane(vertices[index].position);
                vertices[index] = vertices[index].WithPosition(snappedPosition);
            }
        }

        private static void AddCoplanarTriangleToOneSide(SliceVertex[] triangle, Vector3 planeNormal, MeshSideData positiveSide, MeshSideData negativeSide, bool isCutSurface)
        {
            Vector3 edgeAB = triangle[1].position - triangle[0].position;
            Vector3 edgeAC = triangle[2].position - triangle[0].position;
            Vector3 faceNormal = Vector3.Cross(edgeAB, edgeAC);

            if (Vector3.Dot(faceNormal, planeNormal) >= 0f)
            {
                positiveSide.AddSurfaceTriangle(triangle[0], triangle[1], triangle[2], isCutSurface);
            }
            else
            {
                negativeSide.AddSurfaceTriangle(triangle[0], triangle[1], triangle[2], isCutSurface);
            }
        }

        private static List<SliceVertex> ClipTriangleToHalfSpace(SliceVertex[] vertices, float[] signedDistances, Plane localPlane, bool keepPositive, float duplicateEpsilon)
        {
            List<SliceVertex> output = new(4);

            for (int currentIndex = 0; currentIndex < vertices.Length; currentIndex++)
            {
                int nextIndex = (currentIndex + 1) % vertices.Length;
                SliceVertex currentVertex = vertices[currentIndex];
                SliceVertex nextVertex = vertices[nextIndex];
                float currentDistance = signedDistances[currentIndex];
                float nextDistance = signedDistances[nextIndex];
                bool currentInside = keepPositive ? currentDistance >= 0f : currentDistance <= 0f;
                bool nextInside = keepPositive ? nextDistance >= 0f : nextDistance <= 0f;

                if (currentInside && nextInside)
                {
                    output.Add(nextVertex);
                }
                else if (currentInside && !nextInside)
                {
                    output.Add(IntersectEdge(currentVertex, nextVertex, currentDistance, nextDistance, localPlane));
                }
                else if (!currentInside && nextInside)
                {
                    output.Add(IntersectEdge(currentVertex, nextVertex, currentDistance, nextDistance, localPlane));
                    output.Add(nextVertex);
                }
            }

            return RemoveConsecutiveDuplicateVertices(output, duplicateEpsilon);
        }

        private static SliceVertex IntersectEdge(SliceVertex start, SliceVertex end, float startDistance, float endDistance, Plane localPlane)
        {
            float denominator = startDistance - endDistance;
            float intersectionT = Mathf.Abs(denominator) > 0.00000001f ? startDistance / denominator : 0.5f;
            intersectionT = Mathf.Clamp01(intersectionT);
            SliceVertex intersection = SliceVertex.Interpolate(start, end, intersectionT);
            Vector3 snappedPosition = localPlane.ClosestPointOnPlane(intersection.position);

            return intersection.WithPosition(snappedPosition);
        }

        private static List<SliceVertex> RemoveConsecutiveDuplicateVertices(List<SliceVertex> source, float duplicateEpsilon)
        {
            List<SliceVertex> cleaned = new(source.Count);
            float epsilonSquared = duplicateEpsilon * duplicateEpsilon;

            for (int index = 0; index < source.Count; index++)
            {
                SliceVertex candidate = source[index];

                if (cleaned.Count == 0 || (candidate.position - cleaned[cleaned.Count - 1].position).sqrMagnitude > epsilonSquared)
                {
                    cleaned.Add(candidate);
                }
            }

            if (cleaned.Count > 1 && (cleaned[0].position - cleaned[cleaned.Count - 1].position).sqrMagnitude <= epsilonSquared)
            {
                cleaned.RemoveAt(cleaned.Count - 1);
            }

            return cleaned;
        }

        private static void TriangulatePolygon(List<SliceVertex> polygon, MeshSideData destination, bool isCutSurface)
        {
            if (polygon.Count < 3)
            {
                return;
            }

            for (int index = 1; index < polygon.Count - 1; index++)
            {
                destination.AddSurfaceTriangle(polygon[0], polygon[index], polygon[index + 1], isCutSurface);
            }
        }

        private static void TryCollectIntersectionSegment(SliceVertex[] vertices, float[] signedDistances, Plane localPlane, float duplicateEpsilon, List<IntersectionSegment> destination)
        {
            List<Vector3> intersectionPoints = new(3);

            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                if (signedDistances[vertexIndex] == 0f)
                {
                    AddUniquePoint(intersectionPoints, localPlane.ClosestPointOnPlane(vertices[vertexIndex].position), duplicateEpsilon);
                }
            }

            for (int edgeIndex = 0; edgeIndex < vertices.Length; edgeIndex++)
            {
                int nextIndex = (edgeIndex + 1) % vertices.Length;
                float distanceA = signedDistances[edgeIndex];
                float distanceB = signedDistances[nextIndex];

                if (!AreStrictlyOpposite(distanceA, distanceB))
                {
                    continue;
                }

                SliceVertex intersection = IntersectEdge(vertices[edgeIndex], vertices[nextIndex], distanceA, distanceB, localPlane);
                AddUniquePoint(intersectionPoints, intersection.position, duplicateEpsilon);
            }

            if (intersectionPoints.Count != 2)
            {
                return;
            }

            float epsilonSquared = duplicateEpsilon * duplicateEpsilon;

            if ((intersectionPoints[0] - intersectionPoints[1]).sqrMagnitude <= epsilonSquared)
            {
                return;
            }

            destination.Add(new IntersectionSegment(intersectionPoints[0], intersectionPoints[1]));
        }

        private static bool AreStrictlyOpposite(float distanceA, float distanceB)
        {
            return (distanceA > 0f && distanceB < 0f) || (distanceA < 0f && distanceB > 0f);
        }

        private static void AddUniquePoint(List<Vector3> points, Vector3 candidate, float duplicateEpsilon)
        {
            float epsilonSquared = duplicateEpsilon * duplicateEpsilon;

            for (int index = 0; index < points.Count; index++)
            {
                if ((points[index] - candidate).sqrMagnitude <= epsilonSquared)
                {
                    return;
                }
            }

            points.Add(candidate);
        }

        private static bool TryBuildConvexContour(List<IntersectionSegment> segments, Plane localPlane, float duplicateEpsilon, out List<Vector3> sortedContour, out string failureMessage)
        {
            sortedContour = new List<Vector3>();
            failureMessage = string.Empty;
            List<Vector3> uniquePoints = new();
            List<HashSet<int>> adjacency = new();
            HashSet<ulong> uniqueEdges = new();

            foreach (IntersectionSegment segment in segments)
            {
                int indexA = FindOrAddContourPoint(uniquePoints, adjacency, localPlane.ClosestPointOnPlane(segment.pointA), duplicateEpsilon);
                int indexB = FindOrAddContourPoint(uniquePoints, adjacency, localPlane.ClosestPointOnPlane(segment.pointB), duplicateEpsilon);

                if (indexA == indexB)
                {
                    continue;
                }

                int minimumIndex = Mathf.Min(indexA, indexB);
                int maximumIndex = Mathf.Max(indexA, indexB);
                ulong edgeKey = ((ulong)(uint)minimumIndex << 32) | (uint)maximumIndex;

                if (!uniqueEdges.Add(edgeKey))
                {
                    continue;
                }

                adjacency[indexA].Add(indexB);
                adjacency[indexB].Add(indexA);
            }

            if (uniquePoints.Count < 3)
            {
                failureMessage = "The slice contour contains fewer than three unique points.";

                return false;
            }

            if (uniqueEdges.Count != uniquePoints.Count)
            {
                failureMessage = "The intersection segments do not form one closed contour.";

                return false;
            }

            for (int index = 0; index < adjacency.Count; index++)
            {
                if (adjacency[index].Count != 2)
                {
                    failureMessage = $"Contour point {index} has degree {adjacency[index].Count}; expected 2.";

                    return false;
                }
            }

            if (!IsSingleConnectedContour(adjacency))
            {
                failureMessage = "Multiple disconnected slice contours were detected.";

                return false;
            }

            Vector3 centroid = Vector3.zero;

            foreach (Vector3 point in uniquePoints)
            {
                centroid += point;
            }

            centroid /= uniquePoints.Count;
            centroid = localPlane.ClosestPointOnPlane(centroid);
            BuildPlaneBasis(localPlane.normal, out Vector3 basisU, out Vector3 basisV);
            List<ProjectedContourPoint> projectedPoints = new(uniquePoints.Count);

            foreach (Vector3 point in uniquePoints)
            {
                Vector3 offset = point - centroid;
                Vector2 projected = new(Vector3.Dot(offset, basisU), Vector3.Dot(offset, basisV));
                projectedPoints.Add(new ProjectedContourPoint(point, projected, Mathf.Atan2(projected.y, projected.x)));
            }

            projectedPoints.Sort((left, right) => left.angle.CompareTo(right.angle));
            float twiceSignedArea = 0f;

            for (int index = 0; index < projectedPoints.Count; index++)
            {
                Vector2 current = projectedPoints[index].projected;
                Vector2 next = projectedPoints[(index + 1) % projectedPoints.Count].projected;
                twiceSignedArea += current.x * next.y - current.y * next.x;
            }

            if (Mathf.Abs(twiceSignedArea) <= duplicateEpsilon * duplicateEpsilon)
            {
                failureMessage = "The projected slice contour has near-zero area.";

                return false;
            }

            if (twiceSignedArea < 0f)
            {
                projectedPoints.Reverse();
            }

            foreach (ProjectedContourPoint point in projectedPoints)
            {
                sortedContour.Add(point.position);
            }

            return true;
        }

        private static int FindOrAddContourPoint(List<Vector3> points, List<HashSet<int>> adjacency, Vector3 candidate, float duplicateEpsilon)
        {
            float epsilonSquared = duplicateEpsilon * duplicateEpsilon;

            for (int index = 0; index < points.Count; index++)
            {
                if ((points[index] - candidate).sqrMagnitude <= epsilonSquared)
                {
                    return index;
                }
            }

            points.Add(candidate);
            adjacency.Add(new HashSet<int>());
            return points.Count - 1;
        }

        private static bool IsSingleConnectedContour(List<HashSet<int>> adjacency)
        {
            bool[] visited = new bool[adjacency.Count];
            Stack<int> pending = new();
            pending.Push(0);
            visited[0] = true;

            while (pending.Count > 0)
            {
                int current = pending.Pop();

                foreach (int neighbour in adjacency[current])
                {
                    if (visited[neighbour])
                    {
                        continue;
                    }

                    visited[neighbour] = true;
                    pending.Push(neighbour);
                }
            }

            for (int index = 0; index < visited.Length; index++)
            {
                if (!visited[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AppendCaps(List<Vector3> contour, Plane localPlane, MeshSideData positiveSide, MeshSideData negativeSide, float uvEpsilon)
        {
            if (contour.Count < 3)
            {
                return false;
            }

            BuildPlaneBasis(localPlane.normal, out Vector3 basisU, out Vector3 basisV);
            Vector3 centroid = Vector3.zero;

            foreach (Vector3 point in contour)
            {
                centroid += point;
            }

            centroid /= contour.Count;
            centroid = localPlane.ClosestPointOnPlane(centroid);
            Vector2[] projectedPoints = new Vector2[contour.Count];
            Vector2 projectedMinimum = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 projectedMaximum = new(float.NegativeInfinity, float.NegativeInfinity);

            for (int index = 0; index < contour.Count; index++)
            {
                Vector3 offset = contour[index] - centroid;
                Vector2 projected = new(Vector3.Dot(offset, basisU), Vector3.Dot(offset, basisV));
                projectedPoints[index] = projected;
                projectedMinimum = Vector2.Min(projectedMinimum, projected);
                projectedMaximum = Vector2.Max(projectedMaximum, projected);
            }

            Vector2 projectedSize = projectedMaximum - projectedMinimum;
            projectedSize.x = Mathf.Max(projectedSize.x, uvEpsilon);
            projectedSize.y = Mathf.Max(projectedSize.y, uvEpsilon);
            Vector2 centerUv = new((0f - projectedMinimum.x) / projectedSize.x, (0f - projectedMinimum.y) / projectedSize.y);
            int positiveCapTrianglesBefore = positiveSide.CapTriangleCount;
            int negativeCapTrianglesBefore = negativeSide.CapTriangleCount;

            for (int index = 0; index < contour.Count; index++)
            {
                int nextIndex = (index + 1) % contour.Count;
                Vector2 currentUv = new((projectedPoints[index].x - projectedMinimum.x) / projectedSize.x, (projectedPoints[index].y - projectedMinimum.y) / projectedSize.y);
                Vector2 nextUv = new((projectedPoints[nextIndex].x - projectedMinimum.x) / projectedSize.x, (projectedPoints[nextIndex].y - projectedMinimum.y) / projectedSize.y);
                Vector3 negativeNormal = localPlane.normal;
                Vector3 positiveNormal = -localPlane.normal;
                SliceVertex negativeCenter = new(centroid, negativeNormal, centerUv);
                SliceVertex negativeCurrent = new(contour[index], negativeNormal, currentUv);
                SliceVertex negativeNext = new(contour[nextIndex], negativeNormal, nextUv);

                // Ascending contour winding faces +planeNormal.
                negativeSide.AddCapTriangle(negativeCenter, negativeCurrent, negativeNext);
                SliceVertex positiveCenter = new(centroid, positiveNormal, centerUv);
                SliceVertex positiveCurrent = new(contour[index], positiveNormal, currentUv);
                SliceVertex positiveNext = new(contour[nextIndex], positiveNormal, nextUv);

                // Reverse winding so the positive cap faces -planeNormal.
                positiveSide.AddCapTriangle(positiveCenter, positiveNext, positiveCurrent);
            }

            return positiveSide.CapTriangleCount > positiveCapTrianglesBefore && negativeSide.CapTriangleCount > negativeCapTrianglesBefore;
        }

        private static void BuildPlaneBasis(Vector3 planeNormal, out Vector3 basisU, out Vector3 basisV)
        {
            Vector3 helperAxis = Mathf.Abs(Vector3.Dot(planeNormal, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            basisU = Vector3.Cross(helperAxis, planeNormal).normalized;
            basisV = Vector3.Cross(planeNormal, basisU).normalized;
        }
    }
}
