using System;
using System.Runtime.InteropServices;

namespace RemoteCapture.Lib.WebSocket
{
    public static class KeyboardSimulator
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        // Virtual Key Codes for modifier keys
        private const byte VK_SHIFT = 0x10;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_MENU = 0x12; // Alt key
        private const byte VK_LWIN = 0x5B;
        private const byte VK_RWIN = 0x5C;

        public static void KeyDown(byte keyCode)
        {
            keybd_event(keyCode, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        }

        public static void KeyUp(byte keyCode)
        {
            keybd_event(keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void ExecuteKeyboardEvent(KeyboardEventMessage keyEvent)
        {
            if (keyEvent == null)
                return;

            byte keyCode = (byte)keyEvent.KeyCode;

            // Handle modifier keys state
            if (keyEvent.EventType == KeyboardEventType.KeyDown)
            {
                // Press modifier keys first if needed
                if (keyEvent.IsShiftPressed && !IsModifierKey(keyCode, VK_SHIFT))
                    KeyDown(VK_SHIFT);
                if (keyEvent.IsCtrlPressed && !IsModifierKey(keyCode, VK_CONTROL))
                    KeyDown(VK_CONTROL);
                if (keyEvent.IsAltPressed && !IsModifierKey(keyCode, VK_MENU))
                    KeyDown(VK_MENU);
                if (keyEvent.IsWinPressed && !IsModifierKey(keyCode, VK_LWIN))
                    KeyDown(VK_LWIN);

                // Press the main key
                KeyDown(keyCode);
            }
            else if (keyEvent.EventType == KeyboardEventType.KeyUp)
            {
                // Release the main key
                KeyUp(keyCode);

                // Release modifier keys if needed
                if (keyEvent.IsShiftPressed && !IsModifierKey(keyCode, VK_SHIFT))
                    KeyUp(VK_SHIFT);
                if (keyEvent.IsCtrlPressed && !IsModifierKey(keyCode, VK_CONTROL))
                    KeyUp(VK_CONTROL);
                if (keyEvent.IsAltPressed && !IsModifierKey(keyCode, VK_MENU))
                    KeyUp(VK_MENU);
                if (keyEvent.IsWinPressed && !IsModifierKey(keyCode, VK_LWIN))
                    KeyUp(VK_LWIN);
            }
        }

        private static bool IsModifierKey(byte keyCode, byte modifierKeyCode)
        {
            // Check if the keyCode is a modifier key
            return keyCode == VK_SHIFT || keyCode == VK_CONTROL || keyCode == VK_MENU ||
                   keyCode == VK_LWIN || keyCode == VK_RWIN;
        }
    }
}
