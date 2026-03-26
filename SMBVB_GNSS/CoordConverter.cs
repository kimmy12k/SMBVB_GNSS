using System;

namespace SMBVB_GNSS
{
    /// <summary>
    /// WGS-84 ↔ ECEF 좌표 변환
    /// 
    /// SMBV100B HIL UDP 패킷은 ECEF(지구 중심 직교좌표)를 사용합니다.
    /// 이 클래스는 위도/경도/고도(WGS-84) ↔ X/Y/Z(ECEF) 변환을 수행합니다.
    /// 
    /// WGS-84 타원체 파라미터:
    ///   a  = 6,378,137.0 m       (장반경)
    ///   f  = 1/298.257223563     (편평률)
    /// </summary>
    internal static class CoordConverter
    {
        private const double A = 6378137.0;
        private const double F = 1.0 / 298.257223563;
        private const double B = A * (1.0 - F);
        private const double E2 = 2.0 * F - F * F;
        private const double EP2 = (A * A - B * B) / (B * B);

        private const double DEG2RAD = Math.PI / 180.0;
        private const double RAD2DEG = 180.0 / Math.PI;

        /// <summary>
        /// WGS-84 → ECEF 변환
        /// </summary>
        /// <param name="lat">위도 [degree]</param>
        /// <param name="lon">경도 [degree]</param>
        /// <param name="alt">고도 [meter]</param>
        public static void ToECEF(
            double lat, double lon, double alt,
            out double x, out double y, out double z)
        {
            double latRad = lat * DEG2RAD;
            double lonRad = lon * DEG2RAD;

            double sinLat = Math.Sin(latRad);
            double cosLat = Math.Cos(latRad);
            double sinLon = Math.Sin(lonRad);
            double cosLon = Math.Cos(lonRad);

            double N = A / Math.Sqrt(1.0 - E2 * sinLat * sinLat);

            x = (N + alt) * cosLat * cosLon;
            y = (N + alt) * cosLat * sinLon;
            z = (N * (1.0 - E2) + alt) * sinLat;
        }

        /// <summary>
        /// ECEF → WGS-84 변환 (Bowring 반복법)
        /// </summary>
        public static void ToWGS84(
            double x, double y, double z,
            out double lat, out double lon, out double alt)
        {
            lon = Math.Atan2(y, x) * RAD2DEG;

            double p = Math.Sqrt(x * x + y * y);
            double theta = Math.Atan2(z * A, p * B);

            double sinTheta = Math.Sin(theta);
            double cosTheta = Math.Cos(theta);

            double latRad = Math.Atan2(
                z + EP2 * B * sinTheta * sinTheta * sinTheta,
                p - E2 * A * cosTheta * cosTheta * cosTheta);

            lat = latRad * RAD2DEG;

            double sinLat = Math.Sin(latRad);
            double cosLat = Math.Cos(latRad);
            double N = A / Math.Sqrt(1.0 - E2 * sinLat * sinLat);

            if (Math.Abs(cosLat) > 1e-10)
                alt = p / cosLat - N;
            else
                alt = Math.Abs(z) / Math.Abs(sinLat) - N * (1.0 - E2);
        }
    }
}
