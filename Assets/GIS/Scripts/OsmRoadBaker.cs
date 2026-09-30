using UnityEngine;

namespace GIS
{

    [DisallowMultipleComponent]
    public class OsmRoadBaker : MonoBehaviour
    {
        public string osmFile = "unirii.osm";

        public bool overrideOrigin = false;
        public double originLat = 44.4268;
        public double originLon = 26.1025;

        [Header("Output")]
        public string roadsParentName = "Roads";
        public string meshOutputFolder = "Assets/GIS/Meshes";
        public string materialFolder = "Assets/GIS/Materials";
        public string materialName = "RoadGrey";
        public string footwayMaterialName = "FootwayGrey";
        public string pathMaterialName = "PathDirt";

        [Header("Bake")]
        public float laneWidthMeters = 4f;
        public float footwayWidthMeters = 2.5f;
        public float pathWidthMeters = 1.5f;
        public float yOffset = 0f;
        public float maxMiter = 3f;

        public bool bakeJunctions = true;
        public string junctionsParentName = "Junctions";

        public string buildingsParentName = "Buildings";
        public string buildingMaterialName = "BuildingGrey";

        public string propsParentName = "Props";

        public string decorParentName = "Decor";
        public float fountainMinSpacingMeters = 15f;

        public string aiWaypointsParentName = "AIWaypoints";

        public string groundCoverParentName = "GroundCover";
        public string baseGroundName = "BaseGround";
        public float groundCoverY = 0.01f;
        public float baseGroundY = 0f;
        public float baseGroundMargin = 50f;
    }
}
