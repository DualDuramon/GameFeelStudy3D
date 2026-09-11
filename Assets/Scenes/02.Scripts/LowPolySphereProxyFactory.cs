using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeMeshSlicing
{
    public static class LowPolySphereProxyFactory
    {
        private const int MaximumConvexTriangles = 255;
        private const int DefaultLongitudeSegments = 12;
        private const int DefaultLatitudeSegments = 6;

        public static bool TryCreateSlicedProxy(
            Bounds sourceBounds,
            Plane localPlane,
            SliceSettings settings,
            out Mesh positiveProxy,
            out Mesh negativeProxy,
            out string failureMessage)
        {
            positiveProxy = null;
            negativeProxy = null;
            failureMessage = string.Empty;

            Mesh sourceProxy = null;

            try
            {
                sourceProxy = CreateSourceProxy(
                    sourceBounds,
                    DefaultLongitudeSegments,
                    DefaultLatitudeSegments);

                if (!SphereMeshSlicer.TrySlice(
                        sourceProxy,
                        localPlane,
                        settings,
                        out SliceGeometry proxyGeometry,
                        out SliceDiagnostics diagnostics))
                {
                    failureMessage =
                        $"Proxy slicing failed: {diagnostics.Message}";

                    return false;
                }

                if (proxyGeometry.Positive.TotalTriangleCount >
                    MaximumConvexTriangles ||
                    proxyGeometry.Negative.TotalTriangleCount >
                    MaximumConvexTriangles)
                {
                    failureMessage =
                        "A generated proxy exceeded the 255-triangle " +
                        "Convex MeshCollider limit.";

                    return false;
                }

                positiveProxy =
                    RuntimeMeshFactory.CreateColliderMesh(
                        proxyGeometry.Positive,
                        "Sphere_Positive_ColliderProxy");

                negativeProxy =
                    RuntimeMeshFactory.CreateColliderMesh(
                        proxyGeometry.Negative,
                        "Sphere_Negative_ColliderProxy");

                return true;
            }
            catch (Exception exception)
            {
                DestroyRuntimeMesh(positiveProxy);
                DestroyRuntimeMesh(negativeProxy);

                positiveProxy = null;
                negativeProxy = null;

                failureMessage =
                    $"Proxy creation failed: {exception.Message}";

                return false;
            }
            finally
            {
                DestroyRuntimeMesh(sourceProxy);
            }
        }

        private static Mesh CreateSourceProxy(
            Bounds bounds,
            int longitudeSegments,
            int latitudeSegments)
        {
            if (longitudeSegments < 3)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(longitudeSegments));
            }

            if (latitudeSegments < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(latitudeSegments));
            }

            if (bounds.extents.x <= 0f ||
                bounds.extents.y <= 0f ||
                bounds.extents.z <= 0f)
            {
                throw new InvalidOperationException(
                    "The source mesh bounds are invalid.");
            }

            List<Vector3> vertices = new();
            List<Vector3> normals = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();

            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents * 1.001f;

            // Top pole.
            vertices.Add(center + Vector3.up * extents.y);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1f));

            for (int latitude = 1;
                 latitude < latitudeSegments;
                 latitude++)
            {
                float latitudeT =
                    latitude / (float)latitudeSegments;

                float polarAngle =
                    Mathf.PI * latitudeT;

                float ringY = Mathf.Cos(polarAngle);
                float ringRadius = Mathf.Sin(polarAngle);

                for (int longitude = 0;
                     longitude < longitudeSegments;
                     longitude++)
                {
                    float longitudeT =
                        longitude / (float)longitudeSegments;

                    float azimuth =
                        longitudeT * Mathf.PI * 2f;

                    Vector3 direction = new(
                        ringRadius * Mathf.Cos(azimuth),
                        ringY,
                        ringRadius * Mathf.Sin(azimuth));

                    Vector3 position =
                        center +
                        Vector3.Scale(direction, extents);

                    vertices.Add(position);
                    normals.Add(direction.normalized);
                    uvs.Add(new Vector2(
                        longitudeT,
                        1f - latitudeT));
                }
            }

            int bottomIndex = vertices.Count;

            vertices.Add(center + Vector3.down * extents.y);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0f));

            int RingIndex(int latitude, int longitude)
            {
                int wrappedLongitude =
                    (longitude + longitudeSegments) %
                    longitudeSegments;

                return
                    1 +
                    (latitude - 1) * longitudeSegments +
                    wrappedLongitude;
            }

            // Top fan.
            for (int longitude = 0;
                 longitude < longitudeSegments;
                 longitude++)
            {
                int current =
                    RingIndex(1, longitude);

                int next =
                    RingIndex(1, longitude + 1);

                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(current);
            }

            // Middle rings.
            for (int latitude = 1;
                 latitude < latitudeSegments - 1;
                 latitude++)
            {
                for (int longitude = 0;
                     longitude < longitudeSegments;
                     longitude++)
                {
                    int upperCurrent =
                        RingIndex(latitude, longitude);

                    int upperNext =
                        RingIndex(latitude, longitude + 1);

                    int lowerCurrent =
                        RingIndex(latitude + 1, longitude);

                    int lowerNext =
                        RingIndex(latitude + 1, longitude + 1);

                    triangles.Add(upperCurrent);
                    triangles.Add(upperNext);
                    triangles.Add(lowerCurrent);

                    triangles.Add(upperNext);
                    triangles.Add(lowerNext);
                    triangles.Add(lowerCurrent);
                }
            }

            // Bottom fan.
            for (int longitude = 0;
                 longitude < longitudeSegments;
                 longitude++)
            {
                int current =
                    RingIndex(
                        latitudeSegments - 1,
                        longitude);

                int next =
                    RingIndex(
                        latitudeSegments - 1,
                        longitude + 1);

                triangles.Add(bottomIndex);
                triangles.Add(current);
                triangles.Add(next);
            }

            Mesh mesh = new()
            {
                name = "LowPolySphereColliderSource",
                hideFlags = HideFlags.HideAndDontSave
            };

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static void DestroyRuntimeMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}