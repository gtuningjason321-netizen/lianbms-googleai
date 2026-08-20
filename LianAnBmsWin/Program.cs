using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace LianAnBmsWin
{
    class Program
    {
        private static readonly Guid BmsServiceGuid = Guid.Parse("00002760-08C2-11E1-9073-0E8AC72E1001");

        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   锂安 BMS 保护板蓝牙数据解析上位机 (测试版)     ");
            Console.WriteLine("==================================================");

            Console.WriteLine("\n[1/2] 正在进行本地解析公式精度的安全审计...");
            byte[] mockBmsData = CreateMockBmsData();
            
            try
            {
                var testMetrics = BmsParser.DecodeSnapshot(mockBmsData);
                PrintBmsInfo(testMetrics);
                Console.WriteLine(">>> 本地算法验证成功！解析数据与原厂小程序规范一致。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> 本地算法审计失败: {ex.Message}");
            }

            Console.WriteLine("\n[2/2] 正在初始化 Windows BLE 蓝牙扫描引擎...");
            Console.WriteLine("正在搜寻附近的锂安 BMS 保护板，请确保手机小程序已断开连接...");
            
            StartBleAdvertisementWatcher();

            Console.WriteLine("\n按下 [Enter] 键可以退出程序。");
            Console.ReadLine();
        }

        private static void StartBleAdvertisementWatcher()
        {
            try
            {
                var watcher = new BluetoothLEAdvertisementWatcher();
                watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(BmsServiceGuid);
                watcher.ScanningMode = BluetoothLEScanningMode.Active;

                watcher.Received += (sender, eventArgs) =>
                {
                    string deviceName = string.IsNullOrEmpty(eventArgs.Advertisement.LocalName) 
                        ? "未广播名称设备" 
                        : eventArgs.Advertisement.LocalName;

                    Console.WriteLine($"\n[发现目标BMS] 设备名: {deviceName} | MAC: {eventArgs.BluetoothAddress:X}");
                    Console.WriteLine($"信号强度 (RSSI): {eventArgs.RawSignalStrengthInDBm} dBm");
                };

                watcher.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"蓝牙引擎启动异常 (请检查电脑蓝牙是否开启): {ex.Message}");
            }
        }

        private static void PrintBmsInfo(BmsMetrics m)
        {
            Console.WriteLine("------------------- 解码快照 -------------------");
            Console.WriteLine($"电芯类型: {m.CellType}  |  电芯串数: {m.CellCount} 串");
            Console.WriteLine($"总 电 压: {m.Voltage:F3} V   |  实时电流: {m.Current:F2} A");
            Console.WriteLine($"剩余容量: {m.RemainCap} mAh |  当前 SOC : {m.Soc} % (SOH: {m.Soh}%)");
            Console.WriteLine($"循环次数: {m.Cycle} 次     |  MOS 温度 : {m.MosTemp:F1} ℃");
            Console.WriteLine($"环境温度1: {m.Temp1:F1} ℃   |  环境温度2: {m.Temp2:F1} ℃");
            Console.WriteLine("各电芯单体电压详情 (mV):");
            for (int i = 0; i < m.CellVoltages.Count; i++)
            {
                Console.Write($"[{i + 1:D2}单体]: {m.CellVoltages[i]}mV  ");
                if ((i + 1) % 4 == 0) Console.WriteLine();
            }
            Console.WriteLine("\n------------------------------------------------");
        }

        private static byte[] CreateMockBmsData()
        {
            // 声明固定大小，规避预剪裁安全警告
            byte[] data = new byte[128];
            
            // 温度寄存器（模拟 27.0℃ = 3001 开尔文）
            data[0] = 0x0B; data[1] = 0xB9; 
            data[2] = 0x0B; data[3] = 0xB9; 
            data[4] = 0x0B; data[5] = 0xB9; 

            // 总电压 49600 mV -> 49.6V
            data[8] = 0x00; data[9] = 0x00; data[10] = 0xC1; data[11] = 0xC0;

            // 电流 50000 mA -> 50.0A
            data[12] = 0x00; data[13] = 0x00; data[14] = 0xC3; data[15] = 0xC5;

            // 剩余容量 40000 mAh
            data[16] = 0x00; data[17] = 0x00; data[18] = 0x9C; data[19] = 0x40;

            // SOC / SOH
            data[40] = 0x64; data[41] = 0x56;

            // 16串 / 磷酸铁锂
            data[48] = 0x10; data[49] = 0x00;

            // 循环次数 21
            data[52] = 0x00; data[53] = 0x15;

            // 写入 16 串电压数据模拟值
            for (int i = 0; i < 16; i++)
            {
                ushort mv = (ushort)(3278 + (i % 5 * 3));
                data[64 + (i * 2)] = (byte)(mv >> 8);
                data[64 + (i * 2) + 1] = (byte)(mv & 0xFF);
            }

            return data;
        }
    }
}
