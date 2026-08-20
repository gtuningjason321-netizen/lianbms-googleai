using System;
using System.Collections.Generic;

namespace LianAnBmsWin
{
    public class BmsMetrics
    {
        public double Voltage { get; set; }        // 总电压 (V)
        public double Current { get; set; }        // 电流 (A)
        public uint RemainCap { get; set; }         // 剩余容量 (mAh)
        public int Soc { get; set; }               // 剩余百分比 (0-100)
        public int Soh { get; set; }               // 健康度 (0-100)
        public int CellCount { get; set; }         // 电芯串数
        public string CellType { get; set; }       // 电芯类型
        public ushort Cycle { get; set; }          // 循环次数
        public double MosTemp { get; set; }        // MOS温度 (℃)
        public double Temp1 { get; set; }          // 温度1 (℃)
        public double Temp2 { get; set; }          // 温度2 (℃)
        public List<int> CellVoltages { get; set; } = new List<int>(); // 单体电压 (mV)
    }

    public class BmsParser
    {
        // 锂安特有的开尔文温度转摄氏度公式：(K - 2731) / 10
        private static double K2C(ushort k)
        {
            return (k - 2731) / 10.0;
        }

        // 电芯类型映射
        private static readonly Dictionary<int, string> CellTypes = new Dictionary<int, string>
        {
            { 0, "磷酸铁锂" },
            { 1, "三元锂" },
            { 2, "钛酸锂" },
            { 3, "钠离子" }
        };

        /// <summary>
        /// 解析从0x03功能码读回的快照连续字节流（对应小程序核心 decode 逻辑）
        /// </summary>
        public static BmsMetrics DecodeSnapshot(byte[] data)
        {
            if (data == null || data.Length < 64)
                throw new ArgumentException("BMS基础数据长度不足 64 字节");

            BmsMetrics metrics = new BmsMetrics();

            // 1. 解析温度 (寄存器 0~3)
            metrics.MosTemp = K2C(ReadU16(data, 0));
            metrics.Temp1 = K2C(ReadU16(data, 2));
            metrics.Temp2 = K2C(ReadU16(data, 4));

            // 2. 解析电压与电流 (寄存器 4~7)
            metrics.Voltage = ReadU32(data, 8) / 1000.0; // 4-5寄存器是总电压
            metrics.Current = ReadI32(data, 12) / 1000.0; // 6-7寄存器是电流

            // 3. 解析容量 (寄存器 8-9)
            metrics.RemainCap = ReadU32(data, 16);

            // 4. 解析 SOC / SOH (寄存器 20)
            ushort socWord = ReadU16(data, 40);
            metrics.Soc = socWord & 0xFF;
            metrics.Soh = (socWord >> 8) & 0xFF;

            // 5. 解析电芯信息 (寄存器 24)
            ushort cellWord = ReadU16(data, 48);
            metrics.CellCount = (cellWord >> 8) & 0xFF;
            int typeCode = cellWord & 0xFF;
            metrics.CellType = CellTypes.ContainsKey(typeCode) ? CellTypes[typeCode] : "未知";

            // 6. 解析循环次数 (寄存器 26)
            metrics.Cycle = ReadU16(data, 52);

            // 7. 解析单体电压 (从第32个寄存器开始，即偏移量 64 字节开始)
            if (data.Length >= 64 + (metrics.CellCount * 2))
            {
                for (int i = 0; i < metrics.CellCount; i++)
                {
                    ushort cellMv = ReadU16(data, 64 + (i * 2));
                    metrics.CellVoltages.Add(cellMv);
                }
            }

            return metrics;
        }

        #region 基础大端字节序转换辅助函数
        private static ushort ReadU16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static int ReadI32(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }
        #endregion
    }
}
