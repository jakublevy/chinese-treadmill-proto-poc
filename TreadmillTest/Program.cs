using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace TreadmillTest
{
    class Program
    {
        private static BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher() {ScanningMode = BluetoothLEScanningMode.Active};
        private static BluetoothLEDevice runnerT;
        private static IReadOnlyList<GattCharacteristic> characteristics;
        private static GattCharacteristic charac;
        private static GattCharacteristic connectionHoldCharac;
        private static GattCharacteristic runningCharac;
        private static Timer timer;
        


        static void Main(string[] args)
        {
            watcher.Received += WatcherOnReceived;
            watcher.Start();
            while (true)
            {
                string line = Console.ReadLine();
                string[] cmd = line.Split(' ');
                switch (cmd[0])
                {
                    case "start":
                        Start();
                        break;
                    case "stop":
                        Stop();
                        break;
                    case "pause":
                        Pause();
                        break;
                    case "speed":
                        SetSpeed(decimal.Parse(cmd[1]));
                        break;
                    case "cmd":
                        SendCmd(int.Parse(cmd[1]), int.Parse(cmd[2]), int.Parse(cmd[3]));
                        break;
                }
            }
        }

        private static async void WatcherOnReceived(BluetoothLEAdvertisementWatcher w, BluetoothLEAdvertisementReceivedEventArgs btAdv)
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAdv.BluetoothAddress);
            if (device != null && device.Name == "RunnerT")
            {
                var gatt = await device.GetGattServicesAsync();
                if (gatt.Services.Count > 0)
                {

                    var characs = await gatt.Services.Single(s => s.Uuid == Guid.Parse("E54EAA50-371B-476C-99A3-74D267E3EDAE")).GetCharacteristicsAsync();
                    if (characs.Characteristics.Count > 0)
                    {
                        runnerT = device;
                        characteristics = characs.Characteristics;
                        charac = characteristics.Single(s => s.Uuid == Guid.Parse("E54EAA57-371B-476C-99A3-74D267E3EDAE"));
                        connectionHoldCharac = characteristics.Single(s => s.Uuid == Guid.Parse("E54EAA55-371B-476C-99A3-74D267E3EDAE"));
                        runningCharac = characteristics.Single(s => s.Uuid == Guid.Parse("e54eaa56-371b-476c-99a3-74d267e3edae"));

                    //    runningCharac.ValueChanged += RunningCharacOnValueChanged; //not UWP, so not working, it should fire event, when value changes
                        timer = new Timer() {AutoReset = true, Interval = 500};
                        timer.Elapsed += runningValueCheck;
                        timer.Enabled = true;

                        watcher.Stop();

                        SendDoNotDisconnect();

                        Console.WriteLine($"Found {device.Name}");

                    }
                }
            }
        }

        private static async void runningValueCheck(object sender, ElapsedEventArgs e)
        {
            ParseRunningInfo(await ReadRunning());
        }

        private static async void SendDoNotDisconnect()
        {
            DataWriter dw = new DataWriter();
            dw.WriteByte(0x88); //-210 signed
            await connectionHoldCharac.WriteValueAsync(dw.DetachBuffer(), GattWriteOption.WriteWithResponse);
        }

        private static void Start()
        {
            SendCmd(1,0,0);
        }
        private static void Stop()
        {
            SendCmd(2, 0, 0);
        }
        private static void Pause()
        {
            SendCmd(5, 0, 0);
        }
        private static void SetSpeed(decimal speed)
        {
            SendCmd(3, Convert.ToInt32(speed * 10), 0);
        }

        private static async void SendCmd(int a, int b, int c)
        {
            DataWriter dw = new DataWriter();
            dw.WriteBytes(Obfuscate3(a,b,c));
            await charac.WriteValueAsync(dw.DetachBuffer(), GattWriteOption.WriteWithResponse);
        }

        private static async Task<byte[]> ReadRunning()
        {
            return (await runningCharac.ReadValueAsync(BluetoothCacheMode.Uncached)).Value.ToArray();
        }

        private static void ParseRunningInfo(byte[] runningBytes)
        {
            Status state = (Status) Enum.ToObject(typeof(Status), runningBytes[0]);

            decimal currSpeed = DeobfuscatedByteValue(runningBytes[1]) / 10m;
            decimal targetSpeed = DeobfuscatedByteValue(runningBytes[10]) / 10m;
            
            decimal duration = 256 * DeobfuscatedByteValue(runningBytes[2]) +  DeobfuscatedByteValue(runningBytes[3]);
            decimal distance = 256 * DeobfuscatedByteValue(runningBytes[4]) + DeobfuscatedByteValue(runningBytes[5]);
            decimal energy  = 256 * DeobfuscatedByteValue(runningBytes[6]) + DeobfuscatedByteValue(runningBytes[7]);

            Console.Title = $"State: {state}, Speed: {currSpeed}, Duration: {duration}, Distance: {distance}, Energy: {energy}";
        }

        private static byte[] Obfuscate3(int var1, int var2, int var3)
        {
            byte[] var4 = new byte[] { (byte)(var1 & 255), (byte)(var2 & 255), (byte)(var3 & 255), 0, 0, 0, 0 };
            var4[5] = (byte)((var4[0] + var4[1] ^ var4[2]) + var4[3] + 37);
            byte var5 = var4[0];
            var4[6] = (byte)((var4[1] ^ var5) + var4[2] + var4[3] + 2);
            return var4;
        }

        private static int DeobfuscatedByteValue(byte b)
        {
            byte upper = (byte) ((b & 0xF0) >> 4);
            byte lower = (byte) (b & 0x0F);
            if (lower == 0x0 || lower == 0x3 || lower == 0x4 || lower == 0x1 || lower == 0x2 || lower == 0x7 || lower == 0x8 || lower == 0x5 || lower == 0x6 || lower == 0xB || lower == 0xC)
            {
                byte upperFix;
                if (upper == 0x0)
                    upperFix = 0xF;
                else
                    upperFix = (byte) (upper - 1);
                return 16 * (UpperOrder(upperFix)) + LowerOrder(lower);
            }
            return 16 * UpperOrder(upper) + LowerOrder(lower);
        }

        private static int LowerOrder(byte b)
        {
            switch (b)
            {
                case 0x9:
                    return 0;
                case 0xA:
                    return 1;
                case 0xF:
                    return 2;
                case 0x0:
                    return 3;
                case 0xD:
                    return 4;
                case 0xE:
                    return 5;
                case 0x3:
                    return 6;
                case 0x4:
                    return 7;
                case 0x1:
                    return 8;
                case 0x2:
                    return 9;
                case 0x7:
                    return 10;
                case 0x8:
                    return 11;
                case 0x5:
                    return 12;
                case 0x6:
                    return 13;
                case 0xB:
                    return 14;
                case 0xC:
                    return 15;
            }

            throw new Exception("Cannot happen.");
        }

        private static int UpperOrder(byte b)
        {
            int diff = (b - 0x4) % 0xF;
            if (b < 0x4)
                return 16 + diff;

            return diff;
        }
    }

    enum Status
    {
        Rest = 0x49
       ,Running = 0x4D
       ,PreparingToRun = 0x4F
       ,PreparingToPause = 0x51
       ,PreparingToStop = 0x53
       ,Paused = 0x8A
    }
}