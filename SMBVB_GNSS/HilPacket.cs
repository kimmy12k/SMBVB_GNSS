using System;

namespace SMBVB_GNSS
{
    /// <summary>
    /// SMBV100B HIL UDP 패킷 생성기
    /// 
    /// 매뉴얼 p.247: Mode A (ECEF) — UDP 전송 가능한 유일한 모드
    ///               Mode B (NED)  — SCPI 전용, UDP 불가
    /// 
    /// 패킷 구조: 216 bytes, IEEE 754 double, Big Endian
    /// 
    /// Offset  Size  Description
    /// ─────────────────────────────────
    ///   0      8    Elapsed Time [s]
    ///   8      8    Position X (ECEF) [m]
    ///  16      8    Position Y (ECEF) [m]
    ///  24      8    Position Z (ECEF) [m]
    ///  32      8    Velocity X [m/s]
    ///  40      8    Velocity Y [m/s]
    ///  48      8    Velocity Z [m/s]
    ///  56      8    Acceleration X [m/s²]
    ///  64      8    Acceleration Y [m/s²]
    ///  72      8    Acceleration Z [m/s²]
    ///  80      8    Jerk X [m/s³]
    ///  88      8    Jerk Y [m/s³]
    ///  96      8    Jerk Z [m/s³]
    /// 104      8    Yaw [rad]
    /// 112      8    Pitch [rad]
    /// 120      8    Roll [rad]
    /// 128      8    Yaw Rate [rad/s]
    /// 136      8    Pitch Rate [rad/s]
    /// 144      8    Roll Rate [rad/s]
    /// 152      8    Yaw Accel [rad/s²]
    /// 160      8    Pitch Accel [rad/s²]
    /// 168      8    Roll Accel [rad/s²]
    /// 176      8    Antenna Offset X [m]
    /// 184      8    Antenna Offset Y [m]
    /// 192      8    Antenna Offset Z [m]
    /// 200      8    Reserved (0)
    /// 208      8    Reserved (0)
    /// ─────────────────────────────────
    /// Total: 27 × 8 = 216 bytes
    /// </summary>
    internal static class HilPacket
    {
        public const int PACKET_SIZE = 216;

        /// <summary>
        /// 216바이트 HIL UDP 패킷 생성
        /// 
        /// 주의: SMBV100B는 Big Endian을 기대합니다.
        ///       x86/x64 Windows는 Little Endian이므로 바이트 순서를 뒤집습니다.
        /// </summary>
        public static byte[] Build(
            double posX, double posY, double posZ,
            double velX, double velY, double velZ,
            double yaw, double pitch, double roll,
            double elapsedTime)
        {
            var buf = new byte[PACKET_SIZE];

            int offset = 0;

            WriteDoubleBE(buf, offset, elapsedTime); offset += 8;

            WriteDoubleBE(buf, offset, posX); offset += 8;
            WriteDoubleBE(buf, offset, posY); offset += 8;
            WriteDoubleBE(buf, offset, posZ); offset += 8;

            WriteDoubleBE(buf, offset, velX); offset += 8;
            WriteDoubleBE(buf, offset, velY); offset += 8;
            WriteDoubleBE(buf, offset, velZ); offset += 8;

            // Acceleration, Jerk → 0 (48 bytes, 이미 0으로 초기화됨)
            offset += 48;

            WriteDoubleBE(buf, offset, yaw); offset += 8;
            WriteDoubleBE(buf, offset, pitch); offset += 8;
            WriteDoubleBE(buf, offset, roll); offset += 8;

            // 나머지 → 0 (Attitude rates, Antenna offsets, Reserved)

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
