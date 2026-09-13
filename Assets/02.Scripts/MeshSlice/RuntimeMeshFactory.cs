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
                data._outerTriangles,
                0,
                calculateBounds: false);

            mesh.SetTriangles(
                data._capTriangles,
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
                data._outerTriangles.Count +
                data._capTriangles.Count);

            combinedTriangles.AddRange(data._outerTriangles);
            combinedTriangles.AddRange(data._capTriangles);

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

            mesh.SetVertices(data._vertices);
            mesh.SetNormals(data._normals);
            mesh.SetUVs(0, data._uvs);

            return mesh;
        }

        private static void Validate(MeshSideData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data._vertices.Count == 0)
            {
                throw new InvalidOperationException(
                    "The mesh side contains no vertices.");
            }

            if (data._vertices.Count != data._normals.Count ||
                data._vertices.Count != data._uvs.Count)
            {
                throw new InvalidOperationException(
                    "Vertex, normal, and UV counts do not match.");
            }

            ValidateTriangleIndices(
                data._outerTriangles,
                data._vertices.Count,
                "outer");

            ValidateTriangleIndices(
                data._capTriangles,
                data._vertices.Count,
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