namespace RemoteCapture.Protocol;

public enum MessageType : byte
{
    FrameUpdate = 1,
    MouseEvent = 2,
    KeyboardEvent = 3,
    MouseWheelEvent = 4
}

public class FrameUpdateMessage
{
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
}

public class MouseEventMessage
{
    public int X { get; set; }
    public int Y { get; set; }
    public MouseButton Button { get; set; }
    public bool IsPressed { get; set; }
}

public class MouseWheelEventMessage
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Delta { get; set; }
}

public class KeyboardEventMessage
{
    public int VirtualKeyCode { get; set; }
    public bool IsPressed { get; set; }
    public bool IsExtendedKey { get; set; }
}

public enum MouseButton : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3
}
