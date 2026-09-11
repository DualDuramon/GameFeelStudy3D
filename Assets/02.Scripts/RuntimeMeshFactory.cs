using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeMeshSlicing
{
    public static class RuntimeMeshFactory
    {
        public static Mesh CreateVisualMesh(
            MeshSideData data,
            string meshName)
        {
            Validate(data);

            Mesh mesh = CreateBaseMesh(data, meshName);
            mesh.subMeshCount = 2;

            mesh.SetTriangles(
                data.outerTriangles,
                0,
                calculateBounds: false);

            mesh.SetTriangles(
                data.capTriangles,
                1,
                calculateBounds: false);

            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateColliderMesh(
            MeshSideData data,
            string meshName)
        {
            Validate(data);

            Mesh mesh = CreateBaseMesh(data, meshName);
            mesh.subMeshCount = 1;

            List<int> combinedTriangles = new(
                data.outerTriangles.Count +
                data.capTriangles.Count);

            combinedTriangles.AddRange(data.outerTriangles);
            combinedTriangles.AddRange(data.capTriangles);

            mesh.SetTriangles(
                combinedTriangles,
                0,
                calculateBounds: false);

            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBaseMesh(
            MeshSideData data,
            string meshName)
        {
            Mesh mesh = new()
            {
                name = meshName,
                indexFormat =
                    data.VertexCount > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
            };

            mesh.SetVertices(data.vertices);
            mesh.SetNormals(data.normals);
            mesh.SetUVs(0, data.uvs);

            return mesh;
        }

        private static void Validate(MeshSideData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.vertices.Count == 0)
            {
                throw new InvalidOperationException(
                    "The mesh side contains no vertices.");
            }

            if (data.vertices.Count != data.normals.Count ||
                data.vertices.Count != data.uvs.Count)
            {
                throw new InvalidOperationException(
                    "Vertex, normal, and UV counts do not match.");
            }

            ValidateTriangleIndices(
                data.outerTriangles,
                data.vertices.Count,
                "outer");

            ValidateTriangleIndices(
                data.capTriangles,
                data.vertices.Count,
                "cap");
        }

        private static void ValidateTriangleIndices(
            List<int> triangles,
            int vertexCount,
            string label)
        {
            if (triangles.Count % 3 != 0)
            {
                throw new InvalidOperationException(
                    $"The {label} index count is not divisible by three.");
            }

            foreach (int vertexIndex in triangles)
            {
                if (vertexIndex < 0 ||
                    vertexIndex >= vertexCount)
                {
                    throw new InvalidOperationException(
                        $"The {label} submesh contains invalid index " +
                        $"{vertexIndex}.");
                }
            }
        }
    }
}