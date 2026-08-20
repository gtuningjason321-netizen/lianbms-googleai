using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace LianAnBmsWin
{
    // 定义一个内部结构，用来暂存扫描到的保护板
    class DiscoveredBms
    {
        public ulong MacAddress { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Rssi { get; set; }
    }

    class Program
    {
        private static readonly Guid BmsServiceGuid = Guid.Parse("00002760-08C2-11E1-9073-0E8AC72E1001");
        
        // 存储搜寻到的非重复设备列表
        private static readonly List<DiscoveredBms> BmsList = new List<DiscoveredBms>();
        private static BluetoothLEAdvertisementWatcher? _watcher;
        private static bool _isSelecting = false;

        static async Task Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   锂安 BMS 上位机：手动选择设备连接控制中心      ");
            Console.WriteLine("==================================================");

            // 1. 本地算法公式审计
            byte[] mockBmsData = CreateMockBmsData();
            try { BmsParser.DecodeSnapshot(mockBmsData); } catch { }

            Console.WriteLine("\n[步骤 1/3] 正在搜寻附近的锂安保护板（持续 8 秒）...");
            Console.WriteLine("提示：请确保手机小程序已完全关闭！");
            
            StartScan();

            // 倒计时 8 秒，让无线电信号充分收集
            for (int i = 8; i > 0; i--)
            {
                Console.Write($"\r距离开始选择还剩 {i} 秒... 已发现 {BmsList.Count} 个设备");
                await Task.Delay(1000);
            }

            // 2. 停止扫描，进入手动选择阶段
            _isSelecting = true;
            _watcher?.Stop();
            Console.WriteLine("\n\n==================================================");
            Console.WriteLine("[步骤 2/3] 扫描结束！请根据下方列表手动选择你要连接的设备：");
            Console.WriteLine("==================================================");

            if (BmsList.Count == 0)
            {
                Console.WriteLine("❌ 未发现任何目标保护板，请检查电池电源及电脑蓝牙、定位是否开启。");
                Console.WriteLine("按下任意键退出程序...");
                Console.ReadKey();
                return;
            }

            // 打印手动选择菜单
            for (int i = 0; i < BmsList.Count; i++)
            {
                Console.WriteLine($" [{i + 1}] 设备名称: {BmsList[i].Name.PadRight(20)} | MAC地址: 0x{BmsList[i].MacAddress:X} | 信号: {BmsList[i].Rssi} dBm");
            }
            Console.WriteLine(" [0] 放弃并退出程序");
            Console.WriteLine("==================================================");

            // 3. 拦截键盘输入，进行选择过滤
            int selectedIndex = -1;
            while (selectedIndex < 0 || selectedIndex > BmsList.Count)
            {
                Console.Write($"请输入对应设备的序号 (0-{BmsList.Count}) 并按回车: ");
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

            // 4. 锁定目标设备，准备握手
            var targetBms = BmsList[selectedIndex - 1];
            Console.WriteLine($"\n[步骤 3/3] 正在尝试与目标 [序号 {selectedIndex}: {targetBms.Name}] 建立底层长连接通道...");
            
            // 调用长连接逻辑
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
                if (_isSelecting) return; // 如果已经进入选择阶段，不再接纳新广播

                string localName = eventArgs.Advertisement.LocalName ?? string.Empty;
                
                // 模糊匹配逻辑
                bool nameMatched = localName.ToUpper().Contains("LA") || 
                                   localName.ToUpper().Contains("BMS") || 
                                   localName.ToUpper().Contains("BATTERY");

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
                        // 检查是否已经是发现过的重复设备，如果不是则加入缓冲区
                        if (!BmsList.Exists(d => d.MacAddress == eventArgs.BluetoothAddress))
                        {
                            BmsList.Add(new DiscoveredBms
                            {
                                MacAddress = eventArgs.BluetoothAddress,
                                Name = string.IsNullOrEmpty(localName) ? "锂安匿名电池板" : localName,
                                Rssi = eventArgs.RawSignalStrengthInDBm
                            });
                        }
                    }
                }
            };

            _watcher.Start();
        }

        /// <summary>
        /// 基于手动选择的MAC地址，与保护板建立深度长连接（基础通道）
        /// </summary>
        private static async Task ConnectToBmsAsync(ulong bluetoothAddress)
        {
            try
            {
                // 通过 Windows 原生 API 直接使用无线电收发器抓取指定 MAC
                BluetoothLEDevice bluetoothLeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                
                if (bluetoothLeDevice == null)
                {
                    Console.WriteLine("❌ 连接失败：Windows 蓝牙无响应，可能设备已超出无线电范围。");
                    return;
                }

                // 强制触发底层物理连接
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
                    Console.WriteLine("请尝试：给保护板断电重启（拔掉排线重新插入），复位下位机蓝牙状态。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 握手期间发生严重系统异常: {ex.Message}");
            }
        }

        private static byte[] CreateMockBmsData()
        {
            byte[] data = new byte;
            data = 0x0B; data = 0xB9; 
            data = 0x00; data = 0x00; data = 0xC1; data = 0xC0; 
            data = 0x64; data = 0x56; 
            data = 0x10; data = 0x00; 
            return data;
        }
    }
}
