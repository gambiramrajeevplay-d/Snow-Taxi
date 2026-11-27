using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple and lightweight AI Traffic Car script for Unity.
/// Moves the car along assigned waypoints, turns front wheels in the steering direction,
/// and rotates all wheels according to movement speed.
/// </summary>
[SelectionBase]
public class SimpleTrafficCarAI : MonoBehaviour
{
    public enum EndOfRouteAction { Loop, Reverse, Stop }
    public enum WheelRollAxis { X_Axis, Y_Axis, Z_Axis }

    [Header("--- 1. Waypoints Setup ---")]
    [Tooltip("Drag a parent GameObject containing child waypoints here (EASIEST METHOD).")]
    [SerializeField] private Transform waypointsParent;
    [Tooltip("Or manually add individual waypoint Transforms here.")]
    [SerializeField] private List<Transform> waypoints = new List<Transform>();
    [Tooltip("What should the car do when it reaches the last waypoint?")]
    [SerializeField] private EndOfRouteAction endOfRoute = EndOfRouteAction.Loop;
    [Tooltip("How close the car needs to be to a waypoint before switching to the next one.")]
    [SerializeField] private float waypointReachDistance = 3f;

    [Header("--- 2. Speed & Movement ---")]
    [Tooltip("Cruise speed of the traffic car.")]
    [SerializeField] private float moveSpeed = 10f;
    [Tooltip("How fast the car accelerates from a stop.")]
    [SerializeField] private float acceleration = 6f;
    [Tooltip("How fast the car brakes when stopping or near obstacles.")]
    [SerializeField] private float brakeForce = 12f;
    [Tooltip("Turning speed of the car body towards the next waypoint (degrees/sec).")]
    [SerializeField] private float turnSpeed = 90f;

    [Header("--- 3. Wheel Mesh Assignment ---")]
    [Tooltip("Front Left wheel mesh transform (rolls and steers).")]
    [SerializeField] private Transform frontLeftWheel;
    [Tooltip("Front Right wheel mesh transform (rolls and steers).")]
    [SerializeField] private Transform frontRightWheel;
    [Tooltip("Rear Left wheel mesh transform (rolls only).")]
    [SerializeField] private Transform rearLeftWheel;
    [Tooltip("Rear Right wheel mesh transform (rolls only).")]
    [SerializeField] private Transform rearRightWheel;

    [Header("--- 4. Wheel Settings ---")]
    [Tooltip("Radius of the car wheels in meters. Controls how fast wheels visually roll.")]
    [SerializeField] private float wheelRadius = 0.35f;
    [Tooltip("Axis the wheel rolls around. Most 3D car models roll on X-Axis.")]
    [SerializeField] private WheelRollAxis rollAxis = WheelRollAxis.X_Axis;
    [Tooltip("Maximum steering angle for front wheels when turning.")]
    [SerializeField] private float maxSteerAngle = 32f;
    [Tooltip("How quickly front wheels turn towards the steer angle.")]
    [SerializeField] private float wheelSteerSpeed = 8f;
    [Tooltip("Check this if right wheels roll backwards due to mirrored 3D model export.")]
    [SerializeField] private bool invertRightWheels = false;

    [Header("--- 5. Ground Alignment (Optional) ---")]
    [Tooltip("Snaps the car height and slope to the road/ground surface.")]
    [SerializeField] private bool snapToGround = true;
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField] private float raycastDownDistance = 3f;
    [SerializeField] private float groundOffset = 0f;

    [Header("--- 6. Obstacle Avoidance (Optional) ---")]
    [Tooltip("Detect other cars, players, or obstacles ahead and brake automatically.")]
    [SerializeField] private bool avoidObstacles = true;
    [Tooltip("Detection distance in front of the car.")]
    [SerializeField] private float sensorDistance = 6f;
    [Tooltip("Width/radius of the detection beam.")]
    [SerializeField] private float sensorRadius = 0.9f;
    [SerializeField] private Vector3 sensorOffset = new Vector3(0f, 0.5f, 1.5f);
    [SerializeField] private LayerMask obstacleLayers = ~0;

    // Runtime variables
    private int currentWaypointIndex = 0;
    private bool isReversingRoute = false;
    private float currentSpeed = 0f;
    private float currentRollAngle = 0f;
    private float currentSteerAngle = 0f;
    private bool isStopped = false;

    // Initial rotations to support any model orientation
    private Quaternion flBaseRot = Quaternion.identity;
    private Quaternion frBaseRot = Quaternion.identity;
    private Quaternion rlBaseRot = Quaternion.identity;
    private Quaternion rrBaseRot = Quaternion.identity;

    private Rigidbody rb;

    private void Awake()
    {
        // Store base rotations of wheel meshes
        if (frontLeftWheel) flBaseRot = frontLeftWheel.localRotation;
        if (frontRightWheel) frBaseRot = frontRightWheel.localRotation;
        if (rearLeftWheel) rlBaseRot = rearLeftWheel.localRotation;
        if (rearRightWheel) rrBaseRot = rearRightWheel.localRotation;

        // Auto-populate waypoints if parent is assigned
        if (waypoints == null || waypoints.Count == 0)
        {
            LoadWaypointsFromParent();
        }

        // Disable colliders on waypoints so car doesn't collide with them
        if (waypoints != null)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] == null) continue;
                Collider[] cols = waypoints[i].GetComponentsInChildren<Collider>();
                for (int c = 0; c < cols.Length; c++) cols[c].isTrigger = true;
            }
        }

        isStopped = false;

        // Make Rigidbody kinematic if present so it doesn't fight transform movement
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }

    private void OnValidate()
    {
        if (waypointsParent != null && (waypoints == null || waypoints.Count == 0))
        {
            LoadWaypointsFromParent();
        }
    }

    /// <summary>
    /// Loads all child transforms from waypointsParent into the list.
    /// Can also be triggered via right-click context menu in the Inspector.
    /// </summary>
    [ContextMenu("Load Waypoints From Parent")]
    public void LoadWaypointsFromParent()
    {
        if (waypointsParent == null) return;

        waypoints.Clear();

        TrafficWaypointRoute route = waypointsParent.GetComponent<TrafficWaypointRoute>();
        if (route != null && route.waypoints != null && route.waypoints.Count > 0)
        {
            waypoints.AddRange(route.waypoints);
            return;
        }

        foreach (Transform child in waypointsParent)
        {
            if (child != waypointsParent)
            {
                waypoints.Add(child);
            }
        }
    }

    private void Update()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        Transform targetWaypoint = waypoints[currentWaypointIndex];
        if (targetWaypoint == null) return;

        // 1. Check for obstacles ahead
        bool obstacleAhead = CheckForObstacles();

        // 2. Direction and distance to current waypoint
        Vector3 targetPos = targetWaypoint.position;
        Vector3 flatTarget = new Vector3(targetPos.x, transform.position.y, targetPos.z);
        Vector3 directionToTarget = (flatTarget - transform.position).normalized;
        float distanceToWaypoint = Vector3.Distance(transform.position, flatTarget);

        // 3. Corner Speed Assist (Slows down for sharp 90-degree corners in cube loops)
        float angleToTarget = Vector3.Angle(transform.forward, directionToTarget);
        float speedFactor = Mathf.Clamp01(1f - (angleToTarget / 120f));
        float cornerSpeed = Mathf.Lerp(moveSpeed * 0.35f, moveSpeed, speedFactor);

        // 4. Determine target speed
        float targetSpeed = (obstacleAhead || isStopped) ? 0f : cornerSpeed;

        if (currentSpeed < targetSpeed)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);
        }
        else
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, brakeForce * Time.deltaTime);
        }

        // 4. Steer the car body towards the waypoint
        if (directionToTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }

        // 5. Move car forward
        transform.position += transform.forward * (currentSpeed * Time.deltaTime);

        // 6. Optional: Snap to road / terrain surface
        if (snapToGround)
        {
            ApplyGroundSnapping();
        }

        // 7. Check if waypoint reached
        if (distanceToWaypoint <= waypointReachDistance)
        {
            AdvanceToNextWaypoint();
        }

        // 8. Animate Wheels (Roll & Steer)
        AnimateWheels(directionToTarget);
    }

    private void AdvanceToNextWaypoint()
    {
        if (!isReversingRoute)
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= waypoints.Count)
            {
                switch (endOfRoute)
                {
                    case EndOfRouteAction.Loop:
                        currentWaypointIndex = 0;
                        isStopped = false;
                        break;
                    case EndOfRouteAction.Reverse:
                        isReversingRoute = true;
                        currentWaypointIndex = Mathf.Max(0, waypoints.Count - 2);
                        isStopped = false;
                        break;
                    case EndOfRouteAction.Stop:
                        currentWaypointIndex = waypoints.Count - 1;
                        isStopped = true;
                        break;
                }
            }
            else
            {
                isStopped = false;
            }
        }
        else
        {
            currentWaypointIndex--;
            if (currentWaypointIndex < 0)
            {
                switch (endOfRoute)
                {
                    case EndOfRouteAction.Loop:
                    case EndOfRouteAction.Reverse:
                        isReversingRoute = false;
                        currentWaypointIndex = Mathf.Min(1, waypoints.Count - 1);
                        isStopped = false;
                        break;
                    case EndOfRouteAction.Stop:
                        currentWaypointIndex = 0;
                        isStopped = true;
                        break;
                }
            }
            else
            {
                isStopped = false;
            }
        }
    }

    private bool CheckForObstacles()
    {
        if (!avoidObstacles) return false;

        Vector3 origin = transform.TransformPoint(sensorOffset);
        Ray ray = new Ray(origin, transform.forward);

        if (Physics.SphereCast(ray, sensorRadius, out RaycastHit hit, sensorDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            // Ignore own colliders
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                return false;
            }

            // Ignore waypoint objects (e.g. if waypoints are 3D Cubes)
            if (waypoints != null)
            {
                for (int i = 0; i < waypoints.Count; i++)
                {
                    if (waypoints[i] != null && (hit.transform == waypoints[i] || hit.transform.IsChildOf(waypoints[i])))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        return false;
    }

    private void ApplyGroundSnapping()
    {
        Vector3 rayStart = transform.position + Vector3.up * 1f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastDownDistance + 1f, groundLayer, QueryTriggerInteraction.Ignore))
        {
            Vector3 pos = transform.position;
            pos.y = hit.point.y + groundOffset;
            transform.position = pos;

            // Align slope smoothly
            Quaternion targetSlope = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetSlope, 10f * Time.deltaTime);
        }
    }

    private void AnimateWheels(Vector3 directionToTarget)
    {
        // 1. Calculate roll angle based on movement speed
        if (wheelRadius > 0.01f)
        {
            float circumference = 2f * Mathf.PI * wheelRadius;
            float rollDelta = (currentSpeed * Time.deltaTime / circumference) * 360f;
            currentRollAngle = (currentRollAngle + rollDelta) % 360f;
        }

        // 2. Calculate steering angle towards waypoint
        Vector3 localDir = transform.InverseTransformDirection(directionToTarget);
        float desiredSteerAngle = 0f;
        if (localDir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            desiredSteerAngle = Mathf.Clamp(angle, -maxSteerAngle, maxSteerAngle);
        }

        currentSteerAngle = Mathf.Lerp(currentSteerAngle, desiredSteerAngle, wheelSteerSpeed * Time.deltaTime);

        // 3. Construct Rotations based on roll axis
        float leftRoll = currentRollAngle;
        float rightRoll = invertRightWheels ? -currentRollAngle : currentRollAngle;

        Quaternion steerRot = Quaternion.Euler(0f, currentSteerAngle, 0f);
        Quaternion rollRotLeft = GetAxisRotation(leftRoll);
        Quaternion rollRotRight = GetAxisRotation(rightRoll);

        // Apply to Front Wheels (Steer + Roll)
        if (frontLeftWheel)
        {
            frontLeftWheel.localRotation = flBaseRot * steerRot * rollRotLeft;
        }
        if (frontRightWheel)
        {
            frontRightWheel.localRotation = frBaseRot * steerRot * rollRotRight;
        }

        // Apply to Rear Wheels (Roll only)
        if (rearLeftWheel)
        {
            rearLeftWheel.localRotation = rlBaseRot * rollRotLeft;
        }
        if (rearRightWheel)
        {
            rearRightWheel.localRotation = rrBaseRot * rollRotRight;
        }
    }

    private Quaternion GetAxisRotation(float angle)
    {
        switch (rollAxis)
        {
            case WheelRollAxis.Y_Axis:
                return Quaternion.Euler(0f, angle, 0f);
            case WheelRollAxis.Z_Axis:
                return Quaternion.Euler(0f, 0f, angle);
            default: // X_Axis
                return Quaternion.Euler(angle, 0f, 0f);
        }
    }

    private void OnDrawGizmos()
    {
        // Draw waypoint route in Scene view
        if (waypoints != null && waypoints.Count > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] == null) continue;

                Gizmos.DrawSphere(waypoints[i].position, 0.4f);

                int nextIndex = i + 1;
                if (nextIndex < waypoints.Count && waypoints[nextIndex] != null)
                {
                    Gizmos.DrawLine(waypoints[i].position, waypoints[nextIndex].position);
                }
                else if (endOfRoute == EndOfRouteAction.Loop && waypoints.Count > 1 && waypoints[0] != null)
                {
                    Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.4f);
                    Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
                    Gizmos.color = Color.yellow;
                }
            }

            // Draw line to current target in Play mode
            if (Application.isPlaying && currentWaypointIndex < waypoints.Count && waypoints[currentWaypointIndex] != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, waypoints[currentWaypointIndex].position);
            }
        }

        // Draw obstacle avoidance sensor
        if (avoidObstacles)
        {
            Gizmos.color = Color.green;
            Vector3 origin = transform.TransformPoint(sensorOffset);
            Gizmos.DrawRay(origin, transform.forward * sensorDistance);
            Gizmos.DrawWireSphere(origin + transform.forward * sensorDistance, sensorRadius);
        }
    }
}

