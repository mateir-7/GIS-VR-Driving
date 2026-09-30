using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace GIS
{

    public static class OsmParser
    {

        public static string ResolveDataPath(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return null;
            if (Path.IsPathRooted(filename)) return filename;
            return Path.Combine(Application.dataPath, "GIS", "Data", filename);
        }

        public static OsmDataset Parse(string filename)
        {
            var ds = new OsmDataset();
            string path = ResolveDataPath(filename);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[OsmParser] file not found: {path}");
                return ds;
            }

            var inv = CultureInfo.InvariantCulture;
            var nodes = new Dictionary<long, (double lat, double lon)>(capacity: 1 << 14);
            var ways = new List<OsmWay>();

            using (var stream = File.OpenRead(path))
            using (var reader = XmlReader.Create(stream))
            {
                OsmWay currentWay = null;
                List<long> currentRefs = null;
                OsmNode currentTaggedNode = null;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.Name)
                        {
                            case "node":
                            {
                                long id = long.Parse(reader.GetAttribute("id"), inv);
                                double lat = double.Parse(reader.GetAttribute("lat"), inv);
                                double lon = double.Parse(reader.GetAttribute("lon"), inv);
                                nodes[id] = (lat, lon);

                                if (!reader.IsEmptyElement)
                                    currentTaggedNode = new OsmNode { Id = id, Lat = lat, Lon = lon };
                                else
                                    currentTaggedNode = null;
                                break;
                            }
                            case "way":
                            {
                                currentWay = new OsmWay { Id = long.Parse(reader.GetAttribute("id"), inv) };
                                currentRefs = new List<long>();
                                break;
                            }
                            case "nd":
                                if (currentWay != null)
                                {
                                    currentRefs.Add(long.Parse(reader.GetAttribute("ref"), inv));
                                }
                                break;
                            case "tag":
                            {
                                string tk = reader.GetAttribute("k");
                                string tv = reader.GetAttribute("v");
                                if (currentWay != null)
                                    ApplyTag(currentWay, tk, tv, inv);
                                else if (currentTaggedNode != null)
                                    ApplyNodeTag(currentTaggedNode, tk, tv);
                                break;
                            }
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "way" && currentWay != null)
                    {

                        for (int i = 0; i < currentRefs.Count; i++)
                        {
                            if (nodes.TryGetValue(currentRefs[i], out var ll))
                            {
                                currentWay.Points.Add(GeoProjection.LatLonToLocal(ll.lat, ll.lon));
                                currentWay.NodeIds.Add(currentRefs[i]);
                            }
                        }
                        if (currentRefs.Count >= 2)
                            currentWay.IsClosed = currentRefs[0] == currentRefs[currentRefs.Count - 1];

                        ways.Add(currentWay);
                        currentWay = null;
                        currentRefs = null;
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "node" && currentTaggedNode != null)
                    {
                        if (!string.IsNullOrEmpty(currentTaggedNode.Natural)
                         || !string.IsNullOrEmpty(currentTaggedNode.Highway)
                         || !string.IsNullOrEmpty(currentTaggedNode.Amenity)
                         || !string.IsNullOrEmpty(currentTaggedNode.PublicTransport)
                         || !string.IsNullOrEmpty(currentTaggedNode.Historic))
                        {
                            currentTaggedNode.Local = GeoProjection.LatLonToLocal(currentTaggedNode.Lat, currentTaggedNode.Lon);
                            ds.TaggedNodes.Add(currentTaggedNode);
                        }
                        currentTaggedNode = null;
                    }
                }
            }

            foreach (var w in ways)
            {
                if (w.Points.Count < 2) continue;
                if (w.Kind == OsmWayKind.Road) ds.Roads.Add(w);
                else if (w.Kind == OsmWayKind.Building) ds.Buildings.Add(w);
                else if (w.Kind == OsmWayKind.GroundCover) ds.GroundCovers.Add(w);
            }

            Debug.Log($"[OsmParser] {Path.GetFileName(path)}: {nodes.Count} nodes, {ways.Count} ways -> {ds.Roads.Count} roads, {ds.Buildings.Count} buildings, {ds.GroundCovers.Count} ground covers, {ds.TaggedNodes.Count} tagged nodes");
            return ds;
        }

        static void ApplyNodeTag(OsmNode n, string k, string v)
        {
            if (k == null || v == null) return;
            switch (k)
            {
                case "natural":          n.Natural = v; break;
                case "highway":          n.Highway = v; break;
                case "amenity":          n.Amenity = v; break;
                case "public_transport": n.PublicTransport = v; break;
                case "historic":         n.Historic = v; break;
            }
        }

        static void ApplyTag(OsmWay w, string k, string v, CultureInfo inv)
        {
            if (k == null || v == null) return;

            switch (k)
            {
                case "highway":
                    w.Kind = OsmWayKind.Road;
                    w.Highway = v;
                    break;
                case "building":

                    if (w.Kind == OsmWayKind.Other)
                    {
                        w.Kind = OsmWayKind.Building;
                        w.Building = v;
                    }
                    break;
                case "lanes":
                    if (int.TryParse(v, NumberStyles.Integer, inv, out int n)) w.Lanes = n;
                    break;
                case "width":
                    if (float.TryParse(v, NumberStyles.Float, inv, out float wm)) w.WidthMeters = wm;
                    break;
                case "height":
                case "building:height":
                    if (float.TryParse(v, NumberStyles.Float, inv, out float hm)) w.HeightMeters = hm;
                    break;
                case "building:levels":
                    if (int.TryParse(v, NumberStyles.Integer, inv, out int lv)) w.BuildingLevels = lv;
                    break;
                case "leisure":
                    w.Leisure = v;
                    if (w.Kind == OsmWayKind.Other) w.Kind = OsmWayKind.GroundCover;
                    break;
                case "landuse":
                    w.Landuse = v;
                    if (w.Kind == OsmWayKind.Other) w.Kind = OsmWayKind.GroundCover;
                    break;
                case "natural":
                    w.Natural = v;
                    if (w.Kind == OsmWayKind.Other) w.Kind = OsmWayKind.GroundCover;
                    break;
                case "waterway":
                    w.Waterway = v;
                    if (w.Kind == OsmWayKind.Other) w.Kind = OsmWayKind.GroundCover;
                    break;
                case "water":
                    w.Water = v;
                    if (w.Kind == OsmWayKind.Other) w.Kind = OsmWayKind.GroundCover;
                    break;
                case "amenity":
                    w.Amenity = v;
                    if (w.Kind == OsmWayKind.Other) w.Kind = OsmWayKind.GroundCover;
                    break;
            }
        }
    }
}
