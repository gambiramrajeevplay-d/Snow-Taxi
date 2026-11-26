namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;
    using UnityEditor;
    using UnityEditor.SceneManagement;

    public static class FixTrafficLooping
    {
        [MenuItem("Traffic/Verify and Fix Route Looping")]
        public static void VerifyAndFixRoutes()
        {
            var routes = Object.FindObjectsOfType<AITrafficWaypointRoute>();
            if (routes == null || routes.Length == 0)
            {
                EditorUtility.DisplayDialog("Traffic Verification", "No AITrafficWaypointRoute found in the current scene.", "OK");
                return;
            }

            int fixedCount = 0;
            string report = "";

            foreach (var route in routes)
            {
                if (route.waypointDataList == null || route.waypointDataList.Count < 2)
                {
                    report += $"Route '{route.name}' has fewer than 2 waypoints.\n";
                    continue;
                }

                int totalWps = route.waypointDataList.Count;
                var firstWp = route.waypointDataList[0]._waypoint;
                var lastWp = route.waypointDataList[totalWps - 1]._waypoint;

                if (firstWp == null || lastWp == null)
                {
                    report += $"Route '{route.name}' has null waypoint references at ends.\n";
                    continue;
                }

                bool hasLoop = false;
                if (lastWp.onReachWaypointSettings.newRoutePoints != null)
                {
                    foreach (var nrp in lastWp.onReachWaypointSettings.newRoutePoints)
                    {
                        if (nrp == firstWp)
                        {
                            hasLoop = true;
                            break;
                        }
                    }
                }

                if (!hasLoop)
                {
                    Undo.RecordObject(lastWp, "Fix Route Looping");
                    lastWp.onReachWaypointSettings.newRoutePoints = new AITrafficWaypoint[] { firstWp };
                    lastWp.onReachWaypointSettings.nextPointInRoute = firstWp;
                    EditorUtility.SetDirty(lastWp);
                    EditorSceneManager.MarkSceneDirty(lastWp.gameObject.scene);
                    fixedCount++;
                    report += $"Route '{route.name}': Connected last waypoint '{lastWp.name}' -> first waypoint '{firstWp.name}'.\n";
                }
                else
                {
                    report += $"Route '{route.name}' is already looped ({totalWps} waypoints).\n";
                }
            }

            var controller = Object.FindObjectOfType<AITrafficController>();
            if (controller == null)
            {
                report += "\nWarning: No AITrafficController found in the scene!";
            }
            else
            {
                report += $"\nAITrafficController '{controller.name}' found and ready.";
            }

            Debug.Log($"[Traffic Verification]\n{report}");
            EditorUtility.DisplayDialog("Traffic Route Verification", report, "OK");
        }
    }
}
