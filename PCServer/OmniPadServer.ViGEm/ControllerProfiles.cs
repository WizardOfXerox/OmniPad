using System;
using System.Collections.Generic;

namespace OmniPadServer.ViGEm;

/// <summary>
/// Curated preset controller profiles supported across HIDOmniPadBus.
/// Includes standard WHQL gaming controllers, advanced console gamepads,
/// racing wheels with gyro steering, and flight simulator HOTAS rigs.
/// </summary>
public enum ControllerProfilePreset
{
    // Standard Gaming
    Xbox360_WHQL,
    DualShock4_WHQL,
    DualSense_PS5,
    DualSense_Edge,
    
    // Nintendo Suite
    Switch_Pro,
    JoyCon_L,
    JoyCon_R,
    GameCube,
    
    // Handhelds & Pro Controllers
    SteamDeck,
    Xbox_Elite_Series2,
    Xbox_Series_XS,
    
    // Simulations & Specialty
    RacingWheel_Logitech,
    FlightSim_HOTAS,
    ArcadeStick_HORI,
    
    // Zero-Driver Fallback
    KeyboardMouse
}

/// <summary>
/// Metadata descriptor for a controller profile preset.
/// </summary>
public sealed record ControllerProfileInfo(
    ControllerProfilePreset Preset,
    string DisplayName,
    string Category,
    string Engine,
    string UnderlyingProfileId,
    string Description,
    bool SupportsTouchpad = false,
    bool SupportsMotion = false,
    bool SupportsPaddles = false,
    bool IsSimulation = false
);

public static class ControllerProfileCatalog
{
    public static readonly IReadOnlyDictionary<ControllerProfilePreset, ControllerProfileInfo> Profiles =
        new Dictionary<ControllerProfilePreset, ControllerProfileInfo>
        {
            [ControllerProfilePreset.Xbox360_WHQL] = new(
                ControllerProfilePreset.Xbox360_WHQL,
                "Microsoft Xbox 360",
                "Standard",
                "ViGEm (KMDF / WHQL)",
                "xbox-360-wired",
                "Authentic Microsoft Xbox 360 controller. Maximum compatibility with anti-cheat games (EAC, Vanguard, BattlEye)."
            ),

            [ControllerProfilePreset.DualShock4_WHQL] = new(
                ControllerProfilePreset.DualShock4_WHQL,
                "Sony DualShock 4 (PS4)",
                "Standard",
                "ViGEm (KMDF / WHQL)",
                "dualshock-4-v2",
                "Authentic Sony PS4 controller with native DirectInput/HID support for PC ports."
            ),

            [ControllerProfilePreset.DualSense_PS5] = new(
                ControllerProfilePreset.DualSense_PS5,
                "Sony DualSense (PS5)",
                "PlayStation",
                "HIDMaestro (UMDF2 Direct HID)",
                "dualsense",
                "Next-gen PS5 controller with dual-point multi-touch capacitive touchpad, 6-axis gyro, and battery status reporting.",
                SupportsTouchpad: true,
                SupportsMotion: true
            ),

            [ControllerProfilePreset.DualSense_Edge] = new(
                ControllerProfilePreset.DualSense_Edge,
                "Sony DualSense Edge (PS5)",
                "PlayStation",
                "HIDMaestro (UMDF2 Direct HID)",
                "dualsense-edge",
                "Pro PS5 controller with multi-touch touchpad, rear half-dome paddles, and function buttons.",
                SupportsTouchpad: true,
                SupportsMotion: true,
                SupportsPaddles: true
            ),

            [ControllerProfilePreset.Switch_Pro] = new(
                ControllerProfilePreset.Switch_Pro,
                "Nintendo Switch Pro Controller",
                "Nintendo",
                "HIDMaestro (UMDF2 Direct HID)",
                "switch-pro",
                "Official Nintendo Switch Pro Controller with full 6-axis motion for Yuzu, Ryujinx, Cemu, and Dolphin.",
                SupportsMotion: true
            ),

            [ControllerProfilePreset.JoyCon_L] = new(
                ControllerProfilePreset.JoyCon_L,
                "Nintendo Joy-Con (Left)",
                "Nintendo",
                "HIDMaestro (UMDF2 Direct HID)",
                "joycon-l",
                "Single horizontal or vertical Joy-Con (L) with analog stick and SL/SR shoulder buttons.",
                SupportsMotion: true
            ),

            [ControllerProfilePreset.JoyCon_R] = new(
                ControllerProfilePreset.JoyCon_R,
                "Nintendo Joy-Con (Right)",
                "Nintendo",
                "HIDMaestro (UMDF2 Direct HID)",
                "joycon-r",
                "Single horizontal or vertical Joy-Con (R) with face buttons and SL/SR shoulder buttons.",
                SupportsMotion: true
            ),

            [ControllerProfilePreset.GameCube] = new(
                ControllerProfilePreset.GameCube,
                "Nintendo GameCube Controller",
                "Nintendo",
                "HIDMaestro (UMDF2 Direct HID)",
                "gamecube-adapter",
                "Iconic GameCube layout with analog trigger resistance and digital click. The ultimate Smash Bros controller."
            ),

            [ControllerProfilePreset.SteamDeck] = new(
                ControllerProfilePreset.SteamDeck,
                "Valve Steam Deck Controller",
                "Steam & Handhelds",
                "HIDMaestro (UMDF2 Direct HID)",
                "steam-deck",
                "Steam Deck controller featuring dual haptic trackpads, rear grip buttons (L4/L5/R4/R5), and gyro.",
                SupportsTouchpad: true,
                SupportsMotion: true,
                SupportsPaddles: true
            ),

            [ControllerProfilePreset.Xbox_Elite_Series2] = new(
                ControllerProfilePreset.Xbox_Elite_Series2,
                "Xbox Elite Wireless Controller Series 2",
                "Xbox",
                "HIDMaestro (UMDF2 Direct HID)",
                "xbox-elite-v2",
                "Pro Xbox controller with 4 rear paddle triggers (P1-P4) and hair-trigger actuation for competitive FPS.",
                SupportsPaddles: true
            ),

            [ControllerProfilePreset.Xbox_Series_XS] = new(
                ControllerProfilePreset.Xbox_Series_XS,
                "Xbox Series X|S Wireless Controller",
                "Xbox",
                "HIDMaestro (UMDF2 Direct HID)",
                "xbox-series-xs",
                "Modern Xbox controller with hybrid D-pad and dedicated Share button."
            ),

            [ControllerProfilePreset.RacingWheel_Logitech] = new(
                ControllerProfilePreset.RacingWheel_Logitech,
                "Logitech G29 Racing Wheel & Pedals",
                "Simulations",
                "HIDMaestro (UMDF2 Direct HID)",
                "logitech-g29",
                "Transforms your phone into a physical steering wheel using gyroscope tilt, with on-screen throttle, brake, and paddle shifters.",
                SupportsMotion: true,
                IsSimulation: true
            ),

            [ControllerProfilePreset.FlightSim_HOTAS] = new(
                ControllerProfilePreset.FlightSim_HOTAS,
                "Saitek X52 Pro HOTAS Flight System",
                "Simulations",
                "HIDMaestro (UMDF2 Direct HID)",
                "saitek-x52-pro",
                "Hands-On Throttle-And-Stick rig for Flight Simulator, Star Citizen, and Elite Dangerous. Uses phone tilt for joystick pitch/roll and on-screen throttle slider.",
                SupportsMotion: true,
                IsSimulation: true
            ),

            [ControllerProfilePreset.ArcadeStick_HORI] = new(
                ControllerProfilePreset.ArcadeStick_HORI,
                "HORI Fighting Stick Alpha",
                "Arcade",
                "HIDMaestro (UMDF2 Direct HID)",
                "hori-fighting-stick-alpha",
                "Classic 8-button Japanese arcade fight stick layout for Tekken, Street Fighter, and Guilty Gear."
            ),

            [ControllerProfilePreset.KeyboardMouse] = new(
                ControllerProfilePreset.KeyboardMouse,
                "Virtual Keyboard & Mouse",
                "Zero-Driver",
                "Windows SendInput",
                "none",
                "Zero-driver fallback using Windows SendInput API. Works on any locked-down PC without administrator permissions."
            )
        };

    public static ControllerProfileInfo Get(ControllerProfilePreset preset) =>
        Profiles.TryGetValue(preset, out var info) ? info : Profiles[ControllerProfilePreset.Xbox360_WHQL];

    public static ControllerProfilePreset Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ControllerProfilePreset.Xbox360_WHQL;

        string normalized = name.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");

        return normalized switch
        {
            "xbox360" or "x360" or "xbox" => ControllerProfilePreset.Xbox360_WHQL,
            "dualshock4" or "ds4" or "ps4" => ControllerProfilePreset.DualShock4_WHQL,
            "dualsense" or "ps5" or "dualsenseps5" => ControllerProfilePreset.DualSense_PS5,
            "dualsenseedge" or "ps5edge" => ControllerProfilePreset.DualSense_Edge,
            "switchpro" or "switch" or "nintendoswitch" => ControllerProfilePreset.Switch_Pro,
            "joyconl" or "joyconleft" => ControllerProfilePreset.JoyCon_L,
            "joyconr" or "joyconright" => ControllerProfilePreset.JoyCon_R,
            "gamecube" or "smash" or "gc" => ControllerProfilePreset.GameCube,
            "steamdeck" or "deck" => ControllerProfilePreset.SteamDeck,
            "xboxelite" or "elite2" or "xboxeliteseries2" => ControllerProfilePreset.Xbox_Elite_Series2,
            "xboxseries" or "xboxseriesxs" or "seriesxs" => ControllerProfilePreset.Xbox_Series_XS,
            "racingwheel" or "wheel" or "g29" or "logitechwheel" => ControllerProfilePreset.RacingWheel_Logitech,
            "flightsim" or "hotas" or "x52" or "flight" => ControllerProfilePreset.FlightSim_HOTAS,
            "arcadestick" or "fightstick" or "arcade" => ControllerProfilePreset.ArcadeStick_HORI,
            "kbm" or "keyboard" or "mouse" or "zerodriver" => ControllerProfilePreset.KeyboardMouse,
            _ => ControllerProfilePreset.Xbox360_WHQL
        };
    }
}
