using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;

namespace SmallBraces
{
    ///<summary>
    ///SmallBraces places red boxes behind all the "A"s in the editor window
    ///</summary>
    public class SmallBraces : ILineTransformSource
    {
        private static readonly LineTransform _defaultTransform = new LineTransform(0.0, 0.0, 1.0);
        private static readonly LineTransform _braceTransform = new LineTransform(0.0, 0.0, 0.05);
        private static readonly LineTransform _blankLineTransform = new LineTransform(0.0, 0.0, 0.5);
        private static readonly LineTransform _commentCruftTransform = new LineTransform(0.0, 0.0, 0.1);
        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;


        private SmallBraces(IWpfTextView view)
        {
            this._view = view;
            _layer = view.GetAdornmentLayer("SmallBracesAdornment");
            view.LayoutChanged += OnLayoutChanged;
            view.Caret.PositionChanged += OnCaretPositionChanged;
            Debug.Assert(_layer != null);
        }

        public static SmallBraces Create(IWpfTextView view)
        {
            return view.Properties.GetOrCreateSingletonProperty<SmallBraces>(() => new SmallBraces(view));
        }

        private enum LineType
        {
            JustWhitespace,
            JustCommentCruft,
            JustBraces,
            Default,
        };

        private LineType GetLineType(string line)
        {
            line = line.Trim();
            if (line.Equals(""))
            {
                return LineType.JustWhitespace;
            }
            if (line.Equals("/// <summary>") || line.Equals("/// </summary>"))
            {
                return LineType.JustCommentCruft;
            }
            if (Regex.IsMatch(line, @"^[(){}\s]+$")) // Do I have to escape these?
            {
                return LineType.JustBraces;
            }
            return LineType.Default;
        }
        public LineTransform GetLineTransformInternal(ITextViewLine line)
        {
            if (line.Length > 100)
            {
                return _defaultTransform;
            }
            LineType type = GetLineType(LineToString(line));
            switch (type)
            {
                case LineType.JustWhitespace:
                    return _blankLineTransform;
                case LineType.JustCommentCruft:
                    return _commentCruftTransform;
                case LineType.JustBraces:
                    return _braceTransform;
                case LineType.Default:
                    return _defaultTransform;
                default:
                    Debug.Assert(false);
                    throw new SystemException(); // Should never happen
            }
        }
        public LineTransform GetLineTransform(ITextViewLine line, double yPosition, ViewRelativePosition placement)
        {
            try
            {
//                return _defaultTransform;

                LineTransform expected = GetLineTransformInternal(line);
//            if (line.ContainsBufferPosition(_view.Selection.ActivePoint.Position))
//            {
//                var h = 1.0 / expected.VerticalScale;
//                return new LineTransform(h/2, h/2, expected.VerticalScale);
//            }
                return expected;
            }
            catch (Exception ex)
            {
                Debugger.Break();
                return _defaultTransform;
            }
        }

        private ITextViewLine GetLineByPos(CaretPosition pos)
        {
            return _view.GetTextViewLineContainingBufferPosition(pos.BufferPosition);
        }


        void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            foreach (ITextViewLine line in e.NewOrReformattedLines)
            {
                this.CreateVisuals(line);
            }
        }

        private string LineToString(ITextViewLine line)
        {
            string s = "";
            for (int i = line.Start; i < line.End; i++)
            {
                char c = line.Snapshot[i];
                s += c.ToString();
            }
            return s;
        }
        private static readonly double normalLineHeight = (double)new FontSizeConverter().ConvertFrom("10pt");

        private SnapshotSpan LineSpan(ITextViewLine line)
        {
            var start = line.Start.Position;
            while (start < line.End.Position && Char.IsWhiteSpace(line.Snapshot[start])) start++;
            return new SnapshotSpan(_view.TextSnapshot, Span.FromBounds(start, line.End.Position));
        }
        private UIElement CreateVisualsInternal(ITextViewLine line)
        {
            var h = normalLineHeight/2;
            if (line.ContainsBufferPosition(_view.Selection.ActivePoint.Position))
            {
                h = normalLineHeight;
            }
            var str = LineToString(line).Trim();
            var type = GetLineType(str);
            if (type == LineType.Default)
            {
                return null;
            }

            IWpfTextViewLineCollection textViewLines = _view.TextViewLines;
            Geometry g = textViewLines.GetMarkerGeometry(LineSpan(line));
            if (g == null)
            {
                // This happens sometimes, not really sure why :(
                return null;
            }
            Rect r = g.Bounds;
//            r.Height += 20;
            r.Y -= h/2;
            g = new RectangleGeometry(r);

            var textblock = new TextBlock();
            textblock.Text = str; //" " + string.Join(" ", str.ToCharArray());
            textblock.FontSize = h;
            textblock.FontWeight = FontWeights.Normal;
            textblock.FontFamily = new FontFamily("Consolas");
            textblock.Foreground = new SolidColorBrush(Colors.Black);
            // Screw you Microsoft, why can't I just stack TranslateTransforms
            // and ScaleTransforms??
//            var t = new MatrixTransform(2.0, 0.0, 0, 1.0, g.Bounds.Left, g.Bounds.Top);
            var t = new TranslateTransform(g.Bounds.Left, g.Bounds.Top);
            textblock.LayoutTransform = t;
            textblock.RenderTransform = t;
            return textblock;

#if false
            VisualBrush b = new VisualBrush(textblock);
            GeometryDrawing drawing = new GeometryDrawing(b, new Pen(), g);
            DrawingImage drawingImage = new DrawingImage(drawing);
            Image image = new Image();
            image.Source = drawingImage;
            Canvas.SetLeft(image, g.Bounds.Left);
            Canvas.SetTop(image, g.Bounds.Top);
#endif
        }

        private UIElement previousLine = null;
        private void CreateVisuals(ITextViewLine line)
        {
            try
            {
                UIElement element = CreateVisualsInternal(line);
                if (element != null)
                {
                    _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, LineSpan(line), null, element, null);
                }
            }
            catch (Exception ex)
            {
                Debugger.Break();
            }
        }

        private static Image makeCursor()
        {
            Rect r = new Rect(0, 0, 2, normalLineHeight);
            Geometry g = new RectangleGeometry(r);
            Brush b = new SolidColorBrush(Colors.Black);
            GeometryDrawing drawing = new GeometryDrawing(b, new Pen(), g);
            DrawingImage drawingImage = new DrawingImage(drawing);
            Image image = new Image();
            image.Source = drawingImage;
            return image;
        }

        private Image cursor = makeCursor();

        void OnCaretPositionChangedInternal(object sender, CaretPositionChangedEventArgs e)
        {
            ITextViewLine newLine = GetLineByPos(e.NewPosition);
            ITextViewLine oldLine = GetLineByPos(e.OldPosition);
            // TODO: End of document makes this invalid. What we really want is a length
            // of zero, but AddAdornment pukes when we try that...
            var start = e.NewPosition.BufferPosition;            

            var span = new SnapshotSpan(_view.TextSnapshot, e.NewPosition.BufferPosition, 1);
            Geometry g = _view.TextViewLines.GetMarkerGeometry(span);
            Canvas.SetLeft(cursor, g.Bounds.Left);
            Canvas.SetTop(cursor, g.Bounds.Top);
            _layer.RemoveAdornment(cursor);
            _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, cursor, null);

            if (oldLine == newLine)
            {
                return;
            }
            if (previousLine != null) _layer.RemoveAdornment(previousLine);
            UIElement newAdornment = CreateVisualsInternal(newLine);
            if (newAdornment != null)
            {
                previousLine = newAdornment;
                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, LineSpan(newLine), null, newAdornment, null);
            }
        }
        void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            try
            {
                OnCaretPositionChangedInternal(sender, e);
            }
            catch (Exception ex)
            {
                Debugger.Break();
            }
//            ITextViewLine newLine = GetLineByPos(e.NewPosition);
//            ITextViewLine oldLine = GetLineByPos(e.OldPosition);
//            if (newLine != oldLine)
//            {
//                var top = _view.GetTextViewLineContainingBufferPosition(e.NewPosition.BufferPosition).Top;
//                
//                // Forcefully invalidate the line we were on and the line we are moving to.
//                // This feels like a bit more of a hack than it should be, but hey, it works!
//                _view.DisplayTextLineContainingBufferPosition(
//                    e.OldPosition.BufferPosition,
//                    top,
//                    ViewRelativePosition.Top);
//                _view.DisplayTextLineContainingBufferPosition(
//                    e.NewPosition.BufferPosition,
//                    top,
//                    ViewRelativePosition.Top);
//            }
        }
    }
}