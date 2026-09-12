using System.Collections;
using UnityEngine;

namespace RuntimeMeshSlicing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class KnifeAutoStrikeDriver : MonoBehaviour
    {
        [Header("Automatic Strike")]
        [SerializeField]
        private bool strikeOnStart = true;

        [SerializeField, Min(0f)]
        private float strikeDelay = 0.5f;

        [SerializeField]
        private Vector3 worldStrikeDirection =
            Vector3.down;

        [SerializeField, Min(0f)]
        private float impulse = 6f;

        [SerializeField]
        private Vector3 worldTorqueImpulse =
            Vector3.zero;

        private Rigidbody knifeRigidbody;
        private bool hasStruck;

        private void Awake()
        {
            knifeRigidbody = GetComponent<Rigidbody>();
        }

        private IEnumerator Start()
        {
            if (!strikeOnStart)
            {
                yield break;
            }

            if (strikeDelay > 0f)
            {
                yield return new WaitForSeconds(strikeDelay);
            }

            yield return new WaitForFixedUpdate();
            StrikeNow();
        }

        private void OnValidate()
        {
            strikeDelay = Mathf.Max(0f, strikeDelay);
            impulse = Mathf.Max(0f, impulse);
        }

        [ContextMenu("Strike Now")]
        public void StrikeNow()
        {
            if (hasStruck)
            {
                return;
            }

            knifeRigidbody ??= GetComponent<Rigidbody>();

            if (knifeRigidbody.isKinematic)
            {
                Debug.LogWarning(
                    "KnifeAutoStrikeDriver requires a " +
                    "non-kinematic Rigidbody.",
                    this);

                return;
            }

            if (worldStrikeDirection.sqrMagnitude <
                0.00000001f)
            {
                Debug.LogWarning(
                    "World Strike Direction is zero.",
                    this);

                return;
            }

            hasStruck = true;

            knifeRigidbody.AddForce(
                worldStrikeDirection.normalized * impulse,
                ForceMode.Impulse);

            if (worldTorqueImpulse.sqrMagnitude >
                0.00000001f)
            {
                knifeRigidbody.AddTorque(
                    worldTorqueImpulse,
                    ForceMode.Impulse);
            }
        }
    }
}