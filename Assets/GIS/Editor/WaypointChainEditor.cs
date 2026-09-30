using UnityEditor;
using UnityEngine;

namespace GIS
{
    [CustomEditor(typeof(WaypointChain))]
    public class WaypointChainEditor : Editor
    {
        const float  kAppendStep        = 5f;
        const float  kWaypointY         = 0.5f;
        const string kSelectOnPlaceKey  = "GIS.WaypointChain.SelectOnPlace";

        static bool SelectNewWaypointOnPlace
        {
            get => EditorPrefs.GetBool(kSelectOnPlaceKey, false);
            set => EditorPrefs.SetBool(kSelectOnPlaceKey, value);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            var chain = (WaypointChain)target;
            int count = chain.transform.childCount;

            EditorGUILayout.HelpBox(
                $"Waypoints: {count}\n\n" +
                "MANUAL WORKFLOW:\n" +
                "  - Ctrl+Click in the Scene view to drop the next waypoint at the clicked spot.\n" +
                "    The chain stays selected, so you can click repeatedly without reselecting.\n" +
                "    The click position uses physics raycast (hits roads / ground), falling back to the y=0.5 plane.\n" +
                "  - Or click 'Append Waypoint' to add one " + kAppendStep + " m in front of the last waypoint\n" +
                "    (or at the Scene-view camera pivot if the chain is empty).\n" +
                "  - Drag waypoints around in the Scene view to refine the path.\n" +
                "  - Drop this chain GameObject into CityTrafficAI's Waypoints Parent field.",
                MessageType.Info);

            EditorGUILayout.Space();

            bool prev = SelectNewWaypointOnPlace;
            bool next = EditorGUILayout.ToggleLeft(
                new GUIContent("Select new waypoint on place",
                    "When OFF (default), Ctrl+Click and Append Waypoint leave the ManualChain selected so you can keep adding. " +
                    "When ON, selection moves to each newly created waypoint (the old behavior)."),
                prev);
            if (next != prev) SelectNewWaypointOnPlace = next;

            EditorGUILayout.Space();
            if (GUILayout.Button("Append Waypoint", GUILayout.Height(28)))
                AppendWaypoint(chain);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reverse Order"))
                    ReverseAndRenumber(chain);
                if (GUILayout.Button("Renumber (wp_0, wp_1, ...)"))
                    Renumber(chain);
            }
        }

        void OnSceneGUI()
        {
            var chain = (WaypointChain)target;
            Event e = Event.current;

            bool mod = e.control || e.command;
            if (e.type == EventType.MouseDown && e.button == 0 && mod && !e.alt && !e.shift)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                Vector3 pos;
                if (Physics.Raycast(ray, out RaycastHit rh, 5000f))
                {
                    pos = rh.point + Vector3.up * kWaypointY;
                }
                else
                {
                    Plane ground = new Plane(Vector3.up, new Vector3(0f, kWaypointY, 0f));
                    if (ground.Raycast(ray, out float dist))
                        pos = ray.GetPoint(dist);
                    else
                        return;
                }

                AddWaypointAt(chain, pos);
                e.Use();
            }
        }

        static void AppendWaypoint(WaypointChain chain)
        {
            int n = chain.transform.childCount;
            Vector3 pos;
            if (n == 0)
            {
                var sv = SceneView.lastActiveSceneView;
                pos = sv != null ? sv.pivot : chain.transform.position + Vector3.forward;
                pos.y = kWaypointY;
            }
            else if (n == 1)
            {
                pos = chain.transform.GetChild(0).position + Vector3.forward * kAppendStep;
            }
            else
            {
                Transform last       = chain.transform.GetChild(n - 1);
                Transform secondLast = chain.transform.GetChild(n - 2);
                Vector3 dir = (last.position - secondLast.position);
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-3f) dir = Vector3.forward;
                else dir.Normalize();
                pos = last.position + dir * kAppendStep;
            }
            AddWaypointAt(chain, pos);
        }

        static void AddWaypointAt(WaypointChain chain, Vector3 worldPos)
        {
            int n = chain.transform.childCount;
            var wp = new GameObject($"wp_{n}");
            Undo.RegisterCreatedObjectUndo(wp, "Add Waypoint");
            wp.transform.SetParent(chain.transform, worldPositionStays: true);
            wp.transform.position = new Vector3(worldPos.x, kWaypointY, worldPos.z);

            if (SelectNewWaypointOnPlace)
                Selection.activeGameObject = wp;

            EditorUtility.SetDirty(chain);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(chain.gameObject.scene);

            SceneView.RepaintAll();
        }

        static void ReverseAndRenumber(WaypointChain chain)
        {
            int n = chain.transform.childCount;
            var snap = new Transform[n];
            for (int i = 0; i < n; i++) snap[i] = chain.transform.GetChild(i);
            for (int i = 0; i < n; i++) snap[n - 1 - i].SetSiblingIndex(i);
            Renumber(chain);
        }

        static void Renumber(WaypointChain chain)
        {
            int n = chain.transform.childCount;
            for (int i = 0; i < n; i++)
                chain.transform.GetChild(i).name = $"wp_{i}";
            EditorUtility.SetDirty(chain);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(chain.gameObject.scene);
        }
    }

    public static class WaypointChainCreator
    {
        [MenuItem("GIS/Create Waypoint Chain")]
        public static void CreateChain()
        {
            var go = new GameObject("ManualChain");
            Undo.RegisterCreatedObjectUndo(go, "Create Waypoint Chain");
            var chain = go.AddComponent<WaypointChain>();
            chain.highwayType = "residential";
            chain.isLoop = false;

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) go.transform.position = sv.pivot;

            Selection.activeGameObject = go;
        }
    }
}
