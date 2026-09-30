using System.IO;
using UnityEngine;

namespace GIS
{

    [ExecuteAlways]
    public class OsmNetworkVisualizer : MonoBehaviour
    {
        public string osmFile = "unirii.osm";

        public bool overrideOrigin = false;
        public double originLat = 44.4268;
        public double originLon = 26.1025;

        [Header("Drawing")]
        public bool drawRoads = true;
        public bool drawBuildings = true;
        public Color roadColor = Color.white;
        public Color buildingColor = Color.cyan;

        OsmDataset cached;
        string cachedFile;
        double cachedOriginLat, cachedOriginLon;
        bool loggedDrawSummary;

        [ContextMenu("Reparse")]
        public void Reparse()
        {
            cached = null;
            cachedFile = null;
            loggedDrawSummary = false;
            EnsureParsed();
        }

        void OnEnable()
        {
            EnsureParsed();
        }

        void OnValidate()
        {

            cached = null;
            cachedFile = null;
            loggedDrawSummary = false;
        }

        void EnsureParsed()
        {
            double useLat = overrideOrigin ? originLat : GeoProjection.OriginLat;
            double useLon = overrideOrigin ? originLon : GeoProjection.OriginLon;

            if (cached != null
                && cachedFile == osmFile
                && cachedOriginLat == useLat
                && cachedOriginLon == useLon)
                return;

            if (string.IsNullOrEmpty(osmFile))
            {
                Debug.LogWarning("[OsmNetworkVisualizer] osmFile is empty");
                cached = new OsmDataset();
                cachedFile = osmFile;
                cachedOriginLat = useLat;
                cachedOriginLon = useLon;
                return;
            }

            if (overrideOrigin) GeoProjection.SetOrigin(originLat, originLon);

            string resolved = Path.Combine(Application.dataPath, "GIS", "Data", osmFile);
            Debug.Log($"[OsmNetworkVisualizer] parsing {resolved}");

            try
            {
                cached = OsmParser.Parse(osmFile);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OsmNetworkVisualizer] parse threw: {e}");
                cached = new OsmDataset();
            }

            cachedFile = osmFile;
            cachedOriginLat = GeoProjection.OriginLat;
            cachedOriginLon = GeoProjection.OriginLon;
            loggedDrawSummary = false;
        }

        void OnDrawGizmos()
        {
            EnsureParsed();
            if (cached == null) return;

            Vector3 anchor = transform.position;
            int drewRoad = 0, drewBuilding = 0;

            if (drawRoads)
            {
                Gizmos.color = roadColor;
                for (int i = 0; i < cached.Roads.Count; i++)
                {
                    var pts = cached.Roads[i].Points;
                    for (int j = 0; j < pts.Count - 1; j++)
                    {
                        Gizmos.DrawLine(anchor + pts[j], anchor + pts[j + 1]);
                        drewRoad++;
                    }
                }
            }

            if (drawBuildings)
            {
                Gizmos.color = buildingColor;
                for (int i = 0; i < cached.Buildings.Count; i++)
                {
                    var pts = cached.Buildings[i].Points;
                    for (int j = 0; j < pts.Count - 1; j++)
                    {
                        Gizmos.DrawLine(anchor + pts[j], anchor + pts[j + 1]);
                        drewBuilding++;
                    }
                }
            }

            if (!loggedDrawSummary)
            {
                Debug.Log($"[OsmNetworkVisualizer] drew {drewRoad} road segments, {drewBuilding} building segments at anchor {anchor}");
                loggedDrawSummary = true;
            }
        }
    }
}
