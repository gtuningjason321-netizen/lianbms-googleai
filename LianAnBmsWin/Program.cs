using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace LianAnBmsWin
{
    class DiscoveredBms
    {
        public ulong MacAddress { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Rssi { get; set; }
    }

    class Program
    {
        private static readonly Guid BmsServiceGuid = Guid.Parse("00002760-08C2-11E1-9073-0E8AC72E1001");
        private static readonly List<DiscoveredBms> BmsList = new List<DiscoveredBms>();
        private static BluetoothLEAdvertisementWatcher? _watcher;
        // 修复此处：显式指定使用 System.Timers.Timer，消除重名冲突
        private static System.Timers.Timer? _pulseTimer;
        private static bool _isSelecting = false;

        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   锂安 BMS 上位机：无线电脉冲强驱扫描中心        ");
            Console.WriteLine("==================================================");

            // 1. 本地算法公式审计
            byte[] mockBmsData = CreateMockBmsData();
            try { BmsParser.DecodeSnapshot(mockBmsData); } catch { }

            Console.WriteLine("\n[步骤 1/3] 正在全功率搜寻锂安保护板（持续 10 秒）...");
            Console.WriteLine("核心提示：请确保手机蓝牙已关闭！请保持电脑的“蓝牙设置”页面处于打开状态！");
            
            StartScan();

            // 倒计时 10 秒，让无线电信号充分收集
            for (int i = 10; i > 0; i--)
            {
                lock (BmsList)
                {
                    Console.Write($"\r距离开始选择还剩 {i} 秒... 当前已捕获到：{BmsList.Count} 个设备 ");
                }
                await Task.Delay(1000);
            }

            // 2. 停止扫描与脉冲激活定时器
            _isSelecting = true;
            StopScan();

            Console.WriteLine("\n\n==================================================");
            Console.WriteLine("[步骤 2/3] 扫描结束！请根据下方列表手动选择连接：");
            Console.WriteLine("==================================================");

            lock (BmsList)
            {
                if (BmsList.Count == 0)
                {
                    Console.WriteLine("❌ 抱歉，未搜寻到任何信号。");
                    Console.WriteLine("请确认：1.保护板灯在闪烁(未被占用) 2.电脑开启了系统设置里的“位置服务(定位)”");
                    Console.WriteLine("\n按下任意键退出程序...");
                    Console.ReadKey();
                    return;
                }

                for (int i = 0; i < BmsList.Count; i++)
                {
                    Console.WriteLine($" [{i + 1}] 设备名称: {BmsList[i].Name.PadRight(20)} | MAC地址: 0x{BmsList[i].MacAddress:X} | 信号: {BmsList[i].Rssi} dBm");
                }
            }
            Console.WriteLine(" 放弃并退出程序");
            Console.WriteLine("==================================================");

            // 3. 手动选择交互
            int selectedIndex = -1;
            int totalCount = 0;
            lock (BmsList) { totalCount = BmsList.Count; }

            while (selectedIndex < 0 || selectedIndex > totalCount)
            {
                Console.Write($"请输入对应设备的序号 (0-{totalCount}) 并按回车: ");
                string? input = Console.ReadLine();
                if (int.TryParse(input, out int res))
                {
                    selectedIndex = res;
                }
                else
                {
                    Console.WriteLine("⚠️ 输入无效，请输入纯数字！");
                }
            }

            if (selectedIndex == 0)
            {
                Console.WriteLine("已取消连接，程序退出。");
                return;
            }

            DiscoveredBms targetBms;
            lock (BmsList) { targetBms = BmsList[selectedIndex - 1]; }

            Console.WriteLine($"\n[步骤 3/3] 正在与目标 [序号 {selectedIndex}: {targetBms.Name}] 建立底层长连接通道...");
            
            // 4. 锁定物理 MAC 长连接
            await ConnectToBmsAsync(targetBms.MacAddress);

            Console.WriteLine("\n按下 [Enter] 键退出程序。");
            Console.ReadLine();
        }

        private static void StartScan()
        {
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            _watcher.Received += (sender, eventArgs) =>
            {
                if (_isSelecting) return;

                string localName = eventArgs.Advertisement.LocalName ?? string.Empty;
                
                bool nameMatched = localName.ToUpper().Contains("LA") || 
                                   localName.ToUpper().Contains("BMS") || 
                                   localName.ToUpper().Contains("BATTERY") ||
                                   localName.ToUpper().Contains("LIAN");

                bool uuidMatched = false;
                foreach (var uuid in eventArgs.Advertisement.ServiceUuids)
                {
                    if (uuid == BmsServiceGuid || uuid.ToString().Contains("2760"))
                    {
                        uuidMatched = true;
                        break;
                    }
                }

                if (nameMatched || uuidMatched)
                {
                    lock (BmsList)
                    {
                        if (!BmsList.Exists(d => d.MacAddress == eventArgs.BluetoothAddress))
                        {
                            BmsList.Add(new DiscoveredBms
                            {
                                MacAddress = eventArgs.BluetoothAddress,
                                Name = string.IsNullOrEmpty(localName) ? "锂安电池保护板" : localName,
                                Rssi = eventArgs.RawSignalStrengthInDBm
                            });
                        }
                    }
                }
            };

            // 修复此处：显式指定使用 System.Timers.Timer
            _pulseTimer = new System.Timers.Timer(1200);
            _pulseTimer.Elapsed += (s, e) =>
            {
                if (_isSelecting) return;
                try
                {
                    _watcher?.Stop();
                    _watcher?.Start();
                }
                catch { }
            };

            _watcher.Start();
            _pulseTimer.Start();
        }

        private static void StopScan()
        {
            try
            {
                _pulseTimer?.Stop();
                _pulseTimer?.Dispose();
                _watcher?.Stop();
            }
            catch { }
        }

        private static async Task ConnectToBmsAsync(ulong bluetoothAddress)
        {
            try
            {
                BluetoothLEDevice bluetoothLeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                
                if (bluetoothLeDevice == null)
                {
                    Console.WriteLine("❌ 连接失败：设备无响应，可能超出了距离。");
                    return;
                }

                var gattStatus = await bluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                
                if (gattStatus.Status == GattCommunicationStatus.Success)
                {
                    Console.WriteLine($"\n🎉 【完美握手】已成功与物理 MAC [0x{bluetoothAddress:X}] 建立物理长连接！");
                    Console.WriteLine("连接状态: " + bluetoothLeDevice.ConnectionStatus);
                    Console.WriteLine("提示：后续可在此处通过对特征值进行 Notify 注册，来实现每秒数据自动刷新。");
                }
                else
                {
                    Console.WriteLine($"❌ 连接被板子拒绝。系统返回状态码: {gattStatus.Status}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 握手期间发生严重系统异常: {ex.Message}");
            }
        }

        private static byte[] CreateMockBmsData()
        {
            byte[] data = new byte[128];
            data[0] = 0x0B; data[1] = 0xB9; 
            data[8] = 0x00; data[9] = 0x00; data[10] = 0xC1; data[11] = 0xC0; 
            data[40] = 0x64; data[41] = 0x56; 
            data[48] = 0x10; data[49] = 0x00; 
            return data;
        }
    }
}
