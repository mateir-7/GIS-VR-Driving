using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GIS
{
    [CustomEditor(typeof(OsmRoadBaker))]
    public class OsmRoadBakerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            var baker = (OsmRoadBaker)target;
            if (GUILayout.Button("Bake Roads", GUILayout.Height(28)))
                OsmRoadBakeOps.BakeRoads(baker);
            if (GUILayout.Button("Clear Roads + Junctions"))
                OsmRoadBakeOps.ClearRoads(baker);

            EditorGUILayout.Space();

            if (GUILayout.Button("Bake Buildings", GUILayout.Height(28)))
                OsmRoadBakeOps.BakeBuildings(baker);
            if (GUILayout.Button("Clear Buildings"))
                OsmRoadBakeOps.ClearBuildings(baker);

            EditorGUILayout.Space();

            if (GUILayout.Button("Bake Ground", GUILayout.Height(28)))
                OsmRoadBakeOps.BakeGround(baker);
            if (GUILayout.Button("Clear Ground"))
                OsmRoadBakeOps.ClearGround(baker);

            EditorGUILayout.Space();

            if (GUILayout.Button("Bake Props", GUILayout.Height(28)))
                OsmRoadBakeOps.BakeProps(baker);
            if (GUILayout.Button("Clear Props"))
                OsmRoadBakeOps.ClearProps(baker);

            EditorGUILayout.Space();

            if (GUILayout.Button("Bake Decor", GUILayout.Height(28)))
                OsmRoadBakeOps.BakeDecor(baker);
            if (GUILayout.Button("Clear Decor"))
                OsmRoadBakeOps.ClearDecor(baker);

            EditorGUILayout.Space();

            if (GUILayout.Button("Bake AI Waypoints", GUILayout.Height(28)))
                OsmRoadBakeOps.BakeAIWaypoints(baker);
            if (GUILayout.Button("Clear AI Waypoints"))
                OsmRoadBakeOps.ClearAIWaypoints(baker);

            EditorGUILayout.Space();

            if (GUILayout.Button("Setup Daytime Lighting + Sky", GUILayout.Height(28)))
                OsmRoadBakeOps.SetupDaytimeLighting(baker);
        }
    }

    internal enum GroundKind { None, Park, Grass, Water }

    internal enum WayBakeKind { Car, Footway, Path }

    internal static class OsmRoadBakeOps
    {
        static readonly HashSet<string> WideHighways = new HashSet<string>
        {
            "motorway", "trunk", "primary"
        };

        [MenuItem("GIS/Bake Roads (selected OsmRoadBaker)")]
        public static void BakeRoadsMenu()
        {
            var baker = ResolveSelected();
            if (baker != null) BakeRoads(baker);
        }

        [MenuItem("GIS/Bake Buildings (selected OsmRoadBaker)")]
        public static void BakeBuildingsMenu()
        {
            var baker = ResolveSelected();
            if (baker != null) BakeBuildings(baker);
        }

        [MenuItem("GIS/Bake Ground (selected OsmRoadBaker)")]
        public static void BakeGroundMenu()
        {
            var baker = ResolveSelected();
            if (baker != null) BakeGround(baker);
        }

        [MenuItem("GIS/Bake Props (selected OsmRoadBaker)")]
        public static void BakePropsMenu()
        {
            var baker = ResolveSelected();
            if (baker != null) BakeProps(baker);
        }

        [MenuItem("GIS/Bake Decor (selected OsmRoadBaker)")]
        public static void BakeDecorMenu()
        {
            var baker = ResolveSelected();
            if (baker != null) BakeDecor(baker);
        }

        [MenuItem("GIS/Bake AI Waypoints (selected OsmRoadBaker)")]
        public static void BakeAIWaypointsMenu()
        {
            var baker = ResolveSelected();
            if (baker != null) BakeAIWaypoints(baker);
        }

        [MenuItem("GIS/Setup Daytime Lighting (selected OsmRoadBaker)")]
        public static void SetupDaytimeLightingMenu()
        {
            var baker = ResolveSelected();
            if (baker != null) SetupDaytimeLighting(baker);
        }

        static OsmRoadBaker ResolveSelected()
        {
            var go = Selection.activeGameObject;
            var baker = go != null ? go.GetComponent<OsmRoadBaker>() : null;
            if (baker == null)
            {
                EditorUtility.DisplayDialog("OsmRoadBaker",
                    "Select a GameObject with an OsmRoadBaker component.", "OK");
            }
            return baker;
        }

        public static void BakeRoads(OsmRoadBaker baker)
        {
            if (baker == null) return;

            EnsureAssetFolder(baker.meshOutputFolder);
            EnsureAssetFolder(baker.materialFolder);

            Material roadMat    = LoadOrCreateRoadMaterial(baker.materialFolder, baker.materialName);
            Material footwayMat = LoadOrCreateUrpLitMaterial(
                baker.materialFolder, baker.footwayMaterialName,
                new Color(0.66f, 0.66f, 0.64f, 1f), smoothness: 0.08f);
            Material pathMat    = LoadOrCreateUrpLitMaterial(
                baker.materialFolder, baker.pathMaterialName,
                new Color(0.62f, 0.52f, 0.42f, 1f), smoothness: 0.02f);

            if (baker.overrideOrigin)
                GeoProjection.SetOrigin(baker.originLat, baker.originLon);

            OsmDataset ds = OsmParser.Parse(baker.osmFile);
            if (ds.Roads.Count == 0)
            {
                Debug.LogWarning("[OsmRoadBaker] no roads parsed - nothing to bake");
                return;
            }

            float laneWidth = Mathf.Max(0.5f, baker.laneWidthMeters);
            float footwayW  = Mathf.Max(0.3f, baker.footwayWidthMeters);
            float pathW     = Mathf.Max(0.3f, baker.pathWidthMeters);

            Transform prevRoads     = baker.transform.Find(baker.roadsParentName);
            Transform prevJunctions = baker.transform.Find(baker.junctionsParentName);
            if (prevRoads != null)     Object.DestroyImmediate(prevRoads.gameObject);
            if (prevJunctions != null) Object.DestroyImmediate(prevJunctions.gameObject);

            var roadsGo = new GameObject(baker.roadsParentName);
            Undo.RegisterCreatedObjectUndo(roadsGo, "Bake Roads");
            roadsGo.transform.SetParent(baker.transform, worldPositionStays: false);
            roadsGo.isStatic = true;
            Transform roads = roadsGo.transform;

            int built = 0, skipped = 0;
            var bakedRoads = new List<(OsmWay way, int laneCount, float width)>();
            int junctionsBuilt = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var way in ds.Roads)
                {
                    string hRaw = way.Highway ?? "(none)";

                    if (way.Points.Count < 2)
                    {
                        skipped++;
                        continue;
                    }

                    WayBakeKind kind = ClassifyWay(way.Highway);
                    int laneCount;
                    float w;
                    Material useMat;
                    switch (kind)
                    {
                        case WayBakeKind.Footway:
                            laneCount = 1;
                            w = footwayW;
                            useMat = footwayMat != null ? footwayMat : roadMat;
                            break;
                        case WayBakeKind.Path:
                            laneCount = 1;
                            w = pathW;
                            useMat = pathMat != null ? pathMat : roadMat;
                            break;
                        default:
                            laneCount = DetermineLaneCount(way);
                            w = laneCount * laneWidth;
                            useMat = roadMat;
                            break;
                    }

                    Mesh mesh = BuildRoadMesh(way.Points, w, laneCount, baker.yOffset, baker.maxMiter);
                    if (mesh == null)
                    {
                        skipped++;
                        continue;
                    }

                    Vector3 centroid = RecenterMeshOnCentroid(mesh);

                    mesh.name = $"road_{way.Id}";
                    string meshPath = $"{baker.meshOutputFolder}/road_{way.Id}.asset";
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                        AssetDatabase.DeleteAsset(meshPath);
                    AssetDatabase.CreateAsset(mesh, meshPath);

                    var go = new GameObject($"road_{way.Id}_{hRaw}");
                    go.transform.SetParent(roads, worldPositionStays: false);
                    go.transform.position = centroid;
                    go.layer = 0;
                    go.isStatic = true;

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = useMat;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = false;

                    built++;
                    bakedRoads.Add((way, laneCount, w));
                }

                if (baker.bakeJunctions && bakedRoads.Count > 0)
                {
                    junctionsBuilt = BakeJunctionPatches(baker, bakedRoads, roadMat);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[OsmRoadBaker] baked {built} roads, {junctionsBuilt} junctions, skipped {skipped}");

            Selection.activeGameObject = roadsGo;
            EditorSceneMarkDirty(baker);
        }

        public static void ClearRoads(OsmRoadBaker baker)
        {
            if (baker == null) return;
            bool cleared = false;
            foreach (var name in new[] { baker.roadsParentName, baker.junctionsParentName })
            {
                if (string.IsNullOrEmpty(name)) continue;
                Transform t = baker.transform.Find(name);
                if (t != null)
                {
                    Undo.DestroyObjectImmediate(t.gameObject);
                    cleared = true;
                }
            }
            if (cleared)
            {
                Debug.Log("[OsmRoadBaker] cleared Roads + Junctions");
                EditorSceneMarkDirty(baker);
            }
            else
            {
                Debug.Log("[OsmRoadBaker] nothing to clear");
            }
        }

        public static void BakeBuildings(OsmRoadBaker baker)
        {
            if (baker == null) return;

            EnsureAssetFolder(baker.meshOutputFolder);
            EnsureAssetFolder(baker.materialFolder);

            Material buildingMat = LoadOrCreateBuildingMaterial(
                baker.materialFolder, baker.buildingMaterialName);
            if (buildingMat == null) return;

            if (baker.overrideOrigin)
                GeoProjection.SetOrigin(baker.originLat, baker.originLon);

            OsmDataset ds = OsmParser.Parse(baker.osmFile);
            if (ds.Buildings.Count == 0)
            {
                Debug.LogWarning("[OsmRoadBaker] no buildings parsed - nothing to bake");
                return;
            }

            Transform prev = baker.transform.Find(baker.buildingsParentName);
            if (prev != null) Object.DestroyImmediate(prev.gameObject);

            int built = 0, skipped = 0;
            GameObject buildingsGo = null;
            try
            {
                AssetDatabase.StartAssetEditing();
                (buildingsGo, built, skipped) = BakeBuildingsInternal(baker, ds.Buildings, buildingMat);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[OsmRoadBaker] baked {built} buildings / skipped {skipped}");

            if (buildingsGo != null) Selection.activeGameObject = buildingsGo;
            EditorSceneMarkDirty(baker);
        }

        public static void ClearBuildings(OsmRoadBaker baker)
        {
            if (baker == null) return;
            Transform b = baker.transform.Find(baker.buildingsParentName);
            if (b != null)
            {
                Undo.DestroyObjectImmediate(b.gameObject);
                Debug.Log("[OsmRoadBaker] cleared Buildings");
                EditorSceneMarkDirty(baker);
            }
            else
            {
                Debug.Log("[OsmRoadBaker] no Buildings to clear");
            }
        }

        public static void BakeGround(OsmRoadBaker baker)
        {
            if (baker == null) return;

            EnsureAssetFolder(baker.meshOutputFolder);
            EnsureAssetFolder(baker.materialFolder);

            Material parkMat  = LoadOrCreateUrpLitMaterial(baker.materialFolder, "GreenPark",   new Color(0.28f, 0.50f, 0.20f, 1f), smoothness: 0.05f);
            Material grassMat = LoadOrCreateUrpLitMaterial(baker.materialFolder, "GreenGrass",  new Color(0.50f, 0.60f, 0.28f, 1f), smoothness: 0.05f);
            Material waterMat = LoadOrCreateUrpLitMaterial(baker.materialFolder, "WaterBlue",   new Color(0.20f, 0.38f, 0.58f, 1f), smoothness: 0.45f);
            Material baseMat  = LoadOrCreateUrpLitMaterial(baker.materialFolder, "BaseGround",  new Color(0.45f, 0.42f, 0.36f, 1f), smoothness: 0.05f);
            if (parkMat == null || grassMat == null || waterMat == null || baseMat == null) return;

            if (baker.overrideOrigin)
                GeoProjection.SetOrigin(baker.originLat, baker.originLon);

            OsmDataset ds = OsmParser.Parse(baker.osmFile);

            Transform prevGC = baker.transform.Find(baker.groundCoverParentName);
            if (prevGC != null) Object.DestroyImmediate(prevGC.gameObject);
            GameObject prevBase = GameObject.Find(baker.baseGroundName);
            if (prevBase != null) Object.DestroyImmediate(prevBase);

            var gcGo = new GameObject(baker.groundCoverParentName);
            Undo.RegisterCreatedObjectUndo(gcGo, "Bake Ground");
            gcGo.transform.SetParent(baker.transform, worldPositionStays: false);
            gcGo.isStatic = true;
            Transform gcParent = gcGo.transform;

            int parkCount = 0, grassCount = 0, waterCount = 0, gcSkipped = 0;
            bool baseBuilt = false;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var w in ds.GroundCovers)
                {
                    GroundKind kind = ClassifyGroundCover(w);
                    if (kind == GroundKind.None) { gcSkipped++; continue; }

                    Material useMat;
                    string prefix;
                    switch (kind)
                    {
                        case GroundKind.Park:  useMat = parkMat;  prefix = "park";  break;
                        case GroundKind.Grass: useMat = grassMat; prefix = "grass"; break;
                        case GroundKind.Water: useMat = waterMat; prefix = "water"; break;
                        default: gcSkipped++; continue;
                    }

                    var footprint = ExtractFootprint(w.Points);
                    if (footprint.Count < 3) { gcSkipped++; continue; }

                    Mesh mesh = BuildGroundCoverMesh(footprint, baker.groundCoverY);
                    if (mesh == null) { gcSkipped++; continue; }

                    Vector3 centroid = RecenterMeshOnCentroid(mesh);

                    mesh.name = $"{prefix}_{w.Id}";
                    string meshPath = $"{baker.meshOutputFolder}/{prefix}_{w.Id}.asset";
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                        AssetDatabase.DeleteAsset(meshPath);
                    AssetDatabase.CreateAsset(mesh, meshPath);

                    var go = new GameObject($"{prefix}_{w.Id}");
                    go.transform.SetParent(gcParent, worldPositionStays: false);
                    go.transform.position = centroid;
                    go.layer = 0;
                    go.isStatic = true;

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = useMat;

                    if (kind == GroundKind.Park) parkCount++;
                    else if (kind == GroundKind.Grass) grassCount++;
                    else waterCount++;
                }

                var bounds = ComputeOsmBounds(ds);
                if (bounds.max.x > bounds.min.x && bounds.max.y > bounds.min.y)
                {
                    float m = Mathf.Max(0f, baker.baseGroundMargin);
                    Vector2 bMin = bounds.min - new Vector2(m, m);
                    Vector2 bMax = bounds.max + new Vector2(m, m);

                    Mesh baseMesh = BuildBaseGroundMesh(bMin, bMax, baker.baseGroundY);
                    Vector3 baseCentroid = RecenterMeshOnCentroid(baseMesh);

                    baseMesh.name = "BaseGround";
                    string basePath = $"{baker.meshOutputFolder}/BaseGround.asset";
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(basePath) != null)
                        AssetDatabase.DeleteAsset(basePath);
                    AssetDatabase.CreateAsset(baseMesh, basePath);

                    var baseGo = new GameObject(baker.baseGroundName);
                    Undo.RegisterCreatedObjectUndo(baseGo, "Bake Base Ground");
                    baseGo.transform.SetParent(null, worldPositionStays: false);
                    baseGo.transform.position = baseCentroid;
                    baseGo.layer = 0;
                    baseGo.isStatic = true;

                    var bmf = baseGo.AddComponent<MeshFilter>();
                    bmf.sharedMesh = baseMesh;
                    var bmr = baseGo.AddComponent<MeshRenderer>();
                    bmr.sharedMaterial = baseMat;

                    var bbc = baseGo.AddComponent<BoxCollider>();
                    Bounds mb = baseMesh.bounds;
                    bbc.center = mb.center + Vector3.down * 0.5f;
                    bbc.size   = new Vector3(mb.size.x, 1f, mb.size.z);

                    baseBuilt = true;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[OsmRoadBaker] ground: {parkCount} parks, {grassCount} grass, {waterCount} water, skipped {gcSkipped}, baseBuilt={baseBuilt}");

            Selection.activeGameObject = gcGo;
            EditorSceneMarkDirty(baker);
        }

        public static void ClearGround(OsmRoadBaker baker)
        {
            if (baker == null) return;
            bool cleared = false;
            Transform gc = baker.transform.Find(baker.groundCoverParentName);
            if (gc != null)
            {
                Undo.DestroyObjectImmediate(gc.gameObject);
                cleared = true;
            }
            GameObject baseGo = GameObject.Find(baker.baseGroundName);
            if (baseGo != null)
            {
                Undo.DestroyObjectImmediate(baseGo);
                cleared = true;
            }
            if (cleared)
            {
                Debug.Log("[OsmRoadBaker] cleared GroundCover + BaseGround");
                EditorSceneMarkDirty(baker);
            }
            else
            {
                Debug.Log("[OsmRoadBaker] no Ground to clear");
            }
        }

        const float kTreeTrunkRadius  = 0.18f;
        const float kTreeTrunkHeight  = 3.5f;
        const float kTreeCanopyRadius = 1.8f;
        const float kTreeCanopyCenterY = kTreeTrunkHeight + kTreeCanopyRadius * 0.3f;
        const int   kTreeTrunkSides   = 8;

        const float kLampPoleRadius   = 0.06f;
        const float kLampPoleHeight   = 5.0f;
        const float kLampHeadRadius   = 0.25f;
        const float kLampHeadCenterY  = kLampPoleHeight;
        const int   kLampPoleSides    = 6;

        public static void BakeProps(OsmRoadBaker baker)
        {
            if (baker == null) return;

            EnsureAssetFolder(baker.meshOutputFolder);
            EnsureAssetFolder(baker.materialFolder);

            Material trunkMat  = LoadOrCreateUrpLitMaterial(baker.materialFolder, "TreeTrunk",  new Color(0.35f, 0.22f, 0.13f, 1f), smoothness: 0.05f);
            Material canopyMat = LoadOrCreateUrpLitMaterial(baker.materialFolder, "TreeCanopy", new Color(0.30f, 0.50f, 0.18f, 1f), smoothness: 0.05f);
            Material poleMat   = LoadOrCreateUrpLitMaterial(baker.materialFolder, "LampPole",   new Color(0.20f, 0.20f, 0.22f, 1f), smoothness: 0.30f, metallic: 0.50f);
            Material headMat   = LoadOrCreateLampHeadMaterial(baker.materialFolder, "LampHead");
            if (trunkMat == null || canopyMat == null || poleMat == null || headMat == null) return;

            if (baker.overrideOrigin)
                GeoProjection.SetOrigin(baker.originLat, baker.originLon);

            OsmDataset ds = OsmParser.Parse(baker.osmFile);

            Transform prevProps = baker.transform.Find(baker.propsParentName);
            if (prevProps != null) Object.DestroyImmediate(prevProps.gameObject);

            var propsGo = new GameObject(baker.propsParentName);
            Undo.RegisterCreatedObjectUndo(propsGo, "Bake Props");
            propsGo.transform.SetParent(baker.transform, worldPositionStays: false);
            propsGo.isStatic = true;
            Transform propsParent = propsGo.transform;

            var treePoints = new List<Vector3>();
            var lampPoints = new List<Vector3>();
            foreach (var n in ds.TaggedNodes)
            {
                if (n.Natural == "tree") treePoints.Add(n.Local);
                else if (n.Highway == "street_lamp") lampPoints.Add(n.Local);
            }

            foreach (var w in ds.GroundCovers)
            {
                if (w.Natural == "tree_row")
                {
                    for (int i = 0; i < w.Points.Count; i++) treePoints.Add(w.Points[i]);
                }
            }

            DeclusterPoints(treePoints, minDist: 4f, passes: 3);

            float laneWidthForLamps = Mathf.Max(0.5f, baker.laneWidthMeters);
            AddAutoLampsAlongRoads(ds, lampPoints, laneWidth: laneWidthForLamps, spacingMeters: 50f);

            int treesBuilt = 0, lampsBuilt = 0, skipped = 0;
            try
            {
                AssetDatabase.StartAssetEditing();

                Mesh treeMesh = BuildTreeMesh();
                treeMesh.name = "PropTree";
                string treeMeshPath = $"{baker.meshOutputFolder}/PropTree.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(treeMeshPath) != null) AssetDatabase.DeleteAsset(treeMeshPath);
                AssetDatabase.CreateAsset(treeMesh, treeMeshPath);

                Mesh lampMesh = BuildLampMesh();
                lampMesh.name = "PropLamp";
                string lampMeshPath = $"{baker.meshOutputFolder}/PropLamp.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(lampMeshPath) != null) AssetDatabase.DeleteAsset(lampMeshPath);
                AssetDatabase.CreateAsset(lampMesh, lampMeshPath);

                Material[] treeMats = { trunkMat, canopyMat };
                int treeIdx = 0;
                foreach (var p in treePoints)
                {
                    long seed = unchecked((long)(p.x * 73856093f) ^ (long)(p.z * 19349663f) ^ (treeIdx++ * 83492791L));
                    var rng = new System.Random(unchecked((int)seed));
                    float yRot = (float)rng.NextDouble() * 360f;

                    float scale = 0.70f + (float)rng.NextDouble() * 0.70f;

                    var go = new GameObject($"tree_{treeIdx}");
                    go.transform.SetParent(propsParent, worldPositionStays: false);
                    go.transform.position = p;
                    go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
                    go.transform.localScale = Vector3.one * scale;
                    go.layer = 0;
                    go.isStatic = true;

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = treeMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = treeMats;

                    var col = go.AddComponent<CapsuleCollider>();
                    col.direction = 1;
                    col.radius = 0.25f;
                    col.height = kTreeTrunkHeight;
                    col.center = new Vector3(0f, kTreeTrunkHeight * 0.5f, 0f);

                    treesBuilt++;
                }

                Material[] lampMats = { poleMat, headMat };
                int lampIdx = 0;
                foreach (var p in lampPoints)
                {
                    var rng = new System.Random(unchecked((int)(p.x * 73856093f) ^ (int)(p.z * 19349663f) ^ (lampIdx++ * 83492791)));
                    float yRot = (float)rng.NextDouble() * 360f;

                    var go = new GameObject($"lamp_{lampIdx}");
                    go.transform.SetParent(propsParent, worldPositionStays: false);
                    go.transform.position = p;
                    go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
                    go.layer = 0;
                    go.isStatic = true;

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = lampMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = lampMats;

                    var col = go.AddComponent<CapsuleCollider>();
                    col.direction = 1;
                    col.radius = 0.10f;
                    col.height = kLampPoleHeight;
                    col.center = new Vector3(0f, kLampPoleHeight * 0.5f, 0f);

                    lampsBuilt++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[OsmRoadBaker] props: {treesBuilt} trees, {lampsBuilt} lamps (skipped {skipped})");
            Selection.activeGameObject = propsGo;
            EditorSceneMarkDirty(baker);
        }

        public static void ClearProps(OsmRoadBaker baker)
        {
            if (baker == null) return;
            Transform t = baker.transform.Find(baker.propsParentName);
            if (t != null)
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                Debug.Log("[OsmRoadBaker] cleared Props");
                EditorSceneMarkDirty(baker);
            }
            else
            {
                Debug.Log("[OsmRoadBaker] no Props to clear");
            }
        }

        static readonly HashSet<string> AIDrivableHighways = new HashSet<string>
        {
            "motorway", "trunk", "primary", "secondary", "tertiary",
            "residential", "service", "unclassified", "living_street"
        };

        public static void BakeAIWaypoints(OsmRoadBaker baker)
        {
            if (baker == null) return;

            if (baker.overrideOrigin)
                GeoProjection.SetOrigin(baker.originLat, baker.originLon);

            OsmDataset ds = OsmParser.Parse(baker.osmFile);
            if (ds.Roads.Count == 0)
            {
                Debug.LogWarning("[OsmRoadBaker] no roads parsed - nothing to chain");
                return;
            }

            Transform prev = baker.transform.Find(baker.aiWaypointsParentName);
            if (prev != null) Object.DestroyImmediate(prev.gameObject);

            var rootGo = new GameObject(baker.aiWaypointsParentName);
            Undo.RegisterCreatedObjectUndo(rootGo, "Bake AI Waypoints");
            rootGo.transform.SetParent(baker.transform, worldPositionStays: false);
            Transform root = rootGo.transform;

            int chainsBuilt = 0, chainsSkipped = 0, totalWaypoints = 0;

            foreach (var way in ds.Roads)
            {
                string h = way.Highway ?? "";
                string hBase = h;
                if (hBase.EndsWith("_link")) hBase = hBase.Substring(0, hBase.Length - 5);
                if (!AIDrivableHighways.Contains(hBase)) { chainsSkipped++; continue; }
                if (way.Points.Count < 2) { chainsSkipped++; continue; }

                var chainGo = new GameObject($"Chain_{way.Id}_{h}");
                chainGo.transform.SetParent(root, worldPositionStays: false);
                var chain = chainGo.AddComponent<WaypointChain>();
                chain.highwayType = h;
                chain.sourceWayId = way.Id;
                chain.isLoop = way.IsClosed && way.Points.Count >= 4;

                for (int i = 0; i < way.Points.Count; i++)
                {
                    var wp = new GameObject($"wp_{i}");
                    wp.transform.SetParent(chainGo.transform, worldPositionStays: false);
                    Vector3 p = way.Points[i];
                    wp.transform.position = new Vector3(p.x, 0.5f, p.z);
                    totalWaypoints++;
                }

                chainsBuilt++;
            }

            Debug.Log($"[OsmRoadBaker] AI waypoints: {chainsBuilt} chains, {totalWaypoints} waypoints, skipped {chainsSkipped}");

            Selection.activeGameObject = rootGo;
            EditorSceneMarkDirty(baker);
        }

        public static void ClearAIWaypoints(OsmRoadBaker baker)
        {
            if (baker == null) return;
            Transform t = baker.transform.Find(baker.aiWaypointsParentName);
            if (t != null)
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                Debug.Log("[OsmRoadBaker] cleared AI Waypoints");
                EditorSceneMarkDirty(baker);
            }
            else
            {
                Debug.Log("[OsmRoadBaker] no AI Waypoints to clear");
            }
        }

        public static void BakeDecor(OsmRoadBaker baker)
        {
            if (baker == null) return;

            EnsureAssetFolder(baker.meshOutputFolder);
            EnsureAssetFolder(baker.materialFolder);

            Material benchMat    = LoadOrCreateUrpLitMaterial(baker.materialFolder, "BenchWood",   new Color(0.45f, 0.30f, 0.18f, 1f), smoothness: 0.08f);
            Material postMat     = LoadOrCreateUrpLitMaterial(baker.materialFolder, "BusStopPost", new Color(0.25f, 0.25f, 0.28f, 1f), smoothness: 0.35f, metallic: 0.5f);
            Material roofMat     = LoadOrCreateUrpLitMaterial(baker.materialFolder, "BusStopRoof", new Color(0.55f, 0.55f, 0.58f, 1f), smoothness: 0.20f, metallic: 0.3f);
            Material fountainMat = LoadOrCreateUrpLitMaterial(baker.materialFolder, "FountainStone", new Color(0.55f, 0.52f, 0.48f, 1f), smoothness: 0.05f);

            Material waterMat    = LoadOrCreateUrpLitMaterial(baker.materialFolder, "WaterBlue",   new Color(0.20f, 0.38f, 0.58f, 1f), smoothness: 0.45f);
            Material monumentMat = LoadOrCreateUrpLitMaterial(baker.materialFolder, "MonumentStone", new Color(0.62f, 0.58f, 0.50f, 1f), smoothness: 0.10f);
            Material parkingMat  = LoadOrCreateUrpLitMaterial(baker.materialFolder, "ParkingDark",   new Color(0.20f, 0.20f, 0.22f, 1f), smoothness: 0.05f);
            Material sprayMat    = LoadOrCreateFountainSprayMaterial(baker.materialFolder, "FountainSpray");
            if (benchMat == null || postMat == null || roofMat == null || fountainMat == null || waterMat == null || monumentMat == null || parkingMat == null) return;

            if (baker.overrideOrigin)
                GeoProjection.SetOrigin(baker.originLat, baker.originLon);

            OsmDataset ds = OsmParser.Parse(baker.osmFile);

            Transform prev = baker.transform.Find(baker.decorParentName);
            if (prev != null) Object.DestroyImmediate(prev.gameObject);

            var decorGo = new GameObject(baker.decorParentName);
            Undo.RegisterCreatedObjectUndo(decorGo, "Bake Decor");
            decorGo.transform.SetParent(baker.transform, worldPositionStays: false);
            decorGo.isStatic = true;
            Transform parent = decorGo.transform;

            int benches = 0, busStops = 0, fountains = 0, monuments = 0, parkings = 0, skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                Mesh benchMesh = BuildBenchMesh();    SaveDecorMeshAsset(benchMesh,    "DecorBench",    baker.meshOutputFolder);
                Mesh busMesh   = BuildBusStopMesh();  SaveDecorMeshAsset(busMesh,      "DecorBusStop",  baker.meshOutputFolder);
                Mesh fountMesh = BuildFountainMesh(); SaveDecorMeshAsset(fountMesh,    "DecorFountain", baker.meshOutputFolder);
                Mesh monuMesh  = BuildMonumentMesh(); SaveDecorMeshAsset(monuMesh,     "DecorMonument", baker.meshOutputFolder);

                var fountainNodes = new List<OsmNode>();
                int benchIdx = 0, busIdx = 0, fountIdx = 0, monuIdx = 0;
                foreach (var node in ds.TaggedNodes)
                {
                    Vector3 p = new Vector3(node.Local.x, 0f, node.Local.z);

                    if (node.Amenity == "bench")
                    {
                        var go = NewPropGo(parent, $"bench_{++benchIdx}", p, RandYaw(node.Id));
                        SetRendererSingle(go, benchMesh, benchMat);

                        var col = go.AddComponent<BoxCollider>();
                        col.center = new Vector3(0f, 0.50f, 0f);
                        col.size   = new Vector3(1.6f, 1.0f, 0.4f);
                        benches++;
                    }
                    else if (node.Highway == "bus_stop" || node.PublicTransport == "platform")
                    {
                        var go = NewPropGo(parent, $"busstop_{++busIdx}", p, RandYaw(node.Id));
                        SetRendererMulti(go, busMesh, new[] { postMat, roofMat });
                        var col = go.AddComponent<CapsuleCollider>();
                        col.direction = 1;
                        col.radius = 0.15f;
                        col.height = 2.8f;
                        col.center = new Vector3(0f, 1.4f, 0f);
                        busStops++;
                    }
                    else if (node.Amenity == "fountain")
                    {
                        fountainNodes.Add(node);
                    }
                    else if (node.Historic == "monument")
                    {
                        var go = NewPropGo(parent, $"monument_{++monuIdx}", p, RandYaw(node.Id));
                        SetRendererSingle(go, monuMesh, monumentMat);
                        var col = go.AddComponent<BoxCollider>();
                        col.center = new Vector3(0f, 0.75f, 0f);
                        col.size   = new Vector3(0.6f, 1.5f, 0.6f);
                        monuments++;
                    }
                }

                {
                    float spacing = Mathf.Max(0f, baker.fountainMinSpacingMeters);
                    var kept = DeclusterFountainNodes(fountainNodes, spacing);

                    foreach (var node in kept)
                    {
                        Vector3 p = new Vector3(node.Local.x, 0f, node.Local.z);
                        var go = NewPropGo(parent, $"fountain_{++fountIdx}", p, 0f);
                        SetRendererMulti(go, fountMesh, new[] { fountainMat, waterMat });

                        var col = go.AddComponent<BoxCollider>();
                        col.center = new Vector3(0f, 0.35f, 0f);
                        col.size   = new Vector3(3.0f, 0.7f, 3.0f);

                        AddFountainSpray(go, sprayMat, "TopSpray",
                            localPos:        new Vector3(0f, 1.8f, 0f),
                            coneAngle:       8f,
                            shapeRadius:     0.05f,
                            radiusThickness: 1f,
                            minSpeed:        2.6f, maxSpeed: 4.2f,
                            minLifetime:     1.1f, maxLifetime: 1.5f,
                            minSize:         0.05f, maxSize:   0.09f,
                            emitRate:        70f);

                        AddFountainSpray(go, sprayMat, "TierSpray",
                            localPos:        new Vector3(0f, 1.2f, 0f),
                            coneAngle:       28f,
                            shapeRadius:     0.75f,
                            radiusThickness: 0f,
                            minSpeed:        1.5f, maxSpeed: 2.4f,
                            minLifetime:     0.9f, maxLifetime: 1.3f,
                            minSize:         0.04f, maxSize:   0.07f,
                            emitRate:        45f);

                        fountains++;
                    }
                }

                foreach (var w in ds.GroundCovers)
                {
                    if (w.Amenity != "parking") continue;
                    var fp = ExtractFootprint(w.Points);
                    if (fp.Count < 3) { skipped++; continue; }

                    Mesh m = BuildGroundCoverMesh(fp, baker.groundCoverY + 0.005f);
                    if (m == null) { skipped++; continue; }

                    Vector3 centroid = RecenterMeshOnCentroid(m);

                    m.name = $"parking_{w.Id}";
                    string mp = $"{baker.meshOutputFolder}/parking_{w.Id}.asset";
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(mp) != null) AssetDatabase.DeleteAsset(mp);
                    AssetDatabase.CreateAsset(m, mp);

                    var go = new GameObject($"parking_{w.Id}");
                    go.transform.SetParent(parent, worldPositionStays: false);
                    go.transform.position = centroid;
                    go.layer = 0;
                    go.isStatic = true;

                    var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = parkingMat;

                    parkings++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[OsmRoadBaker] decor: {benches} benches, {busStops} bus stops, {fountains} fountains, {monuments} monuments, {parkings} parking polys");
            Selection.activeGameObject = decorGo;
            EditorSceneMarkDirty(baker);
        }

        public static void ClearDecor(OsmRoadBaker baker)
        {
            if (baker == null) return;
            Transform t = baker.transform.Find(baker.decorParentName);
            if (t != null)
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                Debug.Log("[OsmRoadBaker] cleared Decor");
                EditorSceneMarkDirty(baker);
            }
            else
            {
                Debug.Log("[OsmRoadBaker] no Decor to clear");
            }
        }

        static GameObject NewPropGo(Transform parent, string name, Vector3 pos, float yRotDeg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yRotDeg, 0f);
            go.layer = 0;
            go.isStatic = true;
            return go;
        }

        static void SetRendererSingle(GameObject go, Mesh mesh, Material mat)
        {
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
        }

        static void SetRendererMulti(GameObject go, Mesh mesh, Material[] mats)
        {
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
        }

        static float RandYaw(long id)
        {
            return (float)new System.Random(unchecked((int)id ^ 0x646563)).NextDouble() * 360f;
        }

        static void SaveDecorMeshAsset(Mesh m, string name, string folder)
        {
            m.name = name;
            string p = $"{folder}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(p) != null) AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(m, p);
        }

        static Material LoadOrCreateFountainSprayMaterial(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null)
            {
                Debug.LogWarning("[OsmRoadBaker] no particle shader available; fountain spray will use default material");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != sh)
            {
                mat.shader = sh;
            }

            Color spray = new Color(0.92f, 0.96f, 1.00f, 0.85f);
            SetColorIfPresent(mat, "_BaseColor", spray);
            SetColorIfPresent(mat, "_Color",     spray);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend",   0f);
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite",  0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void AddFountainSpray(
            GameObject fountainGo, Material sprayMat, string childName,
            Vector3 localPos, float coneAngle, float shapeRadius, float radiusThickness,
            float minSpeed, float maxSpeed,
            float minLifetime, float maxLifetime,
            float minSize, float maxSize,
            float emitRate)
        {
            var sprayGo = new GameObject(childName);
            sprayGo.transform.SetParent(fountainGo.transform, worldPositionStays: false);
            sprayGo.transform.localPosition = localPos;
            sprayGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var ps = sprayGo.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startSpeed    = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
            main.startSize     = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor    = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.93f, 1.00f, 0.85f),
                new Color(1.00f, 1.00f, 1.00f, 0.85f));
            main.gravityModifier   = 0.6f;
            main.simulationSpace   = ParticleSystemSimulationSpace.World;
            main.maxParticles      = 500;
            main.playOnAwake       = true;

            var emission = ps.emission;
            emission.rateOverTime = emitRate;

            var shape = ps.shape;
            shape.shapeType       = ParticleSystemShapeType.Cone;
            shape.angle           = coneAngle;
            shape.radius          = shapeRadius;
            shape.radiusThickness = radiusThickness;

            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            if (sprayMat != null) pr.sharedMaterial = sprayMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows    = false;
        }

        static Mesh BuildBenchMesh()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();

            float legSize  = 0.05f;
            float legHalfH = 0.225f;
            float legY     = 0.225f;
            float legX     = 0.75f;
            float legZ     = 0.15f;
            Vector3 legDim = new Vector3(legSize, 0.45f, legSize);
            AppendUnityCube(v, n, u, t, new Vector3(-legX, legY,  legZ), legDim);
            AppendUnityCube(v, n, u, t, new Vector3( legX, legY,  legZ), legDim);
            AppendUnityCube(v, n, u, t, new Vector3(-legX, legY, -legZ), legDim);
            AppendUnityCube(v, n, u, t, new Vector3( legX, legY, -legZ), legDim);

            AppendUnityCube(v, n, u, t, new Vector3(0f, 0.475f, 0f), new Vector3(1.6f, 0.05f, 0.4f));

            AppendUnityCube(v, n, u, t, new Vector3(0f, 0.75f, -0.175f), new Vector3(1.6f, 0.5f, 0.05f));

            return Build1Sub(v, n, u, t);
        }

        static Mesh BuildBusStopMesh()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>();
            var post = new List<int>(); var roof = new List<int>();

            AppendUnityCylinder(v, n, u, post, center: new Vector3(0f, 1.40f, 0f), diameter: 0.12f, height: 2.80f);

            AppendUnityCube(v, n, u, roof,     center: new Vector3(0f, 2.825f, 0f), size: new Vector3(1.5f, 0.05f, 1.2f));
            return Build2Sub(v, n, u, post, roof);
        }

        static Mesh BuildFountainMesh()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>();
            var stone = new List<int>(); var water = new List<int>();

            int sides      = 24;
            int smallSides = 16;

            float bO = 1.50f, bI = 1.40f, bH = 0.70f;
            AppendCylinder(v, n, u, stone,           bO, bH, 0f, sides);
            AppendCylinderInnerWall(v, n, u, stone,  bI, bH, 0f, sides);
            AppendAnnularRing(v, n, u, stone,        bO, bI, bH, sides);

            AppendUnityCylinder(v, n, u, stone, center: new Vector3(0f, 0.6f, 0f), diameter: 0.9f, height: 1.2f);

            float sbO = 0.45f, sbI = 0.35f, sbBase = 1.20f, sbH = 0.20f;
            AppendCylinder(v, n, u, stone,           sbO, sbH, sbBase, smallSides);
            AppendCylinderInnerWall(v, n, u, stone,  sbI, sbH, sbBase, smallSides);
            AppendAnnularRing(v, n, u, stone,        sbO, sbI, sbBase + sbH, smallSides);

            AppendUnityCylinder(v, n, u, stone, center: new Vector3(0f, 1.7f, 0f), diameter: 0.24f, height: 0.6f);

            AppendAnnularRing(v, n, u, water, outerRadius: 1.35f, innerRadius: 0.50f, y: 0.67f, sides: sides);

            AppendDisc(v, n, u, water, radius: 0.30f, y: 1.37f, sides: smallSides);

            return Build2Sub(v, n, u, stone, water);
        }

        static Mesh BuildMonumentMesh()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
            AppendUnityCube(v, n, u, t, center: new Vector3(0f, 0.75f, 0f), size: new Vector3(0.6f, 1.5f, 0.6f));
            return Build1Sub(v, n, u, t);
        }

        static Mesh Build1Sub(List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t)
        {
            var mesh = new Mesh();
            if (v.Count > 65530) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, u);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Build2Sub(List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t0, List<int> t1)
        {
            var mesh = new Mesh();
            if (v.Count > 65530) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, u);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(t0, 0); mesh.SetTriangles(t1, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh s_unityCube;
        static Mesh GetUnityCubeMesh()
        {
            if (s_unityCube != null) return s_unityCube;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s_unityCube = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return s_unityCube;
        }

        static void AppendUnityCube(
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
            List<int> tris, Vector3 center, Vector3 size)
        {
            var cube = GetUnityCubeMesh();
            int baseIdx = verts.Count;
            var sv = cube.vertices;
            var sn = cube.normals;
            var su = cube.uv;
            var st = cube.triangles;

            for (int i = 0; i < sv.Length; i++)
            {
                verts.Add(new Vector3(sv[i].x * size.x, sv[i].y * size.y, sv[i].z * size.z) + center);
                normals.Add(sn[i]);
                uvs.Add(i < su.Length ? su[i] : Vector2.zero);
            }
            for (int i = 0; i < st.Length; i++) tris.Add(baseIdx + st[i]);
        }

        static Mesh s_unityCylinder;
        static Mesh GetUnityCylinderMesh()
        {
            if (s_unityCylinder != null) return s_unityCylinder;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            s_unityCylinder = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return s_unityCylinder;
        }

        static void AppendUnityCylinder(
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
            List<int> tris, Vector3 center, float diameter, float height)
        {
            var cyl = GetUnityCylinderMesh();
            int baseIdx = verts.Count;
            var sv = cyl.vertices;
            var sn = cyl.normals;
            var su = cyl.uv;
            var st = cyl.triangles;

            float xz = diameter;
            float yScale = height * 0.5f;
            for (int i = 0; i < sv.Length; i++)
            {
                verts.Add(new Vector3(sv[i].x * xz, sv[i].y * yScale, sv[i].z * xz) + center);
                normals.Add(sn[i]);
                uvs.Add(i < su.Length ? su[i] : Vector2.zero);
            }
            for (int i = 0; i < st.Length; i++) tris.Add(baseIdx + st[i]);
        }

        static Mesh BuildTreeMesh()
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var trunkTris = new List<int>();
            var canopyTris = new List<int>();

            AppendCylinder(verts, normals, uvs, trunkTris,
                           kTreeTrunkRadius, kTreeTrunkHeight, 0f, kTreeTrunkSides);
            AppendUnitySphere(verts, normals, uvs, canopyTris,
                              new Vector3(0f, kTreeCanopyCenterY, 0f), kTreeCanopyRadius);

            var mesh = new Mesh();
            if (verts.Count > 65530) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(trunkTris, 0);
            mesh.SetTriangles(canopyTris, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildLampMesh()
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var poleTris = new List<int>();
            var headTris = new List<int>();

            AppendCylinder(verts, normals, uvs, poleTris,
                           kLampPoleRadius, kLampPoleHeight, 0f, kLampPoleSides);
            AppendUnitySphere(verts, normals, uvs, headTris,
                              new Vector3(0f, kLampHeadCenterY, 0f), kLampHeadRadius);

            var mesh = new Mesh();
            if (verts.Count > 65530) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(poleTris, 0);
            mesh.SetTriangles(headTris, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AppendCylinder(
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
            List<int> tris, float radius, float height, float baseY, int sides)
        {
            int baseIdx = verts.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = (float)i / sides * Mathf.PI * 2f;
                float cx = Mathf.Cos(a);
                float sz = Mathf.Sin(a);
                Vector3 outward = new Vector3(cx, 0f, sz);
                float u = (float)i / sides;

                verts.Add(new Vector3(cx * radius, baseY, sz * radius));
                normals.Add(outward);
                uvs.Add(new Vector2(u, 0f));

                verts.Add(new Vector3(cx * radius, baseY + height, sz * radius));
                normals.Add(outward);
                uvs.Add(new Vector2(u, 1f));
            }

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                int b0 = baseIdx + 2 * i;
                int t0 = baseIdx + 2 * i + 1;
                int b1 = baseIdx + 2 * j;
                int t1 = baseIdx + 2 * j + 1;

                tris.Add(b0); tris.Add(t1); tris.Add(b1);
                tris.Add(b0); tris.Add(t0); tris.Add(t1);
            }
        }

        static void AppendCylinderInnerWall(
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
            List<int> tris, float radius, float height, float baseY, int sides)
        {
            int baseIdx = verts.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = (float)i / sides * Mathf.PI * 2f;
                float cx = Mathf.Cos(a);
                float sz = Mathf.Sin(a);
                Vector3 inward = new Vector3(-cx, 0f, -sz);
                float u = (float)i / sides;

                verts.Add(new Vector3(cx * radius, baseY, sz * radius));
                normals.Add(inward);
                uvs.Add(new Vector2(u, 0f));

                verts.Add(new Vector3(cx * radius, baseY + height, sz * radius));
                normals.Add(inward);
                uvs.Add(new Vector2(u, 1f));
            }

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                int b0 = baseIdx + 2 * i;
                int t0 = baseIdx + 2 * i + 1;
                int b1 = baseIdx + 2 * j;
                int t1 = baseIdx + 2 * j + 1;
                tris.Add(b0); tris.Add(b1); tris.Add(t1);
                tris.Add(b0); tris.Add(t1); tris.Add(t0);
            }
        }

        static void AppendAnnularRing(
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
            List<int> tris, float outerRadius, float innerRadius, float y, int sides)
        {
            int baseIdx = verts.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = (float)i / sides * Mathf.PI * 2f;
                float cx = Mathf.Cos(a);
                float sz = Mathf.Sin(a);
                float u = (float)i / sides;

                verts.Add(new Vector3(cx * innerRadius, y, sz * innerRadius));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(u, 0f));

                verts.Add(new Vector3(cx * outerRadius, y, sz * outerRadius));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(u, 1f));
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                int in_i  = baseIdx + 2 * i;
                int out_i = baseIdx + 2 * i + 1;
                int in_j  = baseIdx + 2 * j;
                int out_j = baseIdx + 2 * j + 1;
                tris.Add(in_i);  tris.Add(in_j);  tris.Add(out_i);
                tris.Add(out_i); tris.Add(in_j);  tris.Add(out_j);
            }
        }

        static void AppendDisc(
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
            List<int> tris, float radius, float y, int sides)
        {
            int baseIdx = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < sides; i++)
            {
                float a = (float)i / sides * Mathf.PI * 2f;
                float cx = Mathf.Cos(a);
                float sz = Mathf.Sin(a);
                verts.Add(new Vector3(cx * radius, y, sz * radius));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(cx * 0.5f + 0.5f, sz * 0.5f + 0.5f));
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                tris.Add(baseIdx);
                tris.Add(baseIdx + 1 + j);
                tris.Add(baseIdx + 1 + i);
            }
        }

        static Mesh s_unitySphere;
        static Mesh GetUnitySphereMesh()
        {
            if (s_unitySphere != null) return s_unitySphere;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s_unitySphere = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return s_unitySphere;
        }

        static void AppendUnitySphere(
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
            List<int> tris, Vector3 center, float radius)
        {
            var sphere = GetUnitySphereMesh();
            int baseIdx = verts.Count;
            var sv = sphere.vertices;
            var sn = sphere.normals;
            var su = sphere.uv;
            var st = sphere.triangles;

            float scale = radius * 2f;
            for (int i = 0; i < sv.Length; i++)
            {
                verts.Add(sv[i] * scale + center);
                normals.Add(sn[i]);
                uvs.Add(i < su.Length ? su[i] : Vector2.zero);
            }
            for (int i = 0; i < st.Length; i++) tris.Add(baseIdx + st[i]);
        }

        static void DeclusterPoints(List<Vector3> pts, float minDist, int passes)
        {
            float minSq = minDist * minDist;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    for (int j = i + 1; j < pts.Count; j++)
                    {
                        Vector3 diff = pts[j] - pts[i];
                        diff.y = 0f;
                        float d2 = diff.sqrMagnitude;
                        if (d2 < minSq && d2 > 1e-4f)
                        {
                            float d = Mathf.Sqrt(d2);
                            float push = (minDist - d) * 0.5f;
                            Vector3 dir = diff / d;
                            pts[i] = pts[i] - dir * push;
                            pts[j] = pts[j] + dir * push;
                        }
                        else if (d2 <= 1e-4f)
                        {

                            pts[j] = pts[j] + new Vector3(minDist * 0.5f, 0f, 0f);
                        }
                    }
                }
            }
        }

        static List<OsmNode> DeclusterFountainNodes(List<OsmNode> input, float minSpacing)
        {
            var kept = new List<OsmNode>(input.Count);
            if (minSpacing <= 0f)
            {
                kept.AddRange(input);
                return kept;
            }
            float minSq = minSpacing * minSpacing;
            foreach (var node in input)
            {
                bool tooClose = false;
                for (int i = 0; i < kept.Count; i++)
                {
                    float dx = node.Local.x - kept[i].Local.x;
                    float dz = node.Local.z - kept[i].Local.z;
                    if (dx * dx + dz * dz < minSq) { tooClose = true; break; }
                }
                if (!tooClose) kept.Add(node);
            }
            return kept;
        }

        static void AddAutoLampsAlongRoads(OsmDataset ds, List<Vector3> lampPoints,
                                           float laneWidth, float spacingMeters)
        {
            foreach (var w in ds.Roads)
            {
                string h = w.Highway ?? "";
                if (h.EndsWith("_link")) h = h.Substring(0, h.Length - 5);
                if (h != "primary" && h != "secondary") continue;
                if (w.Points.Count < 2) continue;

                int lc = DetermineLaneCount(w);
                float halfRoadW = (lc * laneWidth) * 0.5f;
                float lampOffset = halfRoadW + 1.5f;

                float distToNextLamp = spacingMeters * 0.5f;
                bool sideAlternate = false;

                for (int i = 0; i < w.Points.Count - 1; i++)
                {
                    Vector3 a = w.Points[i];     a.y = 0f;
                    Vector3 b = w.Points[i + 1]; b.y = 0f;
                    Vector3 seg = b - a;
                    float segLen = seg.magnitude;
                    if (segLen < 0.01f) continue;
                    Vector3 dir   = seg / segLen;
                    Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;

                    float traveled = 0f;
                    while (traveled + distToNextLamp <= segLen)
                    {
                        traveled += distToNextLamp;
                        Vector3 sideOff = (sideAlternate ? right : -right) * lampOffset;
                        Vector3 pos = a + dir * traveled + sideOff;
                        lampPoints.Add(pos);
                        sideAlternate = !sideAlternate;
                        distToNextLamp = spacingMeters;
                    }
                    distToNextLamp -= (segLen - traveled);
                    if (distToNextLamp < 0f) distToNextLamp = 0f;
                }
            }
        }

        static Material LoadOrCreateLampHeadMaterial(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) { Debug.LogError("[OsmRoadBaker] no shader for lamp head"); return null; }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != sh)
            {
                mat.shader = sh;
            }

            Color baseCol = new Color(0.95f, 0.85f, 0.55f, 1f);
            Color emis    = new Color(1.0f, 0.85f, 0.55f, 1f) * 1.5f;
            SetColorIfPresent(mat, "_BaseColor", baseCol);
            SetColorIfPresent(mat, "_Color",     baseCol);
            SetColorIfPresent(mat, "_EmissionColor", emis);
            SetFloatIfPresent(mat, "_Smoothness", 0.70f);
            SetFloatIfPresent(mat, "_Metallic",   0f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static void SetupDaytimeLighting(OsmRoadBaker baker)
        {
            if (baker == null) return;

            EnsureAssetFolder(baker.materialFolder);
            EnsureAssetFolder("Assets/GIS");

            Light sun = FindOrCreateSunLight();
            sun.type           = LightType.Directional;
            sun.color          = new Color(1.00f, 0.98f, 0.95f, 1f);
            sun.intensity      = 1.4f;
            sun.shadows        = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(70f, -10f, 0f);

            RenderSettings.ambientMode         = AmbientMode.Trilight;
            RenderSettings.ambientIntensity    = 1.0f;
            RenderSettings.ambientSkyColor     = new Color(0.50f, 0.68f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.78f, 0.85f);
            RenderSettings.ambientGroundColor  = new Color(0.45f, 0.45f, 0.48f);

            Material skyMat = LoadOrCreateSkyboxMaterial(baker.materialFolder, "DaytimeSky");
            if (skyMat != null)
            {
                RenderSettings.skybox = skyMat;
                RenderSettings.sun    = sun;
            }
            DynamicGI.UpdateEnvironment();

            SetupGlobalVolume();

            int cameraCount = EnableUrpPostProcessingOnAllCameras();

            EditorSceneMarkDirty(baker);

            Debug.Log($"[OsmRoadBaker] daytime lighting set up ({cameraCount} cameras)");
        }

        static Light FindOrCreateSunLight()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional) return l;
            }
            var go = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(go, "Create Sun");
            return go.AddComponent<Light>();
        }

        static Material LoadOrCreateSkyboxMaterial(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            Shader sh = Shader.Find("Skybox/Procedural");
            if (sh == null)
            {
                Debug.LogError("[OsmRoadBaker] Skybox/Procedural shader not found");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != sh)
            {
                mat.shader = sh;
            }

            SetFloatIfPresent(mat, "_SunSize",             0.04f);
            SetFloatIfPresent(mat, "_SunSizeConvergence",  5f);
            SetFloatIfPresent(mat, "_AtmosphereThickness", 0.8f);
            SetColorIfPresent(mat, "_SkyTint",             new Color(0.40f, 0.62f, 0.92f, 1f));
            SetColorIfPresent(mat, "_GroundColor",         new Color(0.55f, 0.55f, 0.55f, 1f));
            SetFloatIfPresent(mat, "_Exposure",            1.2f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetupGlobalVolume()
        {
            var existingVolumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            Volume existing = null;
            foreach (var v in existingVolumes)
            {
                if (v.isGlobal) { existing = v; break; }
            }

            GameObject volGo;
            if (existing != null)
            {
                volGo = existing.gameObject;
            }
            else
            {
                volGo = new GameObject("Global Volume");
                Undo.RegisterCreatedObjectUndo(volGo, "Create Volume");
            }

            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;

            string profilePath = "Assets/GIS/DaytimeVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            vol.sharedProfile = profile;

            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.35f;
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.0f;

            if (!profile.TryGet(out Tonemapping tonemap)) tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.overrideState = true; tonemap.mode.value = TonemappingMode.ACES;

            if (!profile.TryGet(out ColorAdjustments colorAdj)) colorAdj = profile.Add<ColorAdjustments>(true);
            colorAdj.contrast.overrideState   = true; colorAdj.contrast.value   = 8f;
            colorAdj.saturation.overrideState = true; colorAdj.saturation.value = 6f;

            EditorUtility.SetDirty(profile);
        }

        static int EnableUrpPostProcessingOnAllCameras()
        {
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            int count = 0;
            foreach (var cam in cams)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = true;
                    count++;
                }
            }
            return count;
        }

        static GroundKind ClassifyGroundCover(OsmWay w)
        {
            if (w.Leisure == "park" || w.Leisure == "garden")      return GroundKind.Park;
            if (w.Landuse == "grass" || w.Natural == "grassland")  return GroundKind.Grass;
            if (w.Natural == "water" || w.Waterway == "riverbank") return GroundKind.Water;
            if (!string.IsNullOrEmpty(w.Water))                    return GroundKind.Water;
            return GroundKind.None;
        }

        static Mesh BuildGroundCoverMesh(List<Vector2> footprint, float y)
        {
            int n = footprint.Count;
            if (n < 3) return null;

            if (PolygonArea(footprint) < 0f) footprint.Reverse();

            var verts   = new Vector3[n];
            var normals = new Vector3[n];
            var uvs     = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                verts[i]   = new Vector3(footprint[i].x, y, footprint[i].y);
                normals[i] = Vector3.up;
                uvs[i]     = footprint[i];
            }

            var earTris = EarClip(footprint);
            if (earTris.Count == 0) return null;

            var tris = new int[earTris.Count];
            for (int i = 0; i < earTris.Count; i += 3)
            {
                tris[i + 0] = earTris[i + 0];
                tris[i + 1] = earTris[i + 2];
                tris[i + 2] = earTris[i + 1];
            }

            var mesh = new Mesh();
            if (verts.Length > 65530) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildBaseGroundMesh(Vector2 min, Vector2 max, float y)
        {
            var verts = new Vector3[4];
            verts[0] = new Vector3(min.x, y, min.y);
            verts[1] = new Vector3(max.x, y, min.y);
            verts[2] = new Vector3(max.x, y, max.y);
            verts[3] = new Vector3(min.x, y, max.y);

            var normals = new Vector3[4] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            Vector2 size = max - min;
            var uvs = new Vector2[4];
            uvs[0] = new Vector2(0f,       0f);
            uvs[1] = new Vector2(size.x,   0f);
            uvs[2] = new Vector2(size.x,   size.y);
            uvs[3] = new Vector2(0f,       size.y);

            var tris = new int[] { 0, 3, 1, 1, 3, 2 };

            var mesh = new Mesh();
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static (Vector2 min, Vector2 max) ComputeOsmBounds(OsmDataset ds)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            bool any = false;

            void Accum(List<OsmWay> list)
            {
                foreach (var w in list)
                {
                    for (int i = 0; i < w.Points.Count; i++)
                    {
                        var p = w.Points[i];
                        if (p.x < minX) minX = p.x;
                        if (p.x > maxX) maxX = p.x;
                        if (p.z < minZ) minZ = p.z;
                        if (p.z > maxZ) maxZ = p.z;
                        any = true;
                    }
                }
            }

            Accum(ds.Roads);
            Accum(ds.Buildings);
            Accum(ds.GroundCovers);

            if (!any) return (Vector2.zero, Vector2.zero);
            return (new Vector2(minX, minZ), new Vector2(maxX, maxZ));
        }

        static (GameObject parent, int built, int skipped) BakeBuildingsInternal(
            OsmRoadBaker baker, List<OsmWay> buildings, Material mat)
        {
            var buildingsGo = new GameObject(baker.buildingsParentName);
            Undo.RegisterCreatedObjectUndo(buildingsGo, "Bake Buildings");
            buildingsGo.transform.SetParent(baker.transform, worldPositionStays: false);
            buildingsGo.isStatic = true;
            Transform parent = buildingsGo.transform;

            int built = 0, skipped = 0;

            foreach (var b in buildings)
            {
                var footprint = ExtractFootprint(b.Points);
                if (footprint.Count < 3) { skipped++; continue; }

                float height = DetermineBuildingHeight(b);
                if (height < 0.5f) { skipped++; continue; }

                Color tint = ColorForBuilding(b.Id);
                float parapetH = ShouldHaveParapet(b.Id) ? ParapetHeight(b.Id) : 0f;

                Mesh mesh = BuildBuildingMesh(footprint, height, parapetH, tint);
                if (mesh == null) { skipped++; continue; }

                Vector3 centroid = RecenterMeshOnCentroid(mesh);

                mesh.name = $"building_{b.Id}";
                string meshPath = $"{baker.meshOutputFolder}/building_{b.Id}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                    AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);

                var go = new GameObject($"building_{b.Id}");
                go.transform.SetParent(parent, worldPositionStays: false);
                go.transform.position = centroid;
                go.layer = 0;
                go.isStatic = true;

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;

                var bc = go.AddComponent<BoxCollider>();
                bc.center = mesh.bounds.center;
                bc.size   = mesh.bounds.size;

                built++;
            }

            return (buildingsGo, built, skipped);
        }

        static Vector3 RecenterMeshOnCentroid(Mesh mesh)
        {
            var verts = mesh.vertices;
            if (verts.Length == 0) return Vector3.zero;

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < verts.Length; i++) centroid += verts[i];
            centroid /= verts.Length;

            for (int i = 0; i < verts.Length; i++) verts[i] -= centroid;

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            return centroid;
        }

        static WayBakeKind ClassifyWay(string highway)
        {
            if (string.IsNullOrEmpty(highway)) return WayBakeKind.Car;
            string h = highway;
            if (h.EndsWith("_link")) h = h.Substring(0, h.Length - 5);
            switch (h)
            {
                case "footway":
                case "pedestrian": return WayBakeKind.Footway;
                case "path":       return WayBakeKind.Path;
                default:           return WayBakeKind.Car;
            }
        }

        static int DetermineLaneCount(OsmWay w)
        {
            if (w.Lanes.HasValue)
                return Mathf.Clamp(w.Lanes.Value, 1, 4);

            string h = w.Highway ?? "";
            if (h.EndsWith("_link")) h = h.Substring(0, h.Length - 5);
            return WideHighways.Contains(h) ? 4 : 2;
        }

        static bool IsNearOrigin(OsmWay w, float r2)
        {
            var pts = w.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (p.x * p.x + p.z * p.z <= r2) return true;
            }
            return false;
        }

        static Mesh BuildRoadMesh(List<Vector3> pts, float width, int laneCount, float yOffset, float maxMiter)
        {
            int n = pts.Count;
            if (n < 2) return null;

            var verts   = new Vector3[n * 2];
            var uvs     = new Vector2[n * 2];
            var uv2s    = new Vector2[n * 2];
            var normals = new Vector3[n * 2];

            float halfW = width * 0.5f;
            float cumDist = 0f;
            Vector3 prev = pts[0]; prev.y = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector3 p = pts[i]; p.y = 0f;

                Vector3 inDir = (i > 0) ? (p - prev) : Vector3.zero;
                if (inDir.sqrMagnitude > 1e-8f) inDir.Normalize(); else inDir = Vector3.zero;

                Vector3 outDir = Vector3.zero;
                if (i < n - 1)
                {
                    var nxt = pts[i + 1]; nxt.y = 0f;
                    outDir = nxt - p;
                    if (outDir.sqrMagnitude > 1e-8f) outDir.Normalize(); else outDir = Vector3.zero;
                }

                Vector3 right;
                float miter = 1f;

                if (i == 0)                  right = Vector3.Cross(Vector3.up, outDir).normalized;
                else if (i == n - 1)         right = Vector3.Cross(Vector3.up, inDir).normalized;
                else
                {
                    Vector3 nIn  = Vector3.Cross(Vector3.up, inDir).normalized;
                    Vector3 nOut = Vector3.Cross(Vector3.up, outDir).normalized;
                    Vector3 sum  = nIn + nOut;
                    if (sum.sqrMagnitude < 1e-6f) { right = nIn; }
                    else
                    {
                        Vector3 avg = sum.normalized;
                        float cosHalf = Vector3.Dot(avg, nIn);
                        if (Mathf.Abs(cosHalf) < 1e-3f) cosHalf = 1e-3f;
                        miter = Mathf.Clamp(1f / cosHalf, 1f, maxMiter);
                        right = avg;
                    }
                }

                Vector3 offset = right * (halfW * miter);
                offset.y = 0f;

                Vector3 basePos = new Vector3(p.x, yOffset, p.z);
                verts[2 * i]     = basePos - offset;
                verts[2 * i + 1] = basePos + offset;
                normals[2 * i]     = Vector3.up;
                normals[2 * i + 1] = Vector3.up;

                if (i > 0) cumDist += Vector3.Distance(p, prev);
                uvs[2 * i]     = new Vector2(0f, cumDist);
                uvs[2 * i + 1] = new Vector2(1f, cumDist);
                uv2s[2 * i]     = new Vector2(width, laneCount);
                uv2s[2 * i + 1] = new Vector2(width, laneCount);

                prev = p;
            }

            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n - 1; i++)
            {
                int t = i * 6;
                tris[t + 0] = 2 * i;
                tris[t + 1] = 2 * i + 2;
                tris[t + 2] = 2 * i + 1;
                tris[t + 3] = 2 * i + 1;
                tris[t + 4] = 2 * i + 2;
                tris[t + 5] = 2 * i + 3;
            }

            var mesh = new Mesh();
            if (verts.Length > 65530) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.uv        = uvs;
            mesh.uv2       = uv2s;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static int BakeJunctionPatches(
            OsmRoadBaker baker,
            List<(OsmWay way, int laneCount, float width)> bakedRoads,
            Material mat)
        {
            var nodeToEntries = new Dictionary<long, List<(OsmWay way, float width, int idx)>>();
            foreach (var entry in bakedRoads)
            {
                var ids = entry.way.NodeIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    long nid = ids[i];
                    if (!nodeToEntries.TryGetValue(nid, out var list))
                        nodeToEntries[nid] = list = new List<(OsmWay, float, int)>();
                    list.Add((entry.way, entry.width, i));
                }
            }

            var junctionsGo = new GameObject(baker.junctionsParentName);
            Undo.RegisterCreatedObjectUndo(junctionsGo, "Bake Junctions");
            junctionsGo.transform.SetParent(baker.transform, worldPositionStays: false);
            junctionsGo.isStatic = true;
            Transform junctions = junctionsGo.transform;

            int built = 0;
            foreach (var kv in nodeToEntries)
            {
                var entries = kv.Value;
                if (CountDistinctWays(entries) < 2) continue;

                long nodeId = kv.Key;
                Vector3 center = entries[0].way.Points[entries[0].idx];
                center.y = 0f;

                var corners = new List<Vector3>(entries.Count * 2);
                foreach (var entry in entries)
                {
                    float halfW = entry.width * 0.5f;
                    ComputeEdgeAtNode(entry.way.Points, entry.idx, halfW, baker.maxMiter,
                                      out Vector3 l, out Vector3 r);
                    corners.Add(l);
                    corners.Add(r);
                }

                Mesh jMesh = BuildJunctionMesh(center, corners, baker.yOffset);
                if (jMesh == null) continue;

                Vector3 centroid = RecenterMeshOnCentroid(jMesh);

                jMesh.name = $"junction_{nodeId}";
                string meshPath = $"{baker.meshOutputFolder}/junction_{nodeId}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                    AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(jMesh, meshPath);

                var jGo = new GameObject($"junction_{nodeId}");
                jGo.transform.SetParent(junctions, worldPositionStays: false);
                jGo.transform.position = centroid;
                jGo.layer = 0;
                jGo.isStatic = true;

                var mf = jGo.AddComponent<MeshFilter>();
                mf.sharedMesh = jMesh;
                var mr = jGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                var mc = jGo.AddComponent<MeshCollider>();
                mc.sharedMesh = jMesh;
                mc.convex = false;

                built++;
            }

            return built;
        }

        static int CountDistinctWays(List<(OsmWay way, float width, int idx)> entries)
        {
            if (entries.Count <= 1) return entries.Count;
            var seen = new HashSet<long>();
            foreach (var e in entries) seen.Add(e.way.Id);
            return seen.Count;
        }

        static void ComputeEdgeAtNode(
            List<Vector3> pts, int idx, float halfW, float maxMiter,
            out Vector3 left, out Vector3 right)
        {
            int n = pts.Count;
            Vector3 p = pts[idx]; p.y = 0f;

            Vector3 inDir = Vector3.zero;
            if (idx > 0)
            {
                Vector3 prev = pts[idx - 1]; prev.y = 0f;
                Vector3 d = p - prev;
                if (d.sqrMagnitude > 1e-8f) inDir = d.normalized;
            }

            Vector3 outDir = Vector3.zero;
            if (idx < n - 1)
            {
                Vector3 nxt = pts[idx + 1]; nxt.y = 0f;
                Vector3 d = nxt - p;
                if (d.sqrMagnitude > 1e-8f) outDir = d.normalized;
            }

            Vector3 rightV;
            float miter = 1f;

            if (idx == 0 && outDir != Vector3.zero)
                rightV = Vector3.Cross(Vector3.up, outDir).normalized;
            else if (idx == n - 1 && inDir != Vector3.zero)
                rightV = Vector3.Cross(Vector3.up, inDir).normalized;
            else if (inDir != Vector3.zero && outDir != Vector3.zero)
            {
                Vector3 nIn  = Vector3.Cross(Vector3.up, inDir).normalized;
                Vector3 nOut = Vector3.Cross(Vector3.up, outDir).normalized;
                Vector3 sum  = nIn + nOut;
                if (sum.sqrMagnitude < 1e-6f) rightV = nIn;
                else
                {
                    Vector3 avg = sum.normalized;
                    float cosHalf = Vector3.Dot(avg, nIn);
                    if (Mathf.Abs(cosHalf) < 1e-3f) cosHalf = 1e-3f;
                    miter = Mathf.Clamp(1f / cosHalf, 1f, maxMiter);
                    rightV = avg;
                }
            }
            else
            {
                rightV = Vector3.right;
            }

            Vector3 offset = rightV * (halfW * miter);
            left  = new Vector3(p.x - offset.x, 0f, p.z - offset.z);
            right = new Vector3(p.x + offset.x, 0f, p.z + offset.z);
        }

        static Mesh BuildJunctionMesh(Vector3 center, List<Vector3> corners, float yOffset)
        {
            if (corners.Count < 3) return null;

            corners.Sort((a, b) =>
            {
                float angA = Mathf.Atan2(a.z - center.z, a.x - center.x);
                float angB = Mathf.Atan2(b.z - center.z, b.x - center.x);
                return angA.CompareTo(angB);
            });

            int m = corners.Count;
            var verts   = new Vector3[m + 1];
            var uvs     = new Vector2[m + 1];
            var uv2s    = new Vector2[m + 1];
            var normals = new Vector3[m + 1];

            verts[0]   = new Vector3(center.x, yOffset, center.z);
            uvs[0]     = new Vector2(0.5f, 0f);
            uv2s[0]    = new Vector2(100f, 1f);
            normals[0] = Vector3.up;

            for (int i = 0; i < m; i++)
            {
                verts[i + 1]   = new Vector3(corners[i].x, yOffset, corners[i].z);
                uvs[i + 1]     = new Vector2(0.5f, 0f);
                uv2s[i + 1]    = new Vector2(100f, 1f);
                normals[i + 1] = Vector3.up;
            }

            var tris = new int[m * 3];
            for (int i = 0; i < m; i++)
            {
                int next = (i + 1) % m;
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = next + 1;
                tris[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh();
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.uv        = uvs;
            mesh.uv2       = uv2s;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static List<Vector2> ExtractFootprint(List<Vector3> pts)
        {
            var fp = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
                fp.Add(new Vector2(pts[i].x, pts[i].z));
            if (fp.Count > 1 && Vector2.Distance(fp[0], fp[fp.Count - 1]) < 1e-3f)
                fp.RemoveAt(fp.Count - 1);
            return fp;
        }

        static float DetermineBuildingHeight(OsmWay b)
        {
            if (b.HeightMeters.HasValue && b.HeightMeters.Value > 0f)
                return b.HeightMeters.Value;
            if (b.BuildingLevels.HasValue && b.BuildingLevels.Value > 0)
                return b.BuildingLevels.Value * 3.2f;

            var rng = new System.Random(unchecked((int)b.Id));
            return 15f + (float)rng.NextDouble() * 6f;
        }

        static readonly Color[] BuildingPalette = new[]
        {
            new Color(0.78f, 0.76f, 0.72f, 1f),
            new Color(0.62f, 0.34f, 0.28f, 1f),
            new Color(0.92f, 0.86f, 0.72f, 1f),
            new Color(0.80f, 0.70f, 0.52f, 1f),
            new Color(0.66f, 0.74f, 0.80f, 1f),
        };

        static Color ColorForBuilding(long id)
        {
            int idx = new System.Random(unchecked((int)id ^ 0x70616C00)).Next(BuildingPalette.Length);
            return BuildingPalette[idx];
        }

        static bool ShouldHaveParapet(long id)
        {
            return new System.Random(unchecked((int)id ^ 0x70617200)).NextDouble() < 0.40;
        }

        static float ParapetHeight(long id)
        {
            return 0.3f + (float)new System.Random(unchecked((int)id ^ 0x70617248)).NextDouble() * 0.4f;
        }

        static Mesh BuildBuildingMesh(List<Vector2> footprint, float height, float parapetH, Color tint)
        {
            int n = footprint.Count;
            if (n < 3 || height <= 0f) return null;

            if (PolygonArea(footprint) < 0f)
                footprint.Reverse();

            float wallTop = height + Mathf.Max(0f, parapetH);
            Color32 tint32 = tint;

            int doorWallIdx = 0;
            float longestLen = 0f;
            for (int wi = 0; wi < n; wi++)
            {
                int wj = (wi + 1) % n;
                float len = (footprint[wj] - footprint[wi]).magnitude;
                if (len > longestLen) { longestLen = len; doorWallIdx = wi; }
            }

            var verts     = new List<Vector3>(n * 5);
            var normals   = new List<Vector3>(n * 5);
            var uvs       = new List<Vector2>(n * 5);
            var uv2s      = new List<Vector2>(n * 5);
            var uv3s      = new List<Vector2>(n * 5);
            var colors    = new List<Color32>(n * 5);
            var triangles = new List<int>(n * 6 + Mathf.Max(0, n - 2) * 3);

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 a = footprint[i];
                Vector2 b = footprint[j];

                Vector3 edge = new Vector3(b.x - a.x, 0f, b.y - a.y);
                if (edge.sqrMagnitude < 1e-8f) continue;
                Vector3 edgeDir = edge.normalized;
                Vector3 outward = Vector3.Cross(Vector3.up, edgeDir).normalized;

                Vector3 bottomA = new Vector3(a.x, 0f,      a.y);
                Vector3 bottomB = new Vector3(b.x, 0f,      b.y);
                Vector3 topA    = new Vector3(a.x, wallTop, a.y);
                Vector3 topB    = new Vector3(b.x, wallTop, b.y);

                int baseIdx = verts.Count;
                verts.Add(bottomA); verts.Add(bottomB); verts.Add(topB); verts.Add(topA);
                normals.Add(outward); normals.Add(outward); normals.Add(outward); normals.Add(outward);

                float edgeLen = edge.magnitude;
                uvs.Add(new Vector2(0f,       0f));
                uvs.Add(new Vector2(edgeLen,  0f));
                uvs.Add(new Vector2(edgeLen,  wallTop));
                uvs.Add(new Vector2(0f,       wallTop));

                Vector2 wallUv2 = new Vector2(height, 0f);
                uv2s.Add(wallUv2); uv2s.Add(wallUv2); uv2s.Add(wallUv2); uv2s.Add(wallUv2);

                Vector2 doorInfo = (i == doorWallIdx)
                    ? new Vector2(edgeLen * 0.5f, 1f)
                    : new Vector2(0f, 0f);
                uv3s.Add(doorInfo); uv3s.Add(doorInfo); uv3s.Add(doorInfo); uv3s.Add(doorInfo);

                colors.Add(tint32); colors.Add(tint32); colors.Add(tint32); colors.Add(tint32);

                triangles.Add(baseIdx + 0);
                triangles.Add(baseIdx + 3);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 0);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 1);
            }

            int roofStart = verts.Count;
            Vector2 roofUv2 = new Vector2(height, 0f);
            Vector2 roofUv3 = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                verts.Add(new Vector3(footprint[i].x, height, footprint[i].y));
                normals.Add(Vector3.up);
                uvs.Add(footprint[i]);
                uv2s.Add(roofUv2);
                uv3s.Add(roofUv3);
                colors.Add(tint32);
            }

            var roofTris = EarClip(footprint);
            for (int i = 0; i < roofTris.Count; i += 3)
            {
                triangles.Add(roofStart + roofTris[i + 0]);
                triangles.Add(roofStart + roofTris[i + 2]);
                triangles.Add(roofStart + roofTris[i + 1]);
            }

            var mesh = new Mesh();
            if (verts.Count > 65530) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv2s);
            mesh.SetUVs(2, uv3s);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static List<int> EarClip(List<Vector2> polygon)
        {
            var triangles = new List<int>();
            int n = polygon.Count;
            if (n < 3) return triangles;

            var ring = new List<int>(n);
            for (int i = 0; i < n; i++) ring.Add(i);

            int safety = n * n + 8;
            while (ring.Count > 3 && safety-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int iPrev = ring[(i + ring.Count - 1) % ring.Count];
                    int iCurr = ring[i];
                    int iNext = ring[(i + 1) % ring.Count];

                    if (IsEar(polygon, iPrev, iCurr, iNext, ring))
                    {
                        triangles.Add(iPrev);
                        triangles.Add(iCurr);
                        triangles.Add(iNext);
                        ring.RemoveAt(i);
                        earFound = true;
                        break;
                    }
                }
                if (!earFound) break;
            }

            if (ring.Count == 3)
            {
                triangles.Add(ring[0]);
                triangles.Add(ring[1]);
                triangles.Add(ring[2]);
            }
            return triangles;
        }

        static bool IsEar(List<Vector2> poly, int a, int b, int c, List<int> remaining)
        {
            Vector2 va = poly[a], vb = poly[b], vc = poly[c];

            float cross = (vb.x - va.x) * (vc.y - vb.y) - (vb.y - va.y) * (vc.x - vb.x);
            if (cross <= 0f) return false;

            foreach (int idx in remaining)
            {
                if (idx == a || idx == b || idx == c) continue;
                if (PointInTriangle(poly[idx], va, vb, vc)) return false;
            }
            return true;
        }

        static float PolygonArea(List<Vector2> poly)
        {
            float area = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += poly[i].x * poly[j].y - poly[j].x * poly[i].y;
            }
            return area * 0.5f;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = TriSign(p, a, b);
            float d2 = TriSign(p, b, c);
            float d3 = TriSign(p, c, a);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        static float TriSign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        static Material LoadOrCreateRoadMaterial(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            Shader roadShader = Shader.Find("GIS/RoadLit");
            if (roadShader == null)
            {
                Debug.LogWarning("[OsmRoadBaker] GIS/RoadLit shader not found, falling back to URP/Lit");
                roadShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (roadShader == null)
            {
                Debug.LogError("[OsmRoadBaker] no usable shader found for roads");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(roadShader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != roadShader)
            {
                mat.shader = roadShader;
            }

            Color grey  = new Color(0.22f, 0.22f, 0.22f, 1f);
            Color white = new Color(0.92f, 0.92f, 0.92f, 1f);
            SetColorIfPresent(mat, "_BaseColor",          grey);
            SetColorIfPresent(mat, "_Color",              grey);
            SetColorIfPresent(mat, "_MarkingColor",       white);
            SetFloatIfPresent(mat, "_EdgeLineMeters",     0.15f);
            SetFloatIfPresent(mat, "_CenterLineMeters",   0.15f);
            SetFloatIfPresent(mat, "_CenterDashOnMeters", 3.0f);
            SetFloatIfPresent(mat, "_CenterDashOffMeters",6.0f);
            SetFloatIfPresent(mat, "_CenterDashed",       1.0f);
            SetFloatIfPresent(mat, "_Smoothness",         0.15f);
            SetFloatIfPresent(mat, "_Metallic",           0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateBuildingMaterial(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            Shader sh = Shader.Find("GIS/BuildingFacade");
            if (sh == null)
            {
                Debug.LogWarning("[OsmRoadBaker] GIS/BuildingFacade shader not found, falling back to URP/Lit (no windows)");
                sh = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (sh == null)
            {
                Debug.LogError("[OsmRoadBaker] no usable shader found for buildings");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != sh)
            {
                mat.shader = sh;
            }

            Color white      = Color.white;
            Color windowCol  = new Color(0.18f, 0.22f, 0.30f, 1f);
            Color frameCol   = new Color(0.10f, 0.10f, 0.10f, 1f);
            Color doorCol    = new Color(0.06f, 0.05f, 0.04f, 1f);
            SetColorIfPresent(mat, "_BaseColor",          white);
            SetColorIfPresent(mat, "_Color",              white);
            SetColorIfPresent(mat, "_WindowColor",        windowCol);
            SetColorIfPresent(mat, "_FrameColor",         frameCol);
            SetColorIfPresent(mat, "_DoorColor",          doorCol);
            SetFloatIfPresent(mat, "_WindowWidth",        1.5f);
            SetFloatIfPresent(mat, "_WindowHeight",       1.4f);
            SetFloatIfPresent(mat, "_WindowSpacingH",     3.0f);
            SetFloatIfPresent(mat, "_FloorHeight",        3.2f);
            SetFloatIfPresent(mat, "_FrameWidth",         0.06f);
            SetFloatIfPresent(mat, "_DoorWidth",          1.2f);
            SetFloatIfPresent(mat, "_DoorHeight",         2.2f);
            SetFloatIfPresent(mat, "_DoorMargin",         0.2f);
            SetFloatIfPresent(mat, "_RoofGapMeters",      1.0f);
            SetFloatIfPresent(mat, "_WindowsEnabled",     1f);
            SetFloatIfPresent(mat, "_DoorEnabled",        1f);
            SetFloatIfPresent(mat, "_Smoothness",         0.08f);
            SetFloatIfPresent(mat, "_Metallic",           0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateUrpLitMaterial(string folder, string name, Color baseColor,
                                                   float smoothness = 0.10f, float metallic = 0f)
        {
            string path = $"{folder}/{name}.mat";
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null)
            {
                Debug.LogError($"[OsmRoadBaker] no URP/Lit or Standard shader for '{name}'");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != sh)
            {
                mat.shader = sh;
            }

            SetColorIfPresent(mat, "_BaseColor",  baseColor);
            SetColorIfPresent(mat, "_Color",      baseColor);
            SetFloatIfPresent(mat, "_Smoothness", smoothness);
            SetFloatIfPresent(mat, "_Metallic",   metallic);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetColorIfPresent(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, c);
        }
        static void SetFloatIfPresent(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        static void EnsureAssetFolder(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void EditorSceneMarkDirty(OsmRoadBaker baker)
        {
            if (baker == null) return;
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(baker.gameObject.scene);
            }
        }
    }
}
