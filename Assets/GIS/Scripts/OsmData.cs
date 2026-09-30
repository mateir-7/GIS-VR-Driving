using System.Collections.Generic;
using UnityEngine;

namespace GIS
{
    public enum OsmWayKind { Other, Road, Building, GroundCover }

    public class OsmWay
    {
        public long Id;
        public OsmWayKind Kind;
        public string Highway;
        public string Building;
        public string Leisure;
        public string Landuse;
        public string Natural;
        public string Waterway;
        public string Water;
        public string Amenity;
        public int? Lanes;
        public float? WidthMeters;
        public float? HeightMeters;
        public int? BuildingLevels;
        public bool IsClosed;
        public List<Vector3> Points = new List<Vector3>();

        public List<long> NodeIds = new List<long>();
    }

    public class OsmNode
    {
        public long Id;
        public double Lat;
        public double Lon;
        public Vector3 Local;
        public string Natural;
        public string Highway;
        public string Amenity;
        public string PublicTransport;
        public string Historic;
    }

    public class OsmDataset
    {
        public List<OsmWay> Roads = new List<OsmWay>();
        public List<OsmWay> Buildings = new List<OsmWay>();
        public List<OsmWay> GroundCovers = new List<OsmWay>();
        public List<OsmNode> TaggedNodes = new List<OsmNode>();
    }
}
