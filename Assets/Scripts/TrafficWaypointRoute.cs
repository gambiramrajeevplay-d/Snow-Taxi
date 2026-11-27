using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional helper component placed on a parent GameObject to organize and visualize waypoint routes.
/// </summary>
public class TrafficWaypointRoute : MonoBehaviour
{
    [Header("Route Settings")]
    public Color pathColor = Color.cyan;
    public Color waypointColor = Color.yellow;
    public float waypointGizmoRadius = 0.5f;
    public bool isLoop = true;

    [Header("Waypoints List")]
    public List<Transform> waypoints = new List<Transform>();

    private void OnValidate()
    {
        RefreshWaypoints();
    }

    [ContextMenu("Refresh Child Waypoints")]
    public void RefreshWaypoints()
    {
        waypoints.Clear();
        foreach (Transform child in transform)
        {
            if (child != transform)
            {
                waypoints.Add(child);
            }
        }
    }

    [ContextMenu("Snap All Waypoints To Ground")]
    public void SnapWaypointsToGround()
    {
        RefreshWaypoints();
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;

            if (Physics.Raycast(waypoints[i].position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 50f))
            {
                waypoints[i].position = hit.point;
            }
        }
    }

    private void OnDrawGizmos()
    {
        RefreshWaypoints();

        if (waypoints == null || waypoints.Count == 0) return;

        Gizmos.color = waypointColor;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;

            Gizmos.DrawSphere(waypoints[i].position, waypointGizmoRadius);

            Gizmos.color = pathColor;
            int nextIndex = i + 1;
            if (nextIndex < waypoints.Count && waypoints[nextIndex] != null)
            {
                Gizmos.DrawLine(waypoints[i].position, waypoints[nextIndex].position);
            }
            else if (isLoop && waypoints.Count > 1 && waypoints[0] != null)
            {
                Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
            }
            Gizmos.color = waypointColor;
        }
    }
}
