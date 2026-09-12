using System.Collections.Generic;
using UnityEngine;

namespace RuntimeMeshSlicing
{
    [DisallowMultipleComponent]
    public sealed class AnimatedBladeSlicer : MonoBehaviour
    {
        [Header("Blade Geometry")]
        [SerializeField] private Transform _bladeBase;

        [SerializeField] private Transform _bladeTip;

        [SerializeField, Min(0.001f)] private float _bladeRadius = 0.04f;

        [Header("Slice Detection")]
        [SerializeField] private LayerMask _sliceableLayers = ~0;

        [SerializeField] private QueryTriggerInteraction _triggerInteraction = QueryTriggerInteraction.Collide;

        [SerializeField, Min(0f)] private float _minimumSliceSpeed = 2f;

        [SerializeField, Min(0.000001f)] private float _minimumCrossMagnitude = 0.001f;

        [Header("Sweep Sampling")]
        [SerializeField, Min(0.001f)] private float _maximumSampleTravel = 0.03f;

        [SerializeField, Range(1, 128)] private int _maximumSweepSamples = 32;

        [SerializeField, Range(1, 256)] private int _overlapBufferSize = 32;

        [Header("Debug")]
        [SerializeField] private SliceDebugView _debugView;

        private readonly HashSet<int> _slicedTargetsThisSwing = new HashSet<int>();
        private Collider[] _overlapBuffer;
        private Vector3 _previousBaseWorld;
        private Vector3 _previousTipWorld;
        private bool _sliceWindowOpen;
        private bool _hasPreviousPose;
        private bool _warnedAboutMissingBladePoints;

        public bool IsSliceWindowOpen => _sliceWindowOpen;

        private void Awake()
        {
            _overlapBuffer = new Collider[_overlapBufferSize];
            _debugView ??= GetComponent<SliceDebugView>();
            CacheCurrentPose();
        }

        private void OnEnable()
        {
            CacheCurrentPose();
        }

        private void OnDisable()
        {
            EndSliceWindow();
        }

        private void OnValidate()
        {
            _bladeRadius = Mathf.Max(0.001f, _bladeRadius);
            _minimumSliceSpeed = Mathf.Max(0f, _minimumSliceSpeed);
            _minimumCrossMagnitude = Mathf.Max(0.000001f, _minimumCrossMagnitude);
            _maximumSampleTravel = Mathf.Max(0.001f, _maximumSampleTravel);
            _maximumSweepSamples = Mathf.Clamp(_maximumSweepSamples, 1, 128);
            _overlapBufferSize = Mathf.Clamp(_overlapBufferSize, 1, 256);

            if (Application.isPlaying && (_overlapBuffer == null || _overlapBuffer.Length != _overlapBufferSize))
            {
                _overlapBuffer = new Collider[_overlapBufferSize];
            }
        }

        private void LateUpdate()
        {
            if (!TryGetBladePose(out Vector3 currentBaseWorld, out Vector3 currentTipWorld))
            {
                return;
            }

            if (!_sliceWindowOpen || !_hasPreviousPose)
            {
                SetPreviousPose(currentBaseWorld, currentTipWorld);
                return;
            }

            float deltaTime = Time.deltaTime;

            if (deltaTime <= 0.000001f)
            {
                SetPreviousPose(currentBaseWorld, currentTipWorld);
                return;
            }

            Vector3 baseVelocityWorld = (currentBaseWorld - _previousBaseWorld) / deltaTime;
            Vector3 tipVelocityWorld = (currentTipWorld - _previousTipWorld) / deltaTime;
            float maximumTravel = Mathf.Max(Vector3.Distance(_previousBaseWorld, currentBaseWorld), Vector3.Distance(_previousTipWorld, currentTipWorld));
            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(maximumTravel / _maximumSampleTravel), 1, _maximumSweepSamples);

            for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                float interpolation = sampleIndex / (float)sampleCount;
                Vector3 sampledBaseWorld = Vector3.Lerp(_previousBaseWorld, currentBaseWorld, interpolation);
                Vector3 sampledTipWorld = Vector3.Lerp(_previousTipWorld, currentTipWorld, interpolation);
                DetectSlicesAtPose(sampledBaseWorld, sampledTipWorld, baseVelocityWorld, tipVelocityWorld);
            }

            SetPreviousPose(currentBaseWorld, currentTipWorld);
        }

        public void BeginSliceWindow()
        {
            _slicedTargetsThisSwing.Clear();
            _sliceWindowOpen = true;
            CacheCurrentPose();
        }

        public void EndSliceWindow()
        {
            _sliceWindowOpen = false;
            _hasPreviousPose = false;
        }

        private void DetectSlicesAtPose(Vector3 sampledBaseWorld, Vector3 sampledTipWorld, Vector3 baseVelocityWorld, Vector3 tipVelocityWorld)
        {
            EnsureOverlapBuffer();
            int overlapCount = Physics.OverlapCapsuleNonAlloc(sampledBaseWorld, sampledTipWorld, _bladeRadius, _overlapBuffer, _sliceableLayers, _triggerInteraction);

            if (overlapCount == _overlapBuffer.Length)
            {
                Debug.LogWarning($"AnimatedBladeSlicer overlap buffer is full ({_overlapBuffer.Length}). Increase Overlap Buffer Size.", this);
            }

            for (int overlapIndex = 0; overlapIndex < overlapCount; overlapIndex++)
            {
                Collider hitCollider = _overlapBuffer[overlapIndex];

                if (hitCollider == null)
                {
                    continue;
                }

                TrySliceCollider(hitCollider, sampledBaseWorld, sampledTipWorld, baseVelocityWorld, tipVelocityWorld);
            }
        }

        private void TrySliceCollider(Collider hitCollider, Vector3 sampledBaseWorld, Vector3 sampledTipWorld, Vector3 baseVelocityWorld, Vector3 tipVelocityWorld)
        {
            SliceableSphere sliceable = hitCollider.GetComponentInParent<SliceableSphere>();

            if (sliceable == null || sliceable.IsSliced)
            {
                return;
            }

            int targetId = sliceable.GetInstanceID();

            if (_slicedTargetsThisSwing.Contains(targetId))
            {
                return;
            }

            Vector3 bladeVectorWorld = sampledTipWorld - sampledBaseWorld;
            float bladeLength = bladeVectorWorld.magnitude;

            if (bladeLength <= 0.000001f)
            {
                _debugView?.RecordRejection("Blade Base and Blade Tip are at the same position.");
                return;
            }

            Vector3 bladeAxisWorld = bladeVectorWorld / bladeLength;
            Vector3 approximateTargetPoint = hitCollider.bounds.ClosestPoint((sampledBaseWorld + sampledTipWorld) * 0.5f);
            float bladeInterpolation = Mathf.Clamp01(Vector3.Dot(approximateTargetPoint - sampledBaseWorld, bladeAxisWorld) / bladeLength);
            Vector3 closestBladePoint = Vector3.Lerp(sampledBaseWorld, sampledTipWorld, bladeInterpolation);
            Vector3 contactPointWorld = hitCollider.ClosestPoint(closestBladePoint);
            bladeInterpolation = Mathf.Clamp01(Vector3.Dot(contactPointWorld - sampledBaseWorld, bladeAxisWorld) / bladeLength);

            Vector3 swingVelocityWorld = Vector3.Lerp(baseVelocityWorld, tipVelocityWorld, bladeInterpolation);
            float sliceSpeed = swingVelocityWorld.magnitude;
            Vector3 swingDirectionWorld = sliceSpeed > 0.000001f ? swingVelocityWorld / sliceSpeed : Vector3.zero;

            _debugView?.RecordCollision(contactPointWorld, bladeAxisWorld, swingDirectionWorld, swingVelocityWorld);

            if (sliceSpeed < _minimumSliceSpeed)
            {
                _debugView?.RecordRejection($"Blade speed {sliceSpeed:F2} is below minimum {_minimumSliceSpeed:F2}.");
                return;
            }

            Vector3 crossProduct = Vector3.Cross(bladeAxisWorld, swingDirectionWorld);

            if (crossProduct.sqrMagnitude < _minimumCrossMagnitude * _minimumCrossMagnitude)
            {
                _debugView?.RecordRejection("Blade axis and swing direction are parallel; the slice plane is undefined.");
                return;
            }

            Vector3 planeNormalWorld = crossProduct.normalized;
            _debugView?.RecordPlane(contactPointWorld, planeNormalWorld);

            SliceRequest request = new(contactPointWorld, bladeAxisWorld, swingDirectionWorld, planeNormalWorld, sliceSpeed);
            bool succeeded = sliceable.TrySlice(request);

            if (succeeded)
            {
                _slicedTargetsThisSwing.Add(targetId);
                _debugView?.RecordContour(sliceable.LastContourWorld);
                return;
            }

            _debugView?.RecordRejection(sliceable.LastMessage);
        }

        private void CacheCurrentPose()
        {
            if (!TryGetBladePose(out Vector3 currentBaseWorld, out Vector3 currentTipWorld))
            {
                _hasPreviousPose = false;
                return;
            }

            SetPreviousPose(currentBaseWorld, currentTipWorld);
        }

        private bool TryGetBladePose(out Vector3 baseWorld, out Vector3 tipWorld)
        {
            if (_bladeBase == null || _bladeTip == null)
            {
                baseWorld = Vector3.zero;
                tipWorld = Vector3.zero;

                if (!_warnedAboutMissingBladePoints)
                {
                    _warnedAboutMissingBladePoints = true;
                    Debug.LogWarning("Assign both Blade Base and Blade Tip to AnimatedBladeSlicer.", this);
                }

                return false;
            }

            _warnedAboutMissingBladePoints = false;
            baseWorld = _bladeBase.position;
            tipWorld = _bladeTip.position;
            return true;
        }

        private void SetPreviousPose(Vector3 baseWorld, Vector3 tipWorld)
        {
            _previousBaseWorld = baseWorld;
            _previousTipWorld = tipWorld;
            _hasPreviousPose = true;
        }

        private void EnsureOverlapBuffer()
        {
            if (_overlapBuffer == null || _overlapBuffer.Length != _overlapBufferSize)
            {
                _overlapBuffer = new Collider[_overlapBufferSize];
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_bladeBase == null || _bladeTip == null)
            {
                return;
            }

            Gizmos.color = _sliceWindowOpen ? Color.red : Color.cyan;
            Gizmos.DrawLine(_bladeBase.position, _bladeTip.position);
            Gizmos.DrawWireSphere(_bladeBase.position, _bladeRadius);
            Gizmos.DrawWireSphere(_bladeTip.position, _bladeRadius);
        }
    }
}
