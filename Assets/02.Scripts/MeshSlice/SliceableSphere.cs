using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("cutSurfaceMaterial")]
        [SerializeField] private Material _cutSurfaceMaterial;
        [FormerlySerializedAs("sliceSettings")]
        [SerializeField] private SliceSettings _sliceSettings = SliceSettings.Default;

        [Header("Optional Piece Physics")]
        [FormerlySerializedAs("addPhysicsToPieces")]
        [SerializeField] private bool _addPhysicsToPieces;
        [FormerlySerializedAs("totalPieceMass")]
        [SerializeField, Min(0.001f)] private float _totalPieceMass = 1f;
        [FormerlySerializedAs("separationImpulse")]
        [SerializeField, Min(0f)] private float _separationImpulse = 0.35f;
        [FormerlySerializedAs("pieceCollisionDetection")]
        [SerializeField] private CollisionDetectionMode _pieceCollisionDetection = CollisionDetectionMode.Continuous;

        [Header("Transform Validation")]
        [FormerlySerializedAs("uniformScaleTolerance")]
        [SerializeField, Min(0.000001f)] private float _uniformScaleTolerance = 0.001f;

        [Header("Recursive Slicing")]
        [SerializeField, Min(1)] private int _maximumSliceDepth = 2;
        [SerializeField, Min(0)] private int _currentSliceDepth;
        
        [Header("CameraShaker")]
        [SerializeField] private CameraShakeSignalSender _cameraShakeSignalSender;

        private Mesh _sourceCollisionProxyMesh;
        private MeshFilter _sourceMeshFilter;
        private MeshRenderer _sourceMeshRenderer;
        private Rigidbody _sourceRigidbody;
        private SliceState _state;

        private readonly List<Vector3> _lastContourWorld = new();

        public bool IsSliced => _state == SliceState.Sliced;
        public string LastMessage { get; private set; } = string.Empty;
        public SliceDiagnostics LastDiagnostics { get; private set; }
        public SliceableSphere LastPositiveChild { get; private set; }
        public SliceableSphere LastNegativeChild { get; private set; }
        public bool CanSliceAgain => _currentSliceDepth < _maximumSliceDepth;
        public IReadOnlyList<Vector3> LastContourWorld => _lastContourWorld;


        private void Awake()
        {
            _sourceMeshFilter = GetComponent<MeshFilter>();
            _sourceMeshRenderer = GetComponent<MeshRenderer>();
            _sourceRigidbody = GetComponent<Rigidbody>();
            _cameraShakeSignalSender = GetComponent<CameraShakeSignalSender>();
            _state = SliceState.Ready;
        }

        private void OnValidate()
        {
            _sliceSettings = _sliceSettings.Sanitized();
            _totalPieceMass = Mathf.Max(0.001f, _totalPieceMass);
            _separationImpulse = Mathf.Max(0f, _separationImpulse);
            _uniformScaleTolerance = Mathf.Max(0.000001f, _uniformScaleTolerance);
            _maximumSliceDepth = Mathf.Max(1, _maximumSliceDepth);
            _currentSliceDepth = Mathf.Clamp(_currentSliceDepth, 0, _maximumSliceDepth);
        }

        public bool TrySlice(in SliceRequest request)
        {
            if (_state != SliceState.Ready)
            {
                LastMessage = "This object is already slicing or has been sliced.";
                return false;
            }

            if (_currentSliceDepth >= _maximumSliceDepth)
            {
                LastMessage = "This piece reached its maximum slice depth.";
                return false;
            }

            LastPositiveChild = null;
            LastNegativeChild = null;

            if (!ValidateBeforeSlice(out string validationFailure))
            {
                LastMessage = validationFailure;
                Debug.LogWarning(validationFailure, this);
                return false;
            }

            if (!SlicingMath.IsFinite(request.planeNormalWorld) || request.planeNormalWorld.sqrMagnitude < 0.00000001f)
            {
                LastMessage = "The requested world plane normal is invalid.";
                return false;
            }

            _state = SliceState.Slicing;

            Mesh positiveVisualMesh = null;
            Mesh negativeVisualMesh = null;
            Mesh positiveProxyMesh = null;
            Mesh negativeProxyMesh = null;

            GameObject positiveObject = null;
            GameObject negativeObject = null;

            try
            {
                Vector3 localPlanePoint = transform.InverseTransformPoint(request.contactPointWorld);
                Vector3 localPlaneNormal = transform.InverseTransformDirection(request.planeNormalWorld).normalized;
                Plane localPlane = new(localPlaneNormal, localPlanePoint);

                if (!SphereMeshSlicer.TrySlice(_sourceMeshFilter.sharedMesh, localPlane, _sliceSettings, out SliceGeometry geometry, out SliceDiagnostics diagnostics))
                {
                    LastDiagnostics = diagnostics;
                    LastMessage = diagnostics.Message;
                    _state = SliceState.Ready;

                    Debug.LogWarning($"Slice rejected: {LastMessage}", this);
                    return false;
                }

                LastDiagnostics = diagnostics;

                positiveVisualMesh = RuntimeMeshFactory.CreateVisualMesh(geometry.Positive, "Sphere_Positive_VisualMesh");
                negativeVisualMesh = RuntimeMeshFactory.CreateVisualMesh(geometry.Negative, "Sphere_Negative_VisualMesh");

                string physicsNote = string.Empty;

                bool needsCollisionProxy = _addPhysicsToPieces || _currentSliceDepth + 1 < _maximumSliceDepth;

                if (needsCollisionProxy)
                {
                    bool proxySucceeded;
                    string proxyFailure;

                    if (_sourceCollisionProxyMesh == null)
                    {
                        proxySucceeded = LowPolySphereProxyFactory.TryCreateSlicedProxy(_sourceMeshFilter.sharedMesh.bounds, localPlane, _sliceSettings, out positiveProxyMesh, out negativeProxyMesh, out proxyFailure);
                    }
                    else
                    {
                        proxySucceeded = LowPolySphereProxyFactory.TrySliceExistingProxy(_sourceCollisionProxyMesh, localPlane, _sliceSettings, out positiveProxyMesh, out negativeProxyMesh, out proxyFailure);
                    }

                    if (!proxySucceeded)
                    {
                        physicsNote = $" Physics proxy fallback: {proxyFailure}";
                        Debug.LogWarning(physicsNote, this);
                    }
                }

                CacheWorldContour(geometry.ContourLocal);

                Material outerMaterial = _sourceMeshRenderer.sharedMaterials[0];

                Vector3 inheritedLinearVelocity = _sourceRigidbody != null ? _sourceRigidbody.linearVelocity : Vector3.zero;
                Vector3 inheritedAngularVelocity = _sourceRigidbody != null ? _sourceRigidbody.angularVelocity : Vector3.zero;

                positiveObject = CreatePiece("Sphere_Positive", positiveVisualMesh, positiveProxyMesh, outerMaterial, inheritedLinearVelocity, inheritedAngularVelocity, out Rigidbody positiveBody);
                negativeObject = CreatePiece("Sphere_Negative", negativeVisualMesh, negativeProxyMesh, outerMaterial, inheritedLinearVelocity, inheritedAngularVelocity, out Rigidbody negativeBody);
                LastPositiveChild = ConfigureChildSliceable(positiveObject, positiveProxyMesh);
                LastNegativeChild = ConfigureChildSliceable(negativeObject, negativeProxyMesh);

                positiveObject.SetActive(true);
                negativeObject.SetActive(true);

                if (_addPhysicsToPieces)
                {
                    Vector3 worldPlaneNormal = request.planeNormalWorld.normalized;

                    if (positiveBody != null)
                    {
                        positiveBody.AddForce(worldPlaneNormal * _separationImpulse, ForceMode.Impulse);
                    }

                    if (negativeBody != null)
                    {
                        negativeBody.AddForce(-worldPlaneNormal * _separationImpulse, ForceMode.Impulse);
                    }
                }

                _state = SliceState.Sliced;
                LastMessage = $"Slice completed successfully.{physicsNote}";
                
                if (_cameraShakeSignalSender != null)
                {
                    _cameraShakeSignalSender.SendShakeSignal();
                }
                gameObject.SetActive(false);
                return true;
            }
            catch (Exception exception)
            {
                _state = SliceState.Ready;
                LastMessage = $"Slice failed during result creation: {exception.Message}";

                LastDiagnostics ??= new SliceDiagnostics();
                LastDiagnostics.Fail(SliceFailureReason.MeshCreationFailed, LastMessage);

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

        private bool ValidateBeforeSlice(out string failureMessage)
        {
            failureMessage = string.Empty;

            _sourceMeshFilter ??= GetComponent<MeshFilter>();
            _sourceMeshRenderer ??= GetComponent<MeshRenderer>();
            _sourceRigidbody ??= GetComponent<Rigidbody>();

            if (_sourceMeshFilter.sharedMesh == null)
            {
                failureMessage = "SliceableSphere requires a source Mesh.";
                return false;
            }

            if (_sourceMeshRenderer.sharedMaterials.Length == 0 || _sourceMeshRenderer.sharedMaterials[0] == null)
            {
                failureMessage = "SliceableSphere requires an outer material.";
                return false;
            }

            if (_cutSurfaceMaterial == null)
            {
                failureMessage = "Assign Cut Surface Material before slicing.";
                return false;
            }

            if (!IsSupportedTransform(out failureMessage))
            {
                return false;
            }

            return true;
        }

        private bool IsSupportedTransform(out string failureMessage)
        {
            Matrix4x4 matrix = transform.localToWorldMatrix;

            Vector3 transformedX = matrix.MultiplyVector(Vector3.right);
            Vector3 transformedY = matrix.MultiplyVector(Vector3.up);
            Vector3 transformedZ = matrix.MultiplyVector(Vector3.forward);

            float scaleX = transformedX.magnitude;
            float scaleY = transformedY.magnitude;
            float scaleZ = transformedZ.magnitude;

            float maximumScale = Mathf.Max(scaleX, Mathf.Max(scaleY, scaleZ));

            if (maximumScale <= 0.000001f)
            {
                failureMessage = "The SliceableSphere scale is zero.";
                return false;
            }

            float allowedDifference = maximumScale * _uniformScaleTolerance;

            if (Mathf.Abs(scaleX - scaleY) > allowedDifference || Mathf.Abs(scaleX - scaleZ) > allowedDifference)
            {
                failureMessage = "This prototype supports uniform scale only.";
                return false;
            }

            Vector3 axisX = transformedX / scaleX;
            Vector3 axisY = transformedY / scaleY;
            Vector3 axisZ = transformedZ / scaleZ;

            if (Mathf.Abs(Vector3.Dot(axisX, axisY)) > _uniformScaleTolerance || Mathf.Abs(Vector3.Dot(axisX, axisZ)) > _uniformScaleTolerance || Mathf.Abs(Vector3.Dot(axisY, axisZ)) > _uniformScaleTolerance)
            {
                failureMessage = "A sheared transform is not supported.";
                return false;
            }

            if (matrix.determinant <= 0f)
            {
                failureMessage = "Negative or reflected scale is not supported.";
                return false;
            }

            failureMessage = string.Empty;
            return true;
        }

        private void CacheWorldContour(IReadOnlyList<Vector3> contourLocal)
        {
            _lastContourWorld.Clear();

            for (int index = 0; index < contourLocal.Count; index++)
            {
                _lastContourWorld.Add(transform.TransformPoint(contourLocal[index]));
            }
        }

        private GameObject CreatePiece(string objectName, Mesh visualMesh, Mesh proxyMesh, Material outerMaterial, Vector3 inheritedLinearVelocity, Vector3 inheritedAngularVelocity, out Rigidbody pieceRigidbody)
        {
            GameObject piece = new(objectName);
            piece.SetActive(false);

            piece.layer = gameObject.layer;
            piece.tag = gameObject.tag;

            Transform pieceTransform = piece.transform;
            pieceTransform.SetParent(transform.parent, worldPositionStays: false);
            pieceTransform.localPosition = transform.localPosition;
            pieceTransform.localRotation = transform.localRotation;
            pieceTransform.localScale = transform.localScale;

            MeshFilter pieceMeshFilter = piece.AddComponent<MeshFilter>();
            pieceMeshFilter.sharedMesh = visualMesh;

            MeshRenderer pieceRenderer = piece.AddComponent<MeshRenderer>();
            pieceRenderer.sharedMaterials = new[] { outerMaterial, _cutSurfaceMaterial };

            pieceRigidbody = null;

            if (proxyMesh != null)
            {
                MeshCollider meshCollider = piece.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = proxyMesh;
                meshCollider.convex = _addPhysicsToPieces;
            }
            else
            {
                BoxCollider boxCollider = piece.AddComponent<BoxCollider>();
                boxCollider.center = visualMesh.bounds.center;
                boxCollider.size = visualMesh.bounds.size;
            }

            if (!_addPhysicsToPieces)
            {
                return piece;
            }

            pieceRigidbody = piece.AddComponent<Rigidbody>();
            pieceRigidbody.mass = Mathf.Max(0.001f, _totalPieceMass * 0.5f);
            pieceRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            pieceRigidbody.collisionDetectionMode = _pieceCollisionDetection;
            pieceRigidbody.linearVelocity = inheritedLinearVelocity;
            pieceRigidbody.angularVelocity = inheritedAngularVelocity;

            return piece;
        }

        private static void DestroyRuntimeObject(GameObject target)
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

        private SliceableSphere ConfigureChildSliceable(GameObject piece, Mesh collisionProxyMesh)
        {
            int childDepth = _currentSliceDepth + 1;

            if (childDepth >= _maximumSliceDepth)
            {
                return null;
            }

            SliceableSphere child = piece.AddComponent<SliceableSphere>();
            child._cutSurfaceMaterial = _cutSurfaceMaterial;
            child._sliceSettings = _sliceSettings;
            child._addPhysicsToPieces = _addPhysicsToPieces;
            child._totalPieceMass = Mathf.Max(0.001f, _totalPieceMass * 0.5f);
            child._separationImpulse = _separationImpulse;
            child._pieceCollisionDetection = _pieceCollisionDetection;
            child._uniformScaleTolerance = _uniformScaleTolerance;
            child._maximumSliceDepth = _maximumSliceDepth;
            child._currentSliceDepth = childDepth;
            child._sourceCollisionProxyMesh = collisionProxyMesh;
            return child;
        }
    }
}
