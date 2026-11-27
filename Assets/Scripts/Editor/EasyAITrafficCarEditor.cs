using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EasyAITrafficCar))]
public class EasyAITrafficCarEditor : Editor
{
    private EasyAITrafficCar car;

    private void OnEnable()
    {
        car = (EasyAITrafficCar)target;
    }

    public override void OnInspectorGUI()
    {
        // 1. Live Status Box in Play Mode
        if (Application.isPlaying)
        {
            MessageType msgType = MessageType.Info;
            if (car.statusMessage.Contains("🛑") || car.statusMessage.Contains("⚠️"))
            {
                msgType = MessageType.Warning;
            }
            EditorGUILayout.HelpBox(car.statusMessage, msgType);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Circuit Mode:", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        // Mode 1: Loop
        GUI.backgroundColor = (car.endOfRoute == EasyAITrafficCar.EndOfRouteAction.Loop) ? new Color(0.2f, 1f, 0.3f) : Color.white;
        if (GUILayout.Button("🔁 LOOP (Continuous)", GUILayout.Height(30)))
        {
            Undo.RecordObject(car, "Set Loop Mode");
            car.endOfRoute = EasyAITrafficCar.EndOfRouteAction.Loop;
            EditorUtility.SetDirty(car);
        }

        // Mode 2: Reverse
        GUI.backgroundColor = (car.endOfRoute == EasyAITrafficCar.EndOfRouteAction.Reverse) ? new Color(1f, 0.85f, 0.2f) : Color.white;
        if (GUILayout.Button("↔️ REVERSE", GUILayout.Height(30)))
        {
            Undo.RecordObject(car, "Set Reverse Mode");
            car.endOfRoute = EasyAITrafficCar.EndOfRouteAction.Reverse;
            EditorUtility.SetDirty(car);
        }

        // Mode 3: Stop
        GUI.backgroundColor = (car.endOfRoute == EasyAITrafficCar.EndOfRouteAction.Stop) ? new Color(1f, 0.4f, 0.4f) : Color.white;
        if (GUILayout.Button("⏹️ STOP AT END", GUILayout.Height(30)))
        {
            Undo.RecordObject(car, "Set Stop Mode");
            car.endOfRoute = EasyAITrafficCar.EndOfRouteAction.Stop;
            EditorUtility.SetDirty(car);
        }

        EditorGUILayout.EndHorizontal();
        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space(6);

        // Draw the default inspector properties
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("--- Quick Waypoint & Setup Actions ---", EditorStyles.boldLabel);

        // Button 1: Add a new waypoint
        GUI.backgroundColor = new Color(0.3f, 1f, 0.4f);
        if (GUILayout.Button("➕ Add Waypoint Ahead", GUILayout.Height(32)))
        {
            Undo.RecordObject(car, "Add Waypoint");
            Transform newWP = car.AddWaypoint();
            if (newWP != null)
            {
                Undo.RegisterCreatedObjectUndo(newWP.gameObject, "Create Waypoint");
                Selection.activeGameObject = newWP.gameObject;
            }
            EditorUtility.SetDirty(car);
        }

        // Button 2: Generate 5 waypoints quickly
        GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        if (GUILayout.Button("✨ Generate 5 Waypoints Route", GUILayout.Height(26)))
        {
            Undo.RecordObject(car, "Generate 5 Waypoints");
            for (int i = 0; i < 5; i++)
            {
                Transform wp = car.AddWaypoint();
                Undo.RegisterCreatedObjectUndo(wp.gameObject, "Create Waypoint");
            }
            EditorUtility.SetDirty(car);
        }

        EditorGUILayout.BeginHorizontal();

        // Button 3: Auto-detect wheels
        GUI.backgroundColor = new Color(1f, 0.9f, 0.3f);
        if (GUILayout.Button("🔍 Auto-Detect Wheels", GUILayout.Height(26)))
        {
            Undo.RecordObject(car, "Auto Detect Wheels");
            car.AutoDetectWheels();
            car.AutoCalibrateGroundHeight();
            EditorUtility.SetDirty(car);
        }

        // Button 4: Auto-Calibrate Ground Height
        GUI.backgroundColor = new Color(0.3f, 0.95f, 0.7f);
        if (GUILayout.Button("🚗 Calibrate Ground Height", GUILayout.Height(26)))
        {
            Undo.RecordObject(car, "Calibrate Ground Height");
            car.AutoCalibrateGroundHeight();
            EditorUtility.SetDirty(car);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        // Button 5: Snap to ground
        GUI.backgroundColor = new Color(0.8f, 0.9f, 1f);
        if (GUILayout.Button("⬇️ Snap Waypoints to Ground", GUILayout.Height(26)))
        {
            Undo.RecordObject(car, "Snap Waypoints to Ground");
            car.SnapWaypointsToGround();
            EditorUtility.SetDirty(car);
        }

        // Button 6: Clear all waypoints
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("❌ Clear Waypoints", GUILayout.Height(26)))
        {
            if (EditorUtility.DisplayDialog("Clear Waypoints", "Are you sure you want to delete all waypoints for this car?", "Yes", "No"))
            {
                Undo.RecordObject(car, "Clear Waypoints");
                car.ClearWaypoints();
                EditorUtility.SetDirty(car);
            }
        }

        EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space(6);
    }

    private void OnSceneGUI()
    {
        if (car == null || car.waypoints == null || car.waypoints.Count == 0) return;

        GUIStyle labelStyle = new GUIStyle();
        labelStyle.normal.textColor = Color.yellow;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.fontSize = 13;

        // Draw numbers & draggable handles for waypoints in Scene view
        for (int i = 0; i < car.waypoints.Count; i++)
        {
            Transform wp = car.waypoints[i];
            if (wp == null) continue;

            // Draw waypoint number badge in Scene view
            Handles.Label(wp.position + Vector3.up * 0.7f, "WP " + (i + 1), labelStyle);

            // Allow dragging waypoints directly from Scene view even when Car is selected!
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(wp.position, wp.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(wp, "Move Waypoint");
                wp.position = newPos;
            }
        }
    }
}
