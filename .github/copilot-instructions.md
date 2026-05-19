# Copilot Instructions

## プロジェクト ガイドライン
- User confirmed that mouse wheel scrolling and middle button (wheel click) remote control are working as expected in the RemoteCapture WebSocket-based remote desktop application; the half-width/full-width (IME toggle) key is also working as expected — the receiver no longer responds locally to the IME toggle key and forwards the key event to the sender only.
- Use GDI with OpenInputDesktop/SetThreadDesktop to capture LogonUI and lock screens when RemoteCapture runs as SYSTEM in an active user session; ensure desktop switching handles secure desktops correctly.