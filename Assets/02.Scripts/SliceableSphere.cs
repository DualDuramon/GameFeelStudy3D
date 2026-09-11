using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeMeshSlicing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class SliceableSphere : MonoBehaviour
    {
        private enum SliceState
        {
            Ready,
            Slicing,
            Sliced
        }

        [Header("Visual")]
        [SerializeField]
        private Material cutSurfaceMaterial;

        [SerializeField]
        private SliceSettings sliceSettings =
            SliceSettings.Default;

        [Header("Optional Piece Physics")]
        [SerializeField]
        private bool addPhysicsToPieces;

        [SerializeField, Min(0.001f)]
        private float totalPieceMass = 1f;

        [SerializeField, Min(0f)]
        private float separationImpulse = 0.35f;

        [SerializeField]
        private CollisionDetectionMode pieceCollisionDetection =
            CollisionDetectionMode.Continuous;

        [Header("Transform Validation")]
        [SerializeField, Min(0.000001f)]
        private float uniformScaleTolerance = 0.001f;

        private MeshFilter sourceMeshFilter;
        private MeshRenderer sourceMeshRenderer;
        private Rigidbody sourceRigidbody;
        private SliceState state;

        private readonly List<Vector3> lastContourWorld = new();

        public bool IsSliced => state == SliceState.Sliced;
        public string LastMessage { get; private set; } = string.Empty;
        public SliceDiagnostics LastDiagnostics { get; private set; }
        public IReadOnlyList<Vector3> LastContourWorld =>
            lastContourWorld;

        private void Awake()
        {
            sourceMeshFilter = GetComponent<MeshFilter>();
            sourceMeshRenderer = GetComponent<MeshRenderer>();
            sourceRigidbody = GetComponent<Rigidbody>();
            state = SliceState.Ready;
        }

        private void OnValidate()
        {
            sliceSettings = sliceSettings.Sanitized();
            totalPieceMass = Mathf.Max(0.001f, totalPieceMass);
            separationImpulse = Mathf.Max(0f, separationImpulse);
            uniformScaleTolerance =
                Mathf.Max(0.000001f, uniformScaleTolerance);
        }

        public bool TrySlice(in SliceRequest request)
        {
            if (state != SliceState.Ready)
            {
                LastMessage =
                    "This object is already slicing or has been sliced.";

                return false;
            }

            if (!ValidateBeforeSlice(out string validationFailure))
            {
                LastMessage = validationFailure;
                Debug.LogWarning(validationFailure, this);
                return false;
            }

            if (!SlicingMath.IsFinite(request.planeNormalWorld) ||
                request.planeNormalWorld.sqrMagnitude <
                0.00000001f)
            {
                LastMessage =
                    "The requested world plane normal is invalid.";

                return false;
            }

            state = SliceState.Slicing;

            Mesh positiveVisualMesh = null;
            Mesh negativeVisualMesh = null;
            Mesh positiveProxyMesh = null;
            Mesh negativeProxyMesh = null;

            GameObject positiveObject = null;
            GameObject negativeObject = null;

            try
            {
                Vector3 localPlanePoint =
                    transform.InverseTransformPoint(
                        request.contactPointWorld);

                Vector3 localPlaneNormal =
                    transform.InverseTransformDirection(
                        request.planeNormalWorld).normalized;

                Plane localPlane = new(
                    localPlaneNormal,
                    localPlanePoint);

                if (!SphereMeshSlicer.TrySlice(
                        sourceMeshFilter.sharedMesh,
                        localPlane,
                        sliceSettings,
                        out SliceGeometry geometry,
                        out SliceDiagnostics diagnostics))
                {
                    LastDiagnostics = diagnostics;
                    LastMessage = diagnostics.Message;
                    state = SliceState.Ready;

                    Debug.LogWarning(
                        $"Slice rejected: {LastMessage}",
                        this);

                    return false;
                }

                LastDiagnostics = diagnostics;

                positiveVisualMesh =
                    RuntimeMeshFactory.CreateVisualMesh(
                        geometry.Positive,
                        "Sphere_Positive_VisualMesh");

                negativeVisualMesh =
                    RuntimeMeshFactory.CreateVisualMesh(
                        geometry.Negative,
                        "Sphere_Negative_VisualMesh");

                string physicsNote = string.Empty;

                if (addPhysicsToPieces)
                {
                    bool proxySucceeded =
                        LowPolySphereProxyFactory
                            .TryCreateSlicedProxy(
                                sourceMeshFilter.sharedMesh.bounds,
                                localPlane,
                                sliceSettings,
                                out positiveProxyMesh,
                                out negativeProxyMesh,
                                out string proxyFailure);

                    if (!proxySucceeded)
                    {
                        physicsNote =
                            $" Physics proxy fallback: {proxyFailure}";

                        Debug.LogWarning(
                            physicsNote,
                            this);
                    }
                }

                CacheWorldContour(geometry.ContourLocal);

                Material outerMaterial =
                    sourceMeshRenderer.sharedMaterials[0];

                Vector3 inheritedLinearVelocity =
                    sourceRigidbody != null
                        ? sourceRigidbody.linearVelocity
                        : Vector3.zero;

                Vector3 inheritedAngularVelocity =
                    sourceRigidbody != null
                        ? sourceRigidbody.angularVelocity
                        : Vector3.zero;

                positiveObject = CreatePiece(
                    "Sphere_Positive",
                    positiveVisualMesh,
                    positiveProxyMesh,
                    outerMaterial,
                    inheritedLinearVelocity,
                    inheritedAngularVelocity,
                    out Rigidbody positiveBody);

                negativeObject = CreatePiece(
                    "Sphere_Negative",
                    negativeVisualMesh,
                    negativeProxyMesh,
                    outerMaterial,
                    inheritedLinearVelocity,
                    inheritedAngularVelocity,
                    out Rigidbody negativeBody);

                positiveObject.SetActive(true);
                negativeObject.SetActive(true);

                if (addPhysicsToPieces)
                {
                    Vector3 worldPlaneNormal =
                        request.planeNormalWorld.normalized;

                    if (positiveBody != null)
                    {
                        positiveBody.AddForce(
                            worldPlaneNormal *
                            separationImpulse,
                            ForceMode.Impulse);
                    }

                    if (negativeBody != null)
                    {
                        negativeBody.AddForce(
                            -worldPlaneNormal *
                            separationImpulse,
                            ForceMode.Impulse);
                    }
                }

                state = SliceState.Sliced;
                LastMessage =
                    "Slice completed successfully." +
                    physicsNote;

                gameObject.SetActive(false);
                return true;
            }
            catch (Exception exception)
            {
                state = SliceState.Ready;
                LastMessage =
                    $"Slice failed during result creation: " +
                    exception.Message;

                LastDiagnostics ??= new SliceDiagnostics();
                LastDiagnostics.Fail(
                    SliceFailureReason.MeshCreationFailed,
                    LastMessage);

                DestroyRuntimeObject(positiveObject);
                DestroyRuntimeObject(negativeObject);

                DestroyRuntimeMesh(positiveVisualMesh);
                DestroyRuntimeMesh(negativeVisualMesh);
                DestroyRuntimeMesh(positiveProxyMesh);
                DestroyRuntimeMesh(negativeProxyMesh);

                Debug.LogException(exception, this);
                return false;
            }
        }

        private bool ValidateBeforeSlice(
            out string failureMessage)
        {
            failureMessage = string.Empty;

            sourceMeshFilter ??= GetComponent<MeshFilter>();
            sourceMeshRenderer ??= GetComponent<MeshRenderer>();
            sourceRigidbody ??= GetComponent<Rigidbody>();

            if (sourceMeshFilter.sharedMesh == null)
            {
                failureMessage =
                    "SliceableSphere requires a source Mesh.";
                return false;
            }

            if (sourceMeshRenderer.sharedMaterials.Length == 0 ||
                sourceMeshRenderer.sharedMaterials[0] == null)
            {
                failureMessage =
                    "SliceableSphere requires an outer material.";
                return false;
            }

            if (cutSurfaceMaterial == null)
            {
                failureMessage =
                    "Assign Cut Surface Material before slicing.";
                return false;
            }

            if (!IsSupportedTransform(out failureMessage))
            {
                return false;
            }

            return true;
        }

        private bool IsSupportedTransform(
            out string failureMessage)
        {
            Matrix4x4 matrix = transform.localToWorldMatrix;

            Vector3 transformedX =
                matrix.MultiplyVector(Vector3.right);

            Vector3 transformedY =
                matrix.MultiplyVector(Vector3.up);

            Vector3 transformedZ =
                matrix.MultiplyVector(Vector3.forward);

            float scaleX = transformedX.magnitude;
            float scaleY = transformedY.magnitude;
            float scaleZ = transformedZ.magnitude;

            float maximumScale =
                Mathf.Max(scaleX, Mathf.Max(scaleY, scaleZ));

            if (maximumScale <= 0.000001f)
            {
                failureMessage =
                    "The SliceableSphere scale is zero.";
                return false;
            }

            float allowedDifference =
                maximumScale * uniformScaleTolerance;

            if (Mathf.Abs(scaleX - scaleY) >
                    allowedDifference ||
                Mathf.Abs(scaleX - scaleZ) >
                    allowedDifference)
            {
                failureMessage =
                    "This prototype supports uniform scale only.";
                return false;
            }

            Vector3 axisX = transformedX / scaleX;
            Vector3 axisY = transformedY / scaleY;
            Vector3 axisZ = transformedZ / scaleZ;

            if (Mathf.Abs(Vector3.Dot(axisX, axisY)) >
                    uniformScaleTolerance ||
                Mathf.Abs(Vector3.Dot(axisX, axisZ)) >
                    uniformScaleTolerance ||
                Mathf.Abs(Vector3.Dot(axisY, axisZ)) >
                    uniformScaleTolerance)
            {
                failureMessage =
                    "A sheared transform is not supported.";
                return false;
            }

            if (matrix.determinant <= 0f)
            {
                failureMessage =
                    "Negative or reflected scale is not supported.";
                return false;
            }

            failureMessage = string.Empty;
            return true;
        }

        private void CacheWorldContour(
            IReadOnlyList<Vector3> contourLocal)
        {
            lastContourWorld.Clear();

            for (int index = 0;
                 index < contourLocal.Count;
                 index++)
            {
                lastContourWorld.Add(
                    transform.TransformPoint(
                        contourLocal[index]));
            }
        }

        private GameObject CreatePiece(
            string objectName,
            Mesh visualMesh,
            Mesh proxyMesh,
            Material outerMaterial,
            Vector3 inheritedLinearVelocity,
            Vector3 inheritedAngularVelocity,
            out Rigidbody pieceRigidbody)
        {
            GameObject piece = new(objectName);
            piece.SetActive(false);

            piece.layer = gameObject.layer;
            piece.tag = gameObject.tag;

            Transform pieceTransform = piece.transform;
            pieceTransform.SetParent(
                transform.parent,
                worldPositionStays: false);

            pieceTransform.localPosition =
                transform.localPosition;

            pieceTransform.localRotation =
                transform.localRotation;

            pieceTransform.localScale =
                transform.localScale;

            MeshFilter pieceMeshFilter =
                piece.AddComponent<MeshFilter>();

            pieceMeshFilter.sharedMesh = visualMesh;

            MeshRenderer pieceRenderer =
                piece.AddComponent<MeshRenderer>();

            pieceRenderer.sharedMaterials = new[]
            {
                outerMaterial,
                cutSurfaceMaterial
            };

            pieceRigidbody = null;

            if (!addPhysicsToPieces)
            {
                return piece;
            }

            if (proxyMesh != null)
            {
                MeshCollider meshCollider =
                    piece.AddComponent<MeshCollider>();

                meshCollider.sharedMesh = proxyMesh;
                meshCollider.convex = true;
            }
            else
            {
                BoxCollider boxCollider =
                    piece.AddComponent<BoxCollider>();

                boxCollider.center =
                    visualMesh.bounds.center;

                boxCollider.size =
                    visualMesh.bounds.size;
            }

            pieceRigidbody =
                piece.AddComponent<Rigidbody>();

            pieceRigidbody.mass =
                Mathf.Max(0.001f, totalPieceMass * 0.5f);

            pieceRigidbody.interpolation =
                RigidbodyInterpolation.Interpolate;

            pieceRigidbody.collisionDetectionMode =
                pieceCollisionDetection;

            pieceRigidbody.linearVelocity =
                inheritedLinearVelocity;

            pieceRigidbody.angularVelocity =
                inheritedAngularVelocity;

            return piece;
        }

        private static void DestroyRuntimeObject(
            GameObject target)
        {
            if (target == null)
            {
                return;
            }

            target.SetActive(false);

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static void DestroyRuntimeMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }
    }
}