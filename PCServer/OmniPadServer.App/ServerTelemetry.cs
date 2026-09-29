using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniPadServer.Core;

namespace OmniPadServer.App;

public sealed class SlotTelemetryData
{
    public int Slot { get; set; }
    public bool IsConnected { get; set; }
    public string EndPoint { get; set; } = "Disconnected";
    public long LastPacketTick { get; set; }
    public double PadHz { get; set; }
    public double TouchHz { get; set; }
    public double MotionHz { get; set; }

    // Gamepad
    public short ThumbLX { get; set; }
    public short ThumbLY { get; set; }
    public double NormLX => Math.Round(ThumbLX / 32768.0, 3);
    public double NormLY => Math.Round(ThumbLY / 32768.0, 3);
    public double LeftMagnitude => Math.Round(Math.Min(1.0, Math.Sqrt(NormLX * NormLX + NormLY * NormLY)), 3);
    public double LeftAngleDeg => Math.Round((Math.Atan2(NormLY, NormLX) * 180.0 / Math.PI + 360.0) % 360.0, 1);

    public short ThumbRX { get; set; }
    public short ThumbRY { get; set; }
    public double NormRX => Math.Round(ThumbRX / 32768.0, 3);
    public double NormRY => Math.Round(ThumbRY / 32768.0, 3);
    public double RightMagnitude => Math.Round(Math.Min(1.0, Math.Sqrt(NormRX * NormRX + NormRY * NormRY)), 3);
    public double RightAngleDeg => Math.Round((Math.Atan2(NormRY, NormRX) * 180.0 / Math.PI + 360.0) % 360.0, 1);

    public byte LeftTrigger { get; set; }
    public double LeftTriggerPct => Math.Round((LeftTrigger / 255.0) * 100.0, 1);
    public byte RightTrigger { get; set; }
    public double RightTriggerPct => Math.Round((RightTrigger / 255.0) * 100.0, 1);

    public ushort Buttons { get; set; }
    public string ActiveButtons { get; set; } = "";

    // Touchpad
    public bool TouchClicked { get; set; }
    public bool TouchF0Active { get; set; }
    public byte TouchF0Id { get; set; }
    public ushort TouchF0X { get; set; }
    public ushort TouchF0Y { get; set; }
    public double TouchF0NormX => Math.Round((TouchF0X / 1920.0) * 100.0, 1);
    public double TouchF0NormY => Math.Round((TouchF0Y / 942.0) * 100.0, 1);

    public bool TouchF1Active { get; set; }
    public byte TouchF1Id { get; set; }
    public ushort TouchF1X { get; set; }
    public ushort TouchF1Y { get; set; }
    public double TouchF1NormX => Math.Round((TouchF1X / 1920.0) * 100.0, 1);
    public double TouchF1NormY => Math.Round((TouchF1Y / 942.0) * 100.0, 1);

    // Motion
    public float PitchDegS { get; set; }
    public float RollDegS { get; set; }
    public float YawDegS { get; set; }
    public float AccelXG { get; set; }
    public float AccelYG { get; set; }
    public float AccelZG { get; set; }

    // Rumble
    public byte RumbleLarge { get; set; }
    public byte RumbleSmall { get; set; }
    public long LastRumbleTick { get; set; }

    [JsonIgnore]
    public int PadCounter;
    [JsonIgnore]
    public int TouchCounter;
    [JsonIgnore]
    public int MotionCounter;
    [JsonIgnore]
    public long LastRateCalcTick = Stopwatch.GetTimestamp();
}

public sealed class TelemetrySnapshot
{
    public bool DebugMode { get; set; }
    public string ActiveDriver { get; set; } = "Unknown";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public SlotTelemetryData[] Slots { get; set; } = [];
}

/// <summary>
/// Thread-safe real-time telemetry engine and live debug logger for OmniPadServer.
/// Exposes precise gamepad, touchpad, motion, and driver statistics to console and web APIs.
/// </summary>
public static class ServerTelemetry
{
    private static readonly SlotTelemetryData[] _slots = new SlotTelemetryData[IPadBackend.MaxPads];
    private static readonly object[] _locks = new object[IPadBackend.MaxPads];
    private static bool _debugMode;
    private static string _activeDriverName = "ViGEm (Kernel WHQL)";
    private static CancellationTokenSource? _loggerCts;
    private static Task? _loggerTask;

    public static bool DebugMode
    {
        get => _debugMode;
        set
        {
            _debugMode = value;
            if (_debugMode)
            {
                StartLogger();
            }
        }
    }

    public static string ActiveDriverName
    {
        get => _activeDriverName;
        set => _activeDriverName = value;
    }

    static ServerTelemetry()
    {
        for (int i = 0; i < IPadBackend.MaxPads; i++)
        {
            _slots[i] = new SlotTelemetryData { Slot = i };
            _locks[i] = new object();
        }
    }

    public static void RecordConnect(int slot, string endpoint)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_locks[slot])
        {
            var s = _slots[slot];
            s.IsConnected = true;
            s.EndPoint = endpoint;
            s.LastPacketTick = Stopwatch.GetTimestamp();
        }

        if (_debugMode)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[DEBUG] Slot {slot} CONNECTED: {endpoint}");
            Console.ResetColor();
        }
    }

    public static void RecordDisconnect(int slot)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_locks[slot])
        {
            var s = _slots[slot];
            s.IsConnected = false;
            s.EndPoint = "Disconnected";
            s.ActiveButtons = "";
            s.PadHz = 0;
            s.TouchHz = 0;
            s.MotionHz = 0;
        }

        if (_debugMode)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[DEBUG] Slot {slot} DISCONNECTED");
            Console.ResetColor();
        }
    }

    public static void RecordPad(int slot, in PadState state)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_locks[slot])
        {
            var s = _slots[slot];
            s.IsConnected = true;
            s.LastPacketTick = Stopwatch.GetTimestamp();
            s.PadCounter++;

            s.ThumbLX = state.ThumbLX;
            s.ThumbLY = state.ThumbLY;
            s.ThumbRX = state.ThumbRX;
            s.ThumbRY = state.ThumbRY;
            s.LeftTrigger = state.LeftTrigger;
            s.RightTrigger = state.RightTrigger;
            s.Buttons = state.Buttons;
            s.ActiveButtons = FormatButtons(state.Buttons);

            UpdateRates(s);
        }
    }

    public static void RecordTouch(int slot, in TouchpadState state)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_locks[slot])
        {
            var s = _slots[slot];
            s.IsConnected = true;
            s.LastPacketTick = Stopwatch.GetTimestamp();
            s.TouchCounter++;

            s.TouchClicked = state.Clicked;
            s.TouchF0Active = state.Finger0.IsActive;
            s.TouchF0Id = state.Finger0.Id;
            s.TouchF0X = state.Finger0.X;
            s.TouchF0Y = state.Finger0.Y;

            s.TouchF1Active = state.Finger1.IsActive;
            s.TouchF1Id = state.Finger1.Id;
            s.TouchF1X = state.Finger1.X;
            s.TouchF1Y = state.Finger1.Y;

            UpdateRates(s);
        }
    }

    public static void RecordMotion(int slot, in MotionState motion)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_locks[slot])
        {
            var s = _slots[slot];
            s.IsConnected = true;
            s.LastPacketTick = Stopwatch.GetTimestamp();
            s.MotionCounter++;

            s.PitchDegS = motion.GyroX;
            s.RollDegS = motion.GyroY;
            s.YawDegS = motion.GyroZ;
            s.AccelXG = motion.AccelX;
            s.AccelYG = motion.AccelY;
            s.AccelZG = motion.AccelZ;

            UpdateRates(s);
        }
    }

    public static void RecordRumble(int slot, byte large, byte small)
    {
        if (slot < 0 || slot >= IPadBackend.MaxPads) return;
        lock (_locks[slot])
        {
            var s = _slots[slot];
            s.RumbleLarge = large;
            s.RumbleSmall = small;
            s.LastRumbleTick = Stopwatch.GetTimestamp();
        }

        if (_debugMode)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[DEBUG RUMBLE] Slot {slot}: Large={large} ({large * 100 / 255}%), Small={small} ({small * 100 / 255}%)");
            Console.ResetColor();
        }
    }

    private static void UpdateRates(SlotTelemetryData s)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSec = (now - s.LastRateCalcTick) / (double)Stopwatch.Frequency;
        if (elapsedSec >= 1.0)
        {
            s.PadHz = Math.Round(s.PadCounter / elapsedSec, 1);
            s.TouchHz = Math.Round(s.TouchCounter / elapsedSec, 1);
            s.MotionHz = Math.Round(s.MotionCounter / elapsedSec, 1);
            s.PadCounter = 0;
            s.TouchCounter = 0;
            s.MotionCounter = 0;
            s.LastRateCalcTick = now;
        }
    }

    public static TelemetrySnapshot GetSnapshot()
    {
        var slotsCopy = new SlotTelemetryData[IPadBackend.MaxPads];
        for (int i = 0; i < IPadBackend.MaxPads; i++)
        {
            lock (_locks[i])
            {
                var src = _slots[i];
                slotsCopy[i] = new SlotTelemetryData
                {
                    Slot = src.Slot,
                    IsConnected = src.IsConnected,
                    EndPoint = src.EndPoint,
                    LastPacketTick = src.LastPacketTick,
                    PadHz = src.PadHz,
                    TouchHz = src.TouchHz,
                    MotionHz = src.MotionHz,
                    ThumbLX = src.ThumbLX,
                    ThumbLY = src.ThumbLY,
                    ThumbRX = src.ThumbRX,
                    ThumbRY = src.ThumbRY,
                    LeftTrigger = src.LeftTrigger,
                    RightTrigger = src.RightTrigger,
                    Buttons = src.Buttons,
                    ActiveButtons = src.ActiveButtons,
                    TouchClicked = src.TouchClicked,
                    TouchF0Active = src.TouchF0Active,
                    TouchF0Id = src.TouchF0Id,
                    TouchF0X = src.TouchF0X,
                    TouchF0Y = src.TouchF0Y,
                    TouchF1Active = src.TouchF1Active,
                    TouchF1Id = src.TouchF1Id,
                    TouchF1X = src.TouchF1X,
                    TouchF1Y = src.TouchF1Y,
                    PitchDegS = src.PitchDegS,
                    RollDegS = src.RollDegS,
                    YawDegS = src.YawDegS,
                    AccelXG = src.AccelXG,
                    AccelYG = src.AccelYG,
                    AccelZG = src.AccelZG,
                    RumbleLarge = src.RumbleLarge,
                    RumbleSmall = src.RumbleSmall,
                    LastRumbleTick = src.LastRumbleTick
                };
            }
        }

        return new TelemetrySnapshot
        {
            DebugMode = _debugMode,
            ActiveDriver = _activeDriverName,
            Timestamp = DateTime.UtcNow,
            Slots = slotsCopy
        };
    }

    public static void StartLogger()
    {
        if (_loggerTask != null && !_loggerTask.IsCompleted) return;

        _loggerCts = new CancellationTokenSource();
        var token = _loggerCts.Token;

        _loggerTask = Task.Run(async () =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[DEBUG MODE] Live telemetry monitor active. Press 'D' to toggle on/off.");
            Console.ResetColor();

            while (!token.IsCancellationRequested && _debugMode)
            {
                try
                {
                    await Task.Delay(100, token); // 10 Hz refresh
                    PrintLiveStatus();
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, token);
    }

    public static void StopLogger()
    {
        _loggerCts?.Cancel();
        _loggerCts = null;
        _loggerTask = null;
    }

    private static void PrintLiveStatus()
    {
        for (int i = 0; i < IPadBackend.MaxPads; i++)
        {
            SlotTelemetryData s;
            lock (_locks[i])
            {
                if (!_slots[i].IsConnected) continue;
                s = _slots[i];
            }

            // Only print if active or recent packets (within 3 seconds)
            long now = Stopwatch.GetTimestamp();
            if ((now - s.LastPacketTick) / (double)Stopwatch.Frequency > 3.0) continue;

            string touchStr = s.TouchF0Active
                ? $"F0:({s.TouchF0X,4},{s.TouchF0Y,3}) [ID:{s.TouchF0Id}]" + (s.TouchF1Active ? $" F1:({s.TouchF1X,4},{s.TouchF1Y,3})" : "")
                : (s.TouchClicked ? "[TOUCH CLICK]" : "Touch: Idle");

            string gyroStr = $"G:({s.PitchDegS,5:F1}°,{s.RollDegS,5:F1}°,{s.YawDegS,5:F1}°/s)";

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"[DBG #{i}] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"L:({s.NormLX,5:F2},{s.NormLY,5:F2}|M:{s.LeftMagnitude:0.00}) R:({s.NormRX,5:F2},{s.NormRY,5:F2}|M:{s.RightMagnitude:0.00}) ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"LT:{s.LeftTriggerPct,3:0}% RT:{s.RightTriggerPct,3:0}% ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(string.IsNullOrEmpty(s.ActiveButtons) ? "BTN:[-] " : $"BTN:[{s.ActiveButtons}] ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write($"{touchStr} ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"{gyroStr} ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"({s.PadHz:0}Hz)");
            Console.ResetColor();
        }
    }

    private static string FormatButtons(ushort buttons)
    {
        if (buttons == 0) return "";
        var list = new System.Collections.Generic.List<string>(6);
        if ((buttons & (ushort)Protocol.Buttons.A) != 0) list.Add("A");
        if ((buttons & (ushort)Protocol.Buttons.B) != 0) list.Add("B");
        if ((buttons & (ushort)Protocol.Buttons.X) != 0) list.Add("X");
        if ((buttons & (ushort)Protocol.Buttons.Y) != 0) list.Add("Y");
        if ((buttons & (ushort)Protocol.Buttons.LeftShoulder) != 0) list.Add("LB");
        if ((buttons & (ushort)Protocol.Buttons.RightShoulder) != 0) list.Add("RB");
        if ((buttons & (ushort)Protocol.Buttons.Back) != 0) list.Add("Back");
        if ((buttons & (ushort)Protocol.Buttons.Start) != 0) list.Add("Start");
        if ((buttons & (ushort)Protocol.Buttons.Guide) != 0) list.Add("Guide");
        if ((buttons & (ushort)Protocol.Buttons.LeftThumb) != 0) list.Add("LS");
        if ((buttons & (ushort)Protocol.Buttons.RightThumb) != 0) list.Add("RS");
        if ((buttons & (ushort)Protocol.Buttons.DPadUp) != 0) list.Add("D-Up");
        if ((buttons & (ushort)Protocol.Buttons.DPadDown) != 0) list.Add("D-Down");
        if ((buttons & (ushort)Protocol.Buttons.DPadLeft) != 0) list.Add("D-Left");
        if ((buttons & (ushort)Protocol.Buttons.DPadRight) != 0) list.Add("D-Right");
        if ((buttons & (ushort)Protocol.Buttons.Touchpad) != 0) list.Add("TouchBtn");
        return string.Join(",", list);
    }
}
