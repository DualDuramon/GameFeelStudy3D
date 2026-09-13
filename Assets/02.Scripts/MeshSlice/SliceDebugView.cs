using System.Collections.Generic;
using UnityEngine;

namespace RuntimeMeshSlicing
{
    [DisallowMultipleComponent]
    public sealed class SliceDebugView : MonoBehaviour
    {
        [Header("Visibility")]
        [SerializeField]
        private bool showDebug = true;

        [SerializeField, Min(0.01f)]
        private float vectorLength = 0.8f;

        [SerializeField, Min(0.01f)]
        private float planeSize = 1.5f;

        [SerializeField, Range(1, 10)]
        private int planeGridDivisions = 4;

        [SerializeField, Min(0.001f)]
        private float contactRadius = 0.04f;

        [Header("Colors")]
        [SerializeField]
        private Color contactColor = Color.yellow;

        [SerializeField]
        private Color bladeAxisColor = Color.blue;

        [SerializeField]
        private Color swingDirectionColor = Color.green;

        [SerializeField]
        private Color planeNormalColor = Color.red;

        [SerializeField]
        private Color contourColor = Color.magenta;

        [SerializeField]
        private Color planeColor =
            new(1f, 0.3f, 0.3f, 0.6f);

        [Header("Last Result")]
        [SerializeField, TextArea]
        private string lastStatus = "No collision recorded.";

        private bool hasCollision;
        private bool hasPlane;
        private bool wasRejected;

        private Vector3 contactPointWorld;
        private Vector3 bladeAxisWorld;
        private Vector3 swingDirectionWorld;
        private Vector3 planeNormalWorld;
        private Vector3 reportedRelativeVelocity;

        private readonly List<Vector3> contourWorld = new();

        public string LastStatus => lastStatus;

        public void RecordCollision(
            Vector3 contactPoint,
            Vector3 bladeAxis,
            Vector3 swingDirection,
            Vector3 collisionRelativeVelocity)
        {
            hasCollision = true;
            hasPlane = false;
            wasRejected = false;

            contactPointWorld = contactPoint;
            bladeAxisWorld = bladeAxis;
            swingDirectionWorld = swingDirection;
            reportedRelativeVelocity =
                collisionRelativeVelocity;

            contourWorld.Clear();
            lastStatus = "Collision recorded.";
        }

        public void RecordPlane(
            Vector3 planePoint,
            Vector3 planeNormal)
        {
            hasPlane = true;
            wasRejected = false;

            contactPointWorld = planePoint;
            planeNormalWorld = planeNormal.normalized;

            lastStatus = "Slice plane created.";
        }

        public void RecordContour(
            IReadOnlyList<Vector3> points)
        {
            contourWorld.Clear();

            if (points != null)
            {
                for (int index = 0;
                     index < points.Count;
                     index++)
                {
                    contourWorld.Add(points[index]);
                }
            }

            wasRejected = false;
            lastStatus =
                $"Slice succeeded. Contour points: " +
                $"{contourWorld.Count}.";
        }

        public void RecordRejection(string reason)
        {
            wasRejected = true;
            contourWorld.Clear();

            lastStatus =
                string.IsNullOrWhiteSpace(reason)
                    ? "Slice rejected."
                    : $"Slice rejected: {reason}";

            Debug.LogWarning(lastStatus, this);
        }

        private void OnDrawGizmos()
        {
            if (!showDebug || !hasCollision)
            {
                return;
            }

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = Matrix4x4.identity;

            Gizmos.color = contactColor;
            Gizmos.DrawSphere(
                contactPointWorld,
                contactRadius);

            DrawArrow(
                contactPointWorld,
                bladeAxisWorld,
                vectorLength,
                bladeAxisColor);

            DrawArrow(
                contactPointWorld,
                swingDirectionWorld,
                vectorLength,
                swingDirectionColor);

            if (hasPlane)
            {
                DrawArrow(
                    contactPointWorld,
                    planeNormalWorld,
                    vectorLength,
                    planeNormalColor);

                DrawPlaneGrid();
            }

            DrawContour();

            if (wasRejected)
            {
                Gizmos.color = Color.red;

                Vector3 offsetA =
                    (Vector3.right + Vector3.up).normalized *
                    contactRadius * 2f;

                Vector3 offsetB =
                    (Vector3.right - Vector3.up).normalized *
                    contactRadius * 2f;

                Gizmos.DrawLine(
                    contactPointWorld - offsetA,
                    contactPointWorld + offsetA);

                Gizmos.DrawLine(
                    contactPointWorld - offsetB,
                    contactPointWorld + offsetB);
            }

            Gizmos.color = previousColor;
            Gizmos.matrix = previousMatrix;
        }

        private void DrawPlaneGrid()
        {
            BuildPlaneBasis(
                planeNormalWorld,
                out Vector3 basisU,
                out Vector3 basisV);

            Gizmos.color = planeColor;

            float halfSize = planeSize * 0.5f;
            int divisions = Mathf.Max(1, planeGridDivisions);

            for (int division = 0;
                 division <= divisions;
                 division++)
            {
                float interpolation =
                    division / (float)divisions;

                float offset =
                    Mathf.Lerp(
                        -halfSize,
                        halfSize,
                        interpolation);

                Vector3 horizontalStart =
                    contactPointWorld +
                    basisV * offset -
                    basisU * halfSize;

                Vector3 horizontalEnd =
                    contactPointWorld +
                    basisV * offset +
                    basisU * halfSize;

                Vector3 verticalStart =
                    contactPointWorld +
                    basisU * offset -
                    basisV * halfSize;

                Vector3 verticalEnd =
                    contactPointWorld +
                    basisU * offset +
                    basisV * halfSize;

                Gizmos.DrawLine(
                    horizontalStart,
                    horizontalEnd);

                Gizmos.DrawLine(
                    verticalStart,
                    verticalEnd);
            }
        }

        private void DrawContour()
        {
            if (contourWorld.Count < 2)
            {
                return;
            }

            Gizmos.color = contourColor;

            for (int index = 0;
                 index < contourWorld.Count;
                 index++)
            {
                Vector3 current = contourWorld[index];
                Vector3 next =
                    contourWorld[
                        (index + 1) %
                        contourWorld.Count];

                Gizmos.DrawSphere(
                    current,
                    contactRadius * 0.5f);

                Gizmos.DrawLine(current, next);
            }
        }

        private static void DrawArrow(
            Vector3 origin,
            Vector3 direction,
            float length,
            Color color)
        {
            if (direction.sqrMagnitude <
                0.00000001f)
            {
                return;
            }

            Vector3 normalizedDirection =
                direction.normalized;

            Vector3 end =
                origin + normalizedDirection * length;

            Gizmos.color = color;
            Gizmos.DrawLine(origin, end);

            Vector3 perpendicular =
                Vector3.Cross(
                    normalizedDirection,
                    Vector3.up);

            if (perpendicular.sqrMagnitude <
                0.00000001f)
            {
                perpendicular =
                    Vector3.Cross(
                        normalizedDirection,
                        Vector3.right);
            }

            perpendicular.Normalize();

            float headLength = length * 0.18f;
            Vector3 headBase =
                end -
                normalizedDirection * headLength;

            Gizmos.DrawLine(
                end,
                headBase +
                perpendicular * headLength * 0.5f);

            Gizmos.DrawLine(
                end,
                headBase -
                perpendicular * headLength * 0.5f);
        }

        private static void BuildPlaneBasis(
            Vector3 normal,
            out Vector3 basisU,
            out Vector3 basisV)
        {
            Vector3 helper =
                Mathf.Abs(Vector3.Dot(
                    normal,
                    Vector3.up)) < 0.9f
                    ? Vector3.up
                    : Vector3.right;

            basisU =
                Vector3.Cross(helper, normal).normalized;

            basisV =
                Vector3.Cross(normal, basisU).normalized;
        }
    }
}