using System;

namespace SMBVB_GNSS
{
    /// <summary>
    /// SMBV100B HIL UDP 패킷 생성기
    /// 
    /// 매뉴얼 p.255 Table 16-2 패킷 구조:
    /// 
    /// Offset  Type     Size  Parameter
    /// ──────────────────────────────────────
    ///   0     integer   4    reserve0 (0)        ← 16바이트 예약
    ///   4     integer   4    reserve1 (0)
    ///   8     integer   4    reserve2 (0)
    ///  12     integer   4    reserve3 (0)
    ///  16     double    8    Elapsed Time [s]     ← 데이터 시작
    ///  24     double    8    Position X (ECEF) [m]
    ///  32     double    8    Position Y (ECEF) [m]
    ///  40     double    8    Position Z (ECEF) [m]
    ///  48~64  double   24    Velocity X/Y/Z [m/s]
    ///  72~112 double   48    Accel + Jerk (0)
    /// 120~136 double   24    Yaw/Pitch/Roll [rad]
    /// 144~215 double   72    Attitude rates/accels/jerks (0)
    /// ──────────────────────────────────────
    /// Total: 16 + (25 × 8) = 216 bytes
    /// </summary>
    internal static class HilPacket
    {
        public const int PACKET_SIZE = 216;
        private const int RESERVED_SIZE = 16;

        public static byte[] Build(
            double posX, double posY, double posZ,
            double velX, double velY, double velZ,
            double yaw, double pitch, double roll,
            double elapsedTime)
        {
            var buf = new byte[PACKET_SIZE];

            // 0~15: 예약 영역 (integer × 4, 이미 0)
            int offset = RESERVED_SIZE;

            WriteDoubleBE(buf, offset, elapsedTime); offset += 8;
            WriteDoubleBE(buf, offset, posX); offset += 8;
            WriteDoubleBE(buf, offset, posY); offset += 8;
            WriteDoubleBE(buf, offset, posZ); offset += 8;
            WriteDoubleBE(buf, offset, velX); offset += 8;
            WriteDoubleBE(buf, offset, velY); offset += 8;
            WriteDoubleBE(buf, offset, velZ); offset += 8;

            // Acceleration + Jerk → 0 (48 bytes)
            offset += 48;

            WriteDoubleBE(buf, offset, yaw); offset += 8;
            WriteDoubleBE(buf, offset, pitch); offset += 8;
            WriteDoubleBE(buf, offset, roll); offset += 8;

            return buf;
        }

        private static void WriteDoubleBE(byte[] buf, int offset, double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buf, offset, 8);
        }
    }
}
