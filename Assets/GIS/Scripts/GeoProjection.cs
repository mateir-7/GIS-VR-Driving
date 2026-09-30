using UnityEngine;

namespace GIS
{

    public static class GeoProjection
    {
        public const double MetersPerDegLat = 111320.0;

        public static double OriginLat = 44.4268;
        public static double OriginLon = 26.1025;

        public static void SetOrigin(double lat, double lon)
        {
            OriginLat = lat;
            OriginLon = lon;
        }

        public static Vector3 LatLonToLocal(double lat, double lon)
        {
            double cosLat = System.Math.Cos(OriginLat * System.Math.PI / 180.0);
            double x = (lon - OriginLon) * MetersPerDegLat * cosLat;
            double z = (lat - OriginLat) * MetersPerDegLat;
            return new Vector3((float)x, 0f, (float)z);
        }
    }
}
