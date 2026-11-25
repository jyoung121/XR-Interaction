using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>The diagnostics and experiment share exactly the same supported controls.</summary>
public static class ResearchController
{
    public static bool AChecked { get; private set; }
    public static bool BChecked { get; private set; }
    private static int testedDevice = -1;
    private static bool listening;
    public static bool Connected
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            return pad != null && pad.added && pad.enabled;
#else
            return false;
#endif
        }
    }
    public static string Description
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Connected) return Gamepad.current.displayName + " (Gamepad, ID " + Gamepad.current.deviceId + ")";
            foreach (var device in InputSystem.devices)
                if (device is Joystick) return device.displayName + " detected as Joystick; Gamepad mapping required";
#endif
            return "No supported Gamepad detected";
        }
    }
    public static bool Ready => Connected && AChecked && BChecked;
    public static void Refresh()
    {
#if ENABLE_INPUT_SYSTEM
        if (!listening)
        {
            InputSystem.onDeviceChange += (device, change) => {
                if (device.deviceId == testedDevice && (change == InputDeviceChange.Disconnected ||
                    change == InputDeviceChange.Removed || change == InputDeviceChange.Disabled ||
                    change == InputDeviceChange.Reconnected || change == InputDeviceChange.ConfigurationChanged))
                { AChecked = BChecked = false; }
            };
            listening = true;
        }
        int id = Connected ? Gamepad.current.deviceId : -1;
        if (id != testedDevice) { testedDevice = id; AChecked = BChecked = false; }
#endif
    }
    public static void TestButtons()
    {
        Refresh();
#if ENABLE_INPUT_SYSTEM
        if (!Connected) return;
        if (Gamepad.current.buttonSouth.wasPressedThisFrame) AChecked = true;
        if (Gamepad.current.buttonEast.wasPressedThisFrame) BChecked = true;
#endif
    }
    public static bool PrimaryPressed
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Connected && Gamepad.current.buttonSouth.wasPressedThisFrame) return true;
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
#endif
#endif
            return false;
        }
    }
    public static bool AbortHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Connected && Gamepad.current.buttonEast.isPressed;
#else
            return false;
#endif
        }
    }
}
