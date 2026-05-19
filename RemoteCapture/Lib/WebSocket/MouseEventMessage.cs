using System;

namespace RemoteCapture.Lib.WebSocket
{
    public enum MouseEventType
    {
        Move,
        LeftDown,
        LeftUp,
        RightDown,
        RightUp
    }

    public class MouseEventMessage
    {
        public MouseEventType EventType { get; set; }
        public double NormalizedX { get; set; } // 0.0 ~ 1.0
        public double NormalizedY { get; set; } // 0.0 ~ 1.0
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
    }
}
