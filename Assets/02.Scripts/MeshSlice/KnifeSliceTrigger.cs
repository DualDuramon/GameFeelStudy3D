using UnityEngine;

namespace RuntimeMeshSlicing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class KnifeSliceTrigger : MonoBehaviour
    {
        [Header("Slice Detection")]
        [SerializeField] private BladeAxis _bladeAxis = BladeAxis.LocalX;

        [SerializeField, Min(0f)] private float _minimumSliceSpeed = 2f;

        [SerializeField, Min(0.000001f)] private float _minimumCrossMagnitude = 0.001f;

        [Header("Debug")]
        [SerializeField] private SliceDebugView _debugView;

        private Rigidbody _knifeRigidbody;

        private void Awake()
        {
            _knifeRigidbody = GetComponent<Rigidbody>();
            _debugView ??= GetComponent<SliceDebugView>();
        }

        private void OnValidate()
        {
            _minimumSliceSpeed = Mathf.Max(0f, _minimumSliceSpeed);

            _minimumCrossMagnitude = Mathf.Max(0.000001f, _minimumCrossMagnitude);
        }

        private void OnCollisionEnter(Collision collision)
        {
            SliceableSphere sliceable = collision.collider.GetComponentInParent<SliceableSphere>();

            if (sliceable == null || collision.contactCount == 0)
            {
                return;
            }

            // 충돌 시점의 상대 속도 데이터 사용
            Vector3 relativeVelocity = collision.relativeVelocity;
            float impactSpeed = relativeVelocity.magnitude;

            ContactPoint contact = collision.GetContact(0);
            Vector3 contactPointWorld = contact.point;

            Vector3 bladeAxisWorld = GetBladeAxisWorld();

            Vector3 swingDirectionWorld = impactSpeed > 0.0001f ? relativeVelocity / impactSpeed : Vector3.zero;

            _debugView?.RecordCollision(contactPointWorld, bladeAxisWorld, swingDirectionWorld, relativeVelocity);

            if (impactSpeed < _minimumSliceSpeed)
            {
                _debugView?.RecordRejection($"Impact speed {impactSpeed:F2} is below minimum {_minimumSliceSpeed:F2}.");

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

            SliceRequest request = new(contactPointWorld, bladeAxisWorld, swingDirectionWorld, planeNormalWorld, impactSpeed);
            bool succeeded = sliceable.TrySlice(request);

            if (succeeded)
            {
                _debugView?.RecordContour(sliceable.LastContourWorld);
            }
            else
            {
                _debugView?.RecordRejection(sliceable.LastMessage);
            }
        }

        private Vector3 GetBladeAxisWorld()
        {
            Vector3 localAxis = _bladeAxis switch
            {
                BladeAxis.LocalX => Vector3.right,
                BladeAxis.LocalY => Vector3.up,
                BladeAxis.LocalZ => Vector3.forward,
                _ => Vector3.right
            };

            return transform.TransformDirection(localAxis).normalized;
        }
    }
}