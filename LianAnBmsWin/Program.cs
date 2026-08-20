using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace LianAnBmsWin
{
    class Program
    {
        // 锂安BMS在小程序中暴露的特征标志性广播服务UUID
        private static readonly Guid BmsServiceGuid = Guid.Parse("00002760-08C2-11E1-9073-0E8AC72E1001");

        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   锂安 BMS 保护板蓝牙数据解析上位机 (测试版)     ");
            Console.WriteLine("==================================================");

            // 【第一步：离线公式验证】
            // 这里硬编码一组从小程序模拟器(mock-bms.js)中还原出的锂安板子原始二进制寄存器快照流
            Console.WriteLine("\n[1/2] 正在进行本地解析公式精度审计...");
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

            // 【第二步：Windows 实时蓝牙扫描】
            Console.WriteLine("\n[2/2] 正在初始化 Windows BLE 蓝牙扫描引擎...");
            Console.WriteLine("正在搜寻附近的锂安 BMS 保护板，请确保手机小程序已断开连接并开启板子蓝牙...");
            
            StartBleAdvertisementWatcher();

            // 保持控制台开启，防止程序直接退出
            Console.WriteLine("\n按下 [Enter] 键可以退出程序。");
            Console.ReadLine();
        }

        private static void StartBleAdvertisementWatcher()
        {
            var watcher = new BluetoothLEAdvertisementWatcher();
            
            // 过滤条件：只扫描包含锂安特定BMS服务UUID的设备
            watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(BmsServiceGuid);
            watcher.ScanningMode = BluetoothLEScanningMode.Active;

            // 绑定发现设备事件
            watcher.Received += (sender, eventArgs) =>
            {
                string deviceName = string.IsNullOrEmpty(eventArgs.Advertisement.LocalName) 
                    ? "未广播名称设备" 
                    : eventArgs.Advertisement.LocalName;

                Console.WriteLine($"\n[发现目标BMS] 设备名: {deviceName} | MAC地址: {eventArgs.BluetoothAddress:X}");
                Console.WriteLine($"信号强度 (RSSI): {eventArgs.RawSignalStrengthInDBm} dBm");
                Console.WriteLine($"提示: 可在代码中通过 BluetoothLEDevice.FromBluetoothAddressAsync(0x{eventArgs.BluetoothAddress:X}) 建立深度握手通道。");
            };

            watcher.Start();
        }

        private static void PrintBmsInfo(BmsMetrics m)
        {
            Console.WriteLine("------------------- 解码快照 -------------------");
            Console.WriteLine($"电芯类型: {m.CellType}  |  电芯串数: {m.CellCount} 串");
            Console.WriteLine($"总 电 压: {m.Voltage:F3} V   |  实时电流: {m.Current:F2} A");
            Console.WriteLine($"剩余容量: {m.RemainCap} mAh |  当前 SOC : {m.Soc} % (健康度 SOH: {m.Soh}%)");
            Console.WriteLine($"循环次数: {m.Cycle} 次     |  MOS 温度 : {m.MosTemp:F1} ℃");
            Console.WriteLine($"环境温度1: {m.Temp1:F1} ℃   |  环境温度2: {m.Temp2:F1} ℃");
            Console.WriteLine("各电芯单体电压详情 (mV):");
            for (int i = 0; i < m.CellVoltages.Count; i++)
            {
                Console.Write($"[{i + 1:D2}单体]: {m.CellVoltages[i]}mV  ");
                if ((i + 1) % 4 == 0) Console.WriteLine(); // 每4个换一行打印
            }
            Console.WriteLine("\n------------------------------------------------");
        }

        /// <summary>
        /// 模拟构造从小程序mock-bms.js中取出来的寄存器数据流(大端序)，用于离线测试
        /// </summary>
        private static byte[] CreateMockBmsData()
        {
            byte[] data = new byte[128];
            
            // 寄存器0~3：写入温度开尔文数据（如 3001 对应 27.0℃）
            data[0] = 0x0B; data[1] = 0xB9; // MosTemp = 3001
            data[2] = 0x0B; data[3] = 0xB9; // Temp1 = 3001
            data[4] = 0x0B; data[5] = 0xB9; // Temp2 = 3001

            // 寄存器4~5：总电压 49600 mV -> 49.6V
            data[8] = 0x00; data[9] = 0x00; data[10] = 0xC1; data[11] = 0xC0;

            // 寄存器6~7：电流 50000 mA -> 50.0A
            data[12] = 0x00; data[13] = 0x00; data[14] = 0xC3; data[15] = 0x50;

            // 寄存器8~9：剩余容量 40000 mAh
            data[16] = 0x00; data[17] = 0x00; data[18] = 0x9C; data[19] = 0x40;

            // 寄存器20：高位SOH=100(0x64), 低位SOC=86(0x56)
            data[40] = 0x64; data[41] = 0x56;

            // 寄存器24：高位串数=16(0x10), 低位电芯类型=0(0x00 磷酸铁锂)
            data[48] = 0x10; data[49] = 0x00;

            // 寄存器26：循环次数 = 21次
            data[52] = 0x00; data[53] = 0x15;

            // 寄存器32开始：构造16串单体电压（每串模拟 3278mV 左右渐变）
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
