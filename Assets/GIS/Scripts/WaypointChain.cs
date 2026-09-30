using UnityEngine;

namespace GIS
{

    public class WaypointChain : MonoBehaviour
    {
        public string highwayType;

        public long sourceWayId;

        public bool isLoop;

        public bool drawGizmos = true;
        public Color gizmoColor = new Color(1f, 0.85f, 0.2f, 0.7f);
        public float waypointRadius = 0.4f;

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            int count = transform.childCount;
            if (count == 0) return;

            Gizmos.color = gizmoColor;
            for (int i = 0; i < count - 1; i++)
                Gizmos.DrawLine(transform.GetChild(i).position, transform.GetChild(i + 1).position);
            if (isLoop && count >= 2)
                Gizmos.DrawLine(transform.GetChild(count - 1).position, transform.GetChild(0).position);

            for (int i = 0; i < count; i++)
                Gizmos.DrawSphere(transform.GetChild(i).position, waypointRadius);
        }
    }
}
