using System;

namespace RemoteCapture.Lib.WebSocket
{
    public enum KeyboardEventType
    {
        KeyDown,
        KeyUp
    }

    public class KeyboardEventMessage
    {
        public KeyboardEventType EventType { get; set; }
        public int KeyCode { get; set; }
        public bool IsShiftPressed { get; set; }
        public bool IsCtrlPressed { get; set; }
        public bool IsAltPressed { get; set; }
        public bool IsWinPressed { get; set; }
    }
}
