using RemoteCapture.Lib.WindowsRuntimeHelpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;

namespace RemoteCapture.Lib.CaptureSampleCore
{
    internal class BasicSampleApplication
    {
        private Compositor _compositor;
        private ContainerVisual _root;

        private SpriteVisual _content;
        private CompositionSurfaceBrush _brush;

        private IDirect3DDevice _device;
        private BasicCapture _capture;

        public BasicSampleApplication(Compositor compositor)
        {
            _compositor = compositor;
            _device = Direct3D11Helper.CreateDevice();

            // Setup the root.
            _root = _compositor.CreateContainerVisual();
            _root.RelativeSizeAdjustment = Vector2.One;

            // Setup the content.
            _brush = _compositor.CreateSurfaceBrush();
            _brush.HorizontalAlignmentRatio = 0.5f;
            _brush.VerticalAlignmentRatio = 0.5f;
            _brush.Stretch = CompositionStretch.Uniform;

            var shadow = _compositor.CreateDropShadow();
            shadow.Mask = _brush;

            _content = _compositor.CreateSpriteVisual();
            _content.AnchorPoint = new Vector2(0.5f);
            _content.RelativeOffsetAdjustment = new Vector3(0.5f, 0.5f, 0);
            _content.RelativeSizeAdjustment = Vector2.One;
            _content.Size = new Vector2(-80, -80);
            _content.Brush = _brush;
            _content.Shadow = shadow;
            _root.Children.InsertAtTop(_content);
        }

        public Visual Visual => _root;

        #region 

        public void Dispose()
        {
            StopCapture();
            _compositor = null;
            _root.Dispose();
            _content.Dispose();
            _brush.Dispose();
            _device.Dispose();
        }

        #endregion

        public void StartCaptureFromItem(GraphicsCaptureItem item)
        {
            StopCapture();
            _capture = new BasicCapture(_device, item);

            var surface = _capture.CreateSurface(_compositor);
            _brush.Surface = surface;

            _capture.StartCapture();
        }

        public void StopCapture()
        {
            _capture?.Dispose();
            _brush.Surface = null;
        }

        public void SaveSnapshot(string filePath)
        {
            if (_capture == null)
            {
                throw new InvalidOperationException("Capture is not active.");
            }
            _capture.SaveSnapshot(filePath);
        }

        public byte[] GetCurrentFrameAsPng()
        {
            if (_capture == null)
            {
                return null;
            }
            return _capture.GetCurrentFrameAsPng();
        }

        public byte[] GetCurrentFrameAsJpeg(int quality = 75)
        {
            if (_capture == null)
            {
                return null;
            }
            return _capture.GetCurrentFrameAsJpeg(quality);
        }
    }
}
