using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ultra-easy AI Traffic Car.
/// Automatically creates waypoints inside the car object,
/// automatically detects wheels, moves the car along the route,
/// and rotates/steers the wheels.
/// </summary>
[SelectionBase]
[DisallowMultipleComponent]
public class EasyAITrafficCar : MonoBehaviour
{
    public enum EndOfRouteAction { Loop, Reverse, Stop }
    public enum WheelRollAxis { X_Axis, Y_Axis, Z_Axis }

    [Header("--- 1. Waypoints ---")]
    [Tooltip("Container holding waypoints. Auto-created inside this car.")]
    public Transform waypointsContainer;
    [Tooltip("List of waypoints. Automatically populated from the container or assigned manually.")]
    public List<Transform> waypoints = new List<Transform>();
    [Tooltip("What happens when the car reaches the final waypoint.")]
    public EndOfRouteAction endOfRoute = EndOfRouteAction.Loop;
    [Tooltip("Distance threshold to trigger the next waypoint.")]
    public float waypointReachDistance = 3f;

    [Header("--- Live Status (Read Only) ---")]
    [Tooltip("Current state of the AI car.")]
    public string statusMessage = "Idle";

    [Header("--- 2. Movement & Speed ---")]
    [Tooltip("Cruising speed of the traffic car.")]
    public float moveSpeed = 12f;
    [Tooltip("How fast the car accelerates to cruising speed.")]
    public float acceleration = 6f;
    [Tooltip("How fast the car brakes.")]
    public float brakeForce = 14f;
    [Tooltip("Turning speed of the car body towards the next waypoint (degrees/sec).")]
    public float turnSpeed = 90f;

    [Header("--- 3. Wheels ---")]
    [Tooltip("Front Left wheel mesh (rolls + steers).")]
    public Transform frontLeftWheel;
    [Tooltip("Front Right wheel mesh (rolls + steers).")]
    public Transform frontRightWheel;
    [Tooltip("Rear Left wheel mesh (rolls).")]
    public Transform rearLeftWheel;
    [Tooltip("Rear Right wheel mesh (rolls).")]
    public Transform rearRightWheel;

    [Header("--- 4. Wheel Settings ---")]
    [Tooltip("Wheel radius in meters. Used for accurate roll speed.")]
    public float wheelRadius = 0.35f;
    [Tooltip("Axis the wheel rolls around. Most 3D models roll on X-Axis.")]
    public WheelRollAxis rollAxis = WheelRollAxis.X_Axis;
    [Tooltip("Maximum steering angle for front wheels.")]
    public float maxSteerAngle = 32f;
    [Tooltip("Steering responsiveness.")]
    public float wheelSteerSpeed = 8f;
    [Tooltip("Check this if right wheels roll backward due to 3D model symmetry.")]
    public bool invertRightWheels = false;

    [Header("--- 5. Road Alignment (Stay on Ground) ---")]
    [Tooltip("Keep the car glued to the road/terrain surface.")]
    public bool snapToGround = true;
    [Tooltip("LayerMask of roads and terrain. Default is Everything.")]
    public LayerMask groundLayer = ~0;
    [Tooltip("Height offset of the car pivot above the ground. Click 'Auto-Calibrate Ground Height' to set this automatically.")]
    public float groundOffset = 0.5f;
    [Tooltip("Max downward distance to search for road.")]
    public float maxGroundSearchDistance = 20f;
    [Tooltip("Align car pitch and roll to match hills and road slopes.")]
    public bool alignWithRoadSlope = true;
    [Tooltip("How quickly car height adjusts to ground changes.")]
    public float groundFollowSpeed = 25f;
    [Tooltip("How quickly car tilts to match road slopes.")]
    public float slopeFollowSpeed = 12f;

    [Header("--- 6. Obstacle Avoidance (Optional) ---")]
    [Tooltip("Detect vehicles or obstacles ahead and brake automatically.")]
    public bool avoidObstacles = true;
    public float sensorDistance = 7f;
    public float sensorRadius = 0.9f;
    public Vector3 sensorOffset = new Vector3(0f, 0.5f, 1.5f);
    public LayerMask obstacleLayers = ~0;

    // Runtime state
    private int currentWaypointIndex = 0;
    private bool isReversingRoute = false;
    private float currentSpeed = 0f;
    private float currentRollAngle = 0f;
    private float currentSteerAngle = 0f;
    private bool isStopped = false;
    private float verticalVelocity = 0f;

    // Base mesh rotations
    private Quaternion flBaseRot = Quaternion.identity;
    private Quaternion frBaseRot = Quaternion.identity;
    private Quaternion rlBaseRot = Quaternion.identity;
    private Quaternion rrBaseRot = Quaternion.identity;

    private Rigidbody rb;

    private void Awake()
    {
        // 1. Detach waypoints container into world space so waypoints don't move with the car!
        if (waypointsContainer != null)
        {
            waypointsContainer.SetParent(null);
        }

        // 2. Cache initial wheel rotations
        if (frontLeftWheel) flBaseRot = frontLeftWheel.localRotation;
        if (frontRightWheel) frBaseRot = frontRightWheel.localRotation;
        if (rearLeftWheel) rlBaseRot = rearLeftWheel.localRotation;
        if (rearRightWheel) rrBaseRot = rearRightWheel.localRotation;

        // 3. Populate waypoints list safely
        RefreshWaypointsList();

        // 4. Remove nulls
        waypoints.RemoveAll(w => w == null);

        // 5. Ensure colliders on waypoint objects are triggers so car doesn't physically get blocked
        DisableWaypointColliders();

        // 6. Reset stopped flag on start
        isStopped = false;

        // 7. Auto-calibrate ground height if not configured
        if (Mathf.Approximately(groundOffset, 0f))
        {
            AutoCalibrateGroundHeight();
        }

        // 8. Ensure Rigidbody doesn't fight transform movement
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }

    /// <summary>
    /// Ensures waypoint objects don't physically block the car if user used Cubes/Spheres with colliders.
    /// </summary>
    public void DisableWaypointColliders()
    {
        if (waypoints == null) return;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            Collider[] cols = waypoints[i].GetComponentsInChildren<Collider>();
            for (int c = 0; c < cols.Length; c++)
            {
                cols[c].isTrigger = true;
            }
        }
    }

    private void Reset()
    {
        // Auto-run helpers when script is added to a car
        AutoDetectWheels();
        AutoCalibrateGroundHeight();
        EnsureWaypointsContainer();
    }

    /// <summary>
    /// Ensures a child container object exists inside the car to hold waypoints.
    /// </summary>
    public Transform EnsureWaypointsContainer()
    {
        if (waypointsContainer == null)
        {
            Transform existing = transform.Find("Waypoints_Route");
            if (existing != null)
            {
                waypointsContainer = existing;
            }
            else
            {
                GameObject container = new GameObject("Waypoints_Route");
                container.transform.SetParent(transform);
                container.transform.localPosition = Vector3.zero;
                container.transform.localRotation = Quaternion.identity;
                waypointsContainer = container.transform;
            }
        }
        RefreshWaypointsList();
        return waypointsContainer;
    }

    /// <summary>
    /// Reads all child objects inside waypointsContainer into the waypoints list.
    /// If user assigned waypoints manually in the Inspector, preserves them.
    /// </summary>
    [ContextMenu("Refresh Waypoints")]
    public void RefreshWaypointsList()
    {
        if (waypointsContainer == null) return;

        // If waypointsContainer is empty but waypoints were assigned manually in the inspector, don't clear!
        if (waypointsContainer.childCount == 0 && waypoints != null && waypoints.Count > 0)
        {
            return;
        }

        if (waypointsContainer.childCount > 0)
        {
            waypoints.Clear();
            foreach (Transform child in waypointsContainer)
            {
                if (child != waypointsContainer)
                {
                    waypoints.Add(child);
                }
            }
        }
    }

    /// <summary>
    /// Creates a new waypoint inside the car's route container and selects it.
    /// </summary>
    [ContextMenu("Add Waypoint")]
    public Transform AddWaypoint()
    {
        EnsureWaypointsContainer();
        RefreshWaypointsList();

        Vector3 spawnPos;
        if (waypoints.Count == 0)
        {
            // First waypoint 6m in front of car
            spawnPos = transform.position + transform.forward * 6f;
        }
        else
        {
            Transform lastWP = waypoints[waypoints.Count - 1];
            Vector3 forwardDir = transform.forward;
            if (waypoints.Count >= 2)
            {
                forwardDir = (lastWP.position - waypoints[waypoints.Count - 2].position).normalized;
                if (forwardDir.sqrMagnitude < 0.001f) forwardDir = transform.forward;
            }
            spawnPos = lastWP.position + forwardDir * 8f;
        }

        GameObject wpObj = new GameObject("WP_" + (waypoints.Count + 1));
        wpObj.transform.position = spawnPos;
        wpObj.transform.rotation = Quaternion.identity;
        wpObj.transform.SetParent(waypointsContainer);

        waypoints.Add(wpObj.transform);
        return wpObj.transform;
    }

    /// <summary>
    /// Clears all waypoints in the container.
    /// </summary>
    [ContextMenu("Clear All Waypoints")]
    public void ClearWaypoints()
    {
        if (waypointsContainer != null)
        {
            List<GameObject> toDestroy = new List<GameObject>();
            foreach (Transform child in waypointsContainer)
            {
                if (child != waypointsContainer)
                {
                    toDestroy.Add(child.gameObject);
                }
            }
            for (int i = toDestroy.Count - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(toDestroy[i]);
                else
                    DestroyImmediate(toDestroy[i]);
            }
        }
        waypoints.Clear();
    }

    /// <summary>
    /// Raycasts all waypoints down to the nearest ground/road.
    /// </summary>
    [ContextMenu("Snap Waypoints To Ground")]
    public void SnapWaypointsToGround()
    {
        RefreshWaypointsList();
        foreach (Transform wp in waypoints)
        {
            if (wp == null) continue;
            if (RaycastGround(wp.position + Vector3.up * 15f, 60f, out RaycastHit hit))
            {
                wp.position = hit.point + Vector3.up * 0.15f;
            }
        }
    }

    /// <summary>
    /// Automatically measures the height of the car pivot above the bottom of the wheels.
    /// Ensures wheels touch the road without sinking or floating.
    /// </summary>
    [ContextMenu("Auto-Calibrate Ground Height")]
    public void AutoCalibrateGroundHeight()
    {
        Transform sampleWheel = frontLeftWheel != null ? frontLeftWheel : (rearLeftWheel != null ? rearLeftWheel : (frontRightWheel != null ? frontRightWheel : rearRightWheel));
        if (sampleWheel != null)
        {
            groundOffset = Mathf.Abs(sampleWheel.localPosition.y) + wheelRadius;
            return;
        }

        Renderer[] rends = GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
            {
                b.Encapsulate(rends[i].bounds);
            }
            groundOffset = transform.position.y - b.min.y;
        }
        else
        {
            groundOffset = 0.5f;
        }
    }

    /// <summary>
    /// Scans child objects and auto-assigns wheels based on common names.
    /// </summary>
    [ContextMenu("Auto-Detect Wheels")]
    public void AutoDetectWheels()
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        foreach (Transform t in children)
        {
            if (t == transform) continue;
            string n = t.name.ToLower();

            // Skip colliders, cameras, lights
            if (n.Contains("collider") || n.Contains("camera") || n.Contains("light")) continue;

            if (frontLeftWheel == null && (n.Contains("front") && n.Contains("left") || n.Contains("wheel_fl") || n.Contains("fl_wheel") || n == "fl" || n.Contains("wheel.l.0")))
                frontLeftWheel = t;
            else if (frontRightWheel == null && (n.Contains("front") && n.Contains("right") || n.Contains("wheel_fr") || n.Contains("fr_wheel") || n == "fr" || n.Contains("wheel.r.0")))
                frontRightWheel = t;
            else if (rearLeftWheel == null && ((n.Contains("rear") || n.Contains("back")) && n.Contains("left") || n.Contains("wheel_rl") || n.Contains("rl_wheel") || n == "rl" || n.Contains("wheel.l.1")))
                rearLeftWheel = t;
            else if (rearRightWheel == null && ((n.Contains("rear") || n.Contains("back")) && n.Contains("right") || n.Contains("wheel_rr") || n.Contains("rr_wheel") || n == "rr" || n.Contains("wheel.r.1")))
                rearRightWheel = t;
        }
    }

    private void Update()
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            statusMessage = "⚠️ No waypoints found! Add waypoints in the Inspector.";
            return;
        }

        // Safety clamp on index
        if (currentWaypointIndex < 0 || currentWaypointIndex >= waypoints.Count)
        {
            currentWaypointIndex = 0;
            isStopped = false;
        }

        Transform targetWaypoint = waypoints[currentWaypointIndex];
        if (targetWaypoint == null)
        {
            // Skip null waypoint
            AdvanceToNextWaypoint();
            return;
        }

        // 1. Obstacle Sensor Check (safely ignores waypoint cubes)
        bool obstacleAhead = CheckForObstacles();

        // 2. Direction and Distance to Waypoint
        Vector3 targetPos = targetWaypoint.position;
        Vector3 flatTarget = new Vector3(targetPos.x, transform.position.y, targetPos.z);
        Vector3 directionToTarget = (flatTarget - transform.position).normalized;
        float distanceToWaypoint = Vector3.Distance(transform.position, flatTarget);

        // 3. Corner Speed Assist (Slow down on sharp 90-degree corners so car takes tight turns without overshooting)
        float angleToTarget = Vector3.Angle(transform.forward, directionToTarget);
        float speedFactor = Mathf.Clamp01(1f - (angleToTarget / 120f));
        float cornerMoveSpeed = Mathf.Lerp(moveSpeed * 0.35f, moveSpeed, speedFactor);

        // 4. Speed Calculation
        float targetSpeed = (obstacleAhead || isStopped) ? 0f : cornerMoveSpeed;
        if (currentSpeed < targetSpeed)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);
        }
        else
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, brakeForce * Time.deltaTime);
        }

        // 5. Steer Car Body
        if (directionToTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(directionToTarget, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        // 6. Move Forward
        transform.position += transform.forward * (currentSpeed * Time.deltaTime);

        // 7. Ground Snapping
        if (snapToGround)
        {
            ApplyGroundSnapping();
        }

        // 8. Advance Waypoint
        if (distanceToWaypoint <= waypointReachDistance)
        {
            AdvanceToNextWaypoint();
        }

        // 9. Wheels Animation (Roll & Steer)
        AnimateWheels(directionToTarget);

        // 10. Live Status Message
        UpdateLiveStatus(obstacleAhead, distanceToWaypoint);
    }

    private void UpdateLiveStatus(bool obstacleAhead, float distanceToWaypoint)
    {
        if (isStopped)
        {
            statusMessage = "🛑 Stopped at end waypoint. (Switch 'End Of Route' to Loop to continue!)";
        }
        else if (obstacleAhead)
        {
            statusMessage = "⚠️ Braking: Obstacle or vehicle detected ahead.";
        }
        else
        {
            statusMessage = $"🚗 Driving to WP {currentWaypointIndex + 1}/{waypoints.Count} ({distanceToWaypoint:F1}m away) [Mode: {endOfRoute}]";
        }
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
            // Ignore own car colliders
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                return false;
            }

            // Ignore waypoint objects (e.g. if waypoints are 3D Cubes with colliders)
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] != null && (hit.transform == waypoints[i] || hit.transform.IsChildOf(waypoints[i])))
                {
                    return false;
                }
            }

            return true;
        }
        return false;
    }

    /// <summary>
    /// Raycasts downward while safely ignoring any colliders belonging to the car itself.
    /// </summary>
    public bool RaycastGround(Vector3 origin, float maxDistance, out RaycastHit closestHit)
    {
        closestHit = new RaycastHit();
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, groundLayer, QueryTriggerInteraction.Ignore);

        float minDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            if (hit.distance < minDistance)
            {
                minDistance = hit.distance;
                closestHit = hit;
                found = true;
            }
        }
        return found;
    }

    private void ApplyGroundSnapping()
    {
        float frontZ = frontLeftWheel != null ? frontLeftWheel.localPosition.z : 1.4f;
        float rearZ = rearLeftWheel != null ? rearLeftWheel.localPosition.z : -1.4f;

        if (frontZ <= 0.1f) frontZ = 1.4f;
        if (rearZ >= -0.1f) rearZ = -1.4f;

        Vector3 frontOrigin = transform.position + transform.forward * frontZ + Vector3.up * 2f;
        Vector3 rearOrigin = transform.position + transform.forward * rearZ + Vector3.up * 2f;

        bool hitFront = RaycastGround(frontOrigin, maxGroundSearchDistance + 2f, out RaycastHit frontHit);
        bool hitRear = RaycastGround(rearOrigin, maxGroundSearchDistance + 2f, out RaycastHit rearHit);

        if (hitFront && hitRear)
        {
            verticalVelocity = 0f;
            float targetGroundY = (frontHit.point.y + rearHit.point.y) * 0.5f + groundOffset;

            Vector3 pos = transform.position;
            pos.y = Mathf.MoveTowards(pos.y, targetGroundY, groundFollowSpeed * Time.deltaTime);
            transform.position = pos;

            if (alignWithRoadSlope)
            {
                Vector3 roadForward = (frontHit.point - rearHit.point).normalized;
                Vector3 roadNormal = (frontHit.normal + rearHit.normal) * 0.5f;
                if (roadForward.sqrMagnitude > 0.001f)
                {
                    Quaternion targetSlopeRot = Quaternion.LookRotation(roadForward, roadNormal);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetSlopeRot, slopeFollowSpeed * Time.deltaTime);
                }
            }
        }
        else if (hitFront || hitRear)
        {
            verticalVelocity = 0f;
            RaycastHit singleHit = hitFront ? frontHit : rearHit;
            float targetGroundY = singleHit.point.y + groundOffset;

            Vector3 pos = transform.position;
            pos.y = Mathf.MoveTowards(pos.y, targetGroundY, groundFollowSpeed * Time.deltaTime);
            transform.position = pos;

            if (alignWithRoadSlope)
            {
                Quaternion targetSlope = Quaternion.FromToRotation(transform.up, singleHit.normal) * transform.rotation;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetSlope, slopeFollowSpeed * Time.deltaTime);
            }
        }
        else
        {
            // Fall under gravity if over a drop or no road detected below
            verticalVelocity -= 9.81f * Time.deltaTime;
            transform.position += Vector3.up * (verticalVelocity * Time.deltaTime);
        }
    }

    private void AnimateWheels(Vector3 directionToTarget)
    {
        // 1. Calculate roll angle
        if (wheelRadius > 0.01f)
        {
            float circumference = 2f * Mathf.PI * wheelRadius;
            float rollDelta = (currentSpeed * Time.deltaTime / circumference) * 360f;
            currentRollAngle = (currentRollAngle + rollDelta) % 360f;
        }

        // 2. Calculate steering angle
        Vector3 localDir = transform.InverseTransformDirection(directionToTarget);
        float desiredSteerAngle = 0f;
        if (localDir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            desiredSteerAngle = Mathf.Clamp(angle, -maxSteerAngle, maxSteerAngle);
        }

        currentSteerAngle = Mathf.Lerp(currentSteerAngle, desiredSteerAngle, wheelSteerSpeed * Time.deltaTime);

        // 3. Roll & Steer Quaternions
        float leftRoll = currentRollAngle;
        float rightRoll = invertRightWheels ? -currentRollAngle : currentRollAngle;

        Quaternion steerRot = Quaternion.Euler(0f, currentSteerAngle, 0f);
        Quaternion rollRotLeft = GetAxisRotation(leftRoll);
        Quaternion rollRotRight = GetAxisRotation(rightRoll);

        // Front wheels (Steer + Roll)
        if (frontLeftWheel)
        {
            frontLeftWheel.localRotation = flBaseRot * steerRot * rollRotLeft;
        }
        if (frontRightWheel)
        {
            frontRightWheel.localRotation = frBaseRot * steerRot * rollRotRight;
        }

        // Rear wheels (Roll only)
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
            case WheelRollAxis.Y_Axis: return Quaternion.Euler(0f, angle, 0f);
            case WheelRollAxis.Z_Axis: return Quaternion.Euler(0f, 0f, angle);
            default: return Quaternion.Euler(angle, 0f, 0f); // X_Axis
        }
    }

    private void OnDrawGizmos()
    {
        // Draw waypoint route in Scene view
        if (waypoints != null && waypoints.Count > 0)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] == null) continue;

                // Draw waypoint sphere
                Gizmos.DrawSphere(waypoints[i].position, 0.45f);

                // Draw line to next waypoint
                int nextIndex = i + 1;
                if (nextIndex < waypoints.Count && waypoints[nextIndex] != null)
                {
                    Gizmos.DrawLine(waypoints[i].position, waypoints[nextIndex].position);
                }
                else if (endOfRoute == EndOfRouteAction.Loop && waypoints.Count > 1 && waypoints[0] != null)
                {
                    Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
                    Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
                    Gizmos.color = Color.cyan;
                }
            }

            // Draw line from car to current target in Play mode
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

        // Draw ground sensors
        if (snapToGround)
        {
            Gizmos.color = Color.magenta;
            float frontZ = frontLeftWheel != null ? frontLeftWheel.localPosition.z : 1.4f;
            float rearZ = rearLeftWheel != null ? rearLeftWheel.localPosition.z : -1.4f;
            if (frontZ <= 0.1f) frontZ = 1.4f;
            if (rearZ >= -0.1f) rearZ = -1.4f;

            Vector3 fPos = transform.position + transform.forward * frontZ;
            Vector3 rPos = transform.position + transform.forward * rearZ;

            Gizmos.DrawLine(fPos + Vector3.up * 1f, fPos - Vector3.up * groundOffset);
            Gizmos.DrawWireSphere(fPos - Vector3.up * groundOffset, 0.15f);

            Gizmos.DrawLine(rPos + Vector3.up * 1f, rPos - Vector3.up * groundOffset);
            Gizmos.DrawWireSphere(rPos - Vector3.up * groundOffset, 0.15f);
        }
    }

    private void OnDestroy()
    {
        // Clean up detached container when car is destroyed
        if (Application.isPlaying && waypointsContainer != null)
        {
            Destroy(waypointsContainer.gameObject);
        }
    }
}
