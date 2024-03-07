﻿using GHelper.Helpers;
using GHelper.Mode;
using GHelper.USB;
using System.Management;

namespace GHelper.Display
{
    public enum SplendidGamut : int
    {
        Native = 50,
        sRGB = 51,
        DCIP3 = 53,
        DisplayP3 = 54
    }

    public enum SplendidCommand: int
    {
        Init = 10,
        DimmingAsus = 9,
        DimmingVisual = 19,
        GamutMode = 200,

        Default = 11,
        Racing = 21,
        Scenery = 22,
        RTS = 23,
        FPS = 24,
        Cinema = 25,
        Vivid = 13,
        Eyecare = 17,
    }

    public static class ScreenControl
    {

        public const int MAX_REFRESH = 1000;

        public static DisplayGammaRamp? gammaRamp;

        private static int _brightness = 100;
        private static bool _init = true;
        private static string? _splendidPath = null;

        private static System.Timers.Timer brightnessTimer = new System.Timers.Timer(100);

 
        static ScreenControl () {
            brightnessTimer.Elapsed += BrightnessTimerTimer_Elapsed;
        }


        public static Dictionary<SplendidGamut, string> GetGamutModes ()
        {
            Dictionary<SplendidGamut, string> _modes = new Dictionary<SplendidGamut, string>();

            DirectoryInfo d = new DirectoryInfo("C:\\ProgramData\\ASUS\\GameVisual"); 
            FileInfo[] icms = d.GetFiles("*.icm");

            if (icms.Length == 0) return _modes;

            _modes.Add(SplendidGamut.Native, "Gamut - Native");
            foreach (FileInfo icm in icms)
            {
                if (icm.Name.Contains("sRGB")) _modes.Add(SplendidGamut.sRGB, "Gamut - sRGB");
                if (icm.Name.Contains("DCIP3")) _modes.Add(SplendidGamut.DCIP3, "Gamut - DCIP3");
                if (icm.Name.Contains("DisplayP3")) _modes.Add(SplendidGamut.DisplayP3, "Gamut - DisplayP3");
            }
            return _modes;
        }

        public static Dictionary<SplendidCommand, string> GetVisualModes()
        {
            return new Dictionary<SplendidCommand, string>
            {
                { SplendidCommand.Default, "Default"},
                { SplendidCommand.Racing, "Racing"},
                { SplendidCommand.Scenery, "Scenery"},
                { SplendidCommand.RTS, "RTS/RPG"},
                { SplendidCommand.FPS, "FPS"},
                { SplendidCommand.Cinema, "Cinema"},
                { SplendidCommand.Vivid, "Vivid" },
                { SplendidCommand.Eyecare, "Eyecare"}
            };
        }

        public static void SetGamut(int mode = 50)
        {
            if (RunSplendid(SplendidCommand.GamutMode, 0, mode)) return;
            
            if (_init)
            {
                _init = false;
                RunSplendid(SplendidCommand.Init);
                RunSplendid(SplendidCommand.GamutMode, 0, mode);
            }
        }

        public static void SetVisual(SplendidCommand mode = SplendidCommand.Default, int whiteBalance = 50)
        {
            int balance = mode == SplendidCommand.Eyecare ? 2 : whiteBalance;
            RunSplendid(mode, 0, balance);
            
            if (_init)
            {
                _init = false;
                RunSplendid(SplendidCommand.Init);
                RunSplendid(mode, 0, balance);
            }
        }

        private static string GetSplendidPath()
        {
            if (_splendidPath == null)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher(@"Select * from Win32_SystemDriver WHERE Name='ATKWMIACPIIO'"))
                    {
                        foreach (var driver in searcher.Get())
                        {
                            string path = driver["PathName"].ToString();
                            _splendidPath = Path.GetDirectoryName(path) + "\\AsusSplendid.exe";
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }
            }

            return _splendidPath;
        }

        private static bool RunSplendid(SplendidCommand command, int? param1 = null, int? param2 = null)
        {
            var splendid = GetSplendidPath(); 
            bool isGameVisual = Directory.Exists("C:\\ProgramData\\ASUS\\GameVisual");
            bool isSplenddid = File.Exists(splendid);

            if (isSplenddid)
            {
                if (command == SplendidCommand.DimmingVisual && !isGameVisual) command = SplendidCommand.DimmingAsus;
                var result = ProcessHelper.RunCMD(splendid, (int)command + " " + param1 + " " + param2);
                if (result.Contains("file not exist") || (result.Length == 0 && isGameVisual)) return false;
            }

            return true;
        }

        private static void BrightnessTimerTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            brightnessTimer.Stop();


            if (RunSplendid(SplendidCommand.DimmingVisual, 0, (int)(40 + _brightness * 0.6))) return;

            if (_init)
            {
                _init = false;
                RunSplendid(SplendidCommand.Init);
                RunSplendid(SplendidCommand.Init, 4);
                if (RunSplendid(SplendidCommand.DimmingVisual, 0, (int)(40 + _brightness * 0.6))) return;
            }

            // GammaRamp Fallback
            SetGamma(_brightness);
        }

        public static void AutoScreen(bool force = false)
        {
            if (force || AppConfig.Is("screen_auto"))
            {
                if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online)
                    SetScreen(MAX_REFRESH, 1);
                else
                    SetScreen(60, 0);
            }
            else
            {
                SetScreen(overdrive: AppConfig.Get("overdrive"));
            }
        }

        public static int SetBrightness(int brightness = -1, int delta = 0)
        {
            if (!AppConfig.IsOLED()) return -1;

            if (brightness < 0) brightness = AppConfig.Get("brightness", 100);

            _brightness = Math.Max(0, Math.Min(100, brightness + delta));
            AppConfig.Set("brightness", _brightness);

            brightnessTimer.Start();

            Program.settingsForm.VisualiseBrightness();

            return _brightness;
        }



        public static void SetGamma(int brightness = 100)
        {
            var bright = Math.Round((float)brightness / 200 + 0.5, 2);

            var screenName = ScreenNative.FindLaptopScreen();
            if (screenName is null) return;

            try
            {
                var handle = ScreenNative.CreateDC(screenName, screenName, null, IntPtr.Zero);
                if (gammaRamp is null)
                {
                    var gammaDump = new GammaRamp();
                    if (ScreenNative.GetDeviceGammaRamp(handle, ref gammaDump))
                    {
                        gammaRamp = new DisplayGammaRamp(gammaDump);
                        //Logger.WriteLine("Gamma R: " + string.Join("-", gammaRamp.Red));
                        //Logger.WriteLine("Gamma G: " + string.Join("-", gammaRamp.Green));
                        //Logger.WriteLine("Gamma B: " + string.Join("-", gammaRamp.Blue));
                    }
                }

                if (gammaRamp is null || !gammaRamp.IsOriginal())
                {
                    Logger.WriteLine("Not default Gamma");
                    gammaRamp = new DisplayGammaRamp();
                }

                var ramp = gammaRamp.AsBrightnessRamp(bright);
                bool result = ScreenNative.SetDeviceGammaRamp(handle, ref ramp);

                Logger.WriteLine("Gamma " + bright.ToString() + ": " + result);

            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
            }

            //ScreenBrightness.Set(60 + (int)(40 * bright));
        }

        public static void SetScreen(int frequency = -1, int overdrive = -1, int miniled = -1)
        {
            var laptopScreen = ScreenNative.FindLaptopScreen(true);

            if (laptopScreen is null) return;

            if (ScreenNative.GetRefreshRate(laptopScreen) < 0) return;

            if (frequency >= MAX_REFRESH)
            {
                frequency = ScreenNative.GetMaxRefreshRate(laptopScreen);
            }

            if (frequency > 0)
            {
                ScreenNative.SetRefreshRate(laptopScreen, frequency);
            }

            if (overdrive >= 0)
            {
                if (AppConfig.IsNoOverdrive()) overdrive = 0;
                if (!AppConfig.IsOLED()) Program.acpi.DeviceSet(AsusACPI.ScreenOverdrive, overdrive, "ScreenOverdrive");

            }

            if (miniled >= 0)
            {
                if (Program.acpi.DeviceGet(AsusACPI.ScreenMiniled1) >= 0)
                    Program.acpi.DeviceSet(AsusACPI.ScreenMiniled1, miniled, "Miniled1");
                else
                    Program.acpi.DeviceSet(AsusACPI.ScreenMiniled2, miniled, "Miniled2");
            }

            InitScreen();
        }


        public static int ToogleMiniled()
        {
            int miniled1 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled1);
            int miniled2 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled2);

            Logger.WriteLine($"MiniledToggle: {miniled1} {miniled2}");

            int miniled;

            if (miniled1 >= 0)
            {
                miniled = (miniled1 == 1) ? 0 : 1;
            }
            else
            {
                switch (miniled2)
                {
                    case 1: miniled = 2; break;
                    case 2: miniled = 0; break;
                    default: miniled = 1; break;
                }
            }

            AppConfig.Set("miniled", miniled);
            SetScreen(-1, -1, miniled);
            return miniled;
        }

        public static void InitScreen()
        {
            var laptopScreen = ScreenNative.FindLaptopScreen();

            int frequency = ScreenNative.GetRefreshRate(laptopScreen);
            int maxFrequency = ScreenNative.GetMaxRefreshRate(laptopScreen);

            bool screenAuto = AppConfig.Is("screen_auto");
            bool overdriveSetting = !AppConfig.IsNoOverdrive();

            int overdrive = AppConfig.IsNoOverdrive() ? 0 : Program.acpi.DeviceGet(AsusACPI.ScreenOverdrive);

            int miniled1 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled1);
            int miniled2 = Program.acpi.DeviceGet(AsusACPI.ScreenMiniled2);

            int miniled = (miniled1 >= 0) ? miniled1 : miniled2;
            bool hdr = false;

            if (miniled >= 0)
            {
                Logger.WriteLine($"Miniled: {miniled1} {miniled2}");
                AppConfig.Set("miniled", miniled);
                hdr = ScreenCCD.GetHDRStatus();
            }

            bool screenEnabled = (frequency >= 0);

            AppConfig.Set("frequency", frequency);
            AppConfig.Set("overdrive", overdrive);

            Program.settingsForm.Invoke(delegate
            {
                Program.settingsForm.VisualiseScreen(
                    screenEnabled: screenEnabled,
                    screenAuto: screenAuto,
                    frequency: frequency,
                    maxFrequency: maxFrequency,
                    overdrive: overdrive,
                    overdriveSetting: overdriveSetting,
                    miniled1: miniled1,
                    miniled2: miniled2,
                    hdr: hdr
                );
            });

        }
    }
}
