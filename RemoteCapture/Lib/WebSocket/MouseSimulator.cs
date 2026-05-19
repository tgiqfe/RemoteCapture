using System;
using System.Runtime.InteropServices;

namespace RemoteCapture.Lib.WebSocket
{
    public static class MouseSimulator
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        public static void MoveMouse(int x, int y)
        {
            SetCursorPos(x, y);
        }

        public static void LeftMouseDown()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        }

        public static void LeftMouseUp()
        {
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void RightMouseDown()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        }

        public static void RightMouseUp()
        {
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void ExecuteMouseEvent(MouseEventMessage mouseEvent)
        {
            if (mouseEvent == null)
                return;

            int screenX = (int)(mouseEvent.NormalizedX * mouseEvent.ScreenWidth);
            int screenY = (int)(mouseEvent.NormalizedY * mouseEvent.ScreenHeight);

            switch (mouseEvent.EventType)
            {
                case MouseEventType.Move:
                    MoveMouse(screenX, screenY);
                    break;
                case MouseEventType.LeftDown:
                    MoveMouse(screenX, screenY);
                    LeftMouseDown();
                    break;
                case MouseEventType.LeftUp:
                    MoveMouse(screenX, screenY);
                    LeftMouseUp();
                    break;
                case MouseEventType.RightDown:
                    MoveMouse(screenX, screenY);
                    RightMouseDown();
                    break;
                case MouseEventType.RightUp:
                    MoveMouse(screenX, screenY);
                    RightMouseUp();
                    break;
            }
        }
    }
}
