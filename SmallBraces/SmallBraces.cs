using System;
using System.Diagnostics;
using System.Net.Mime;
using System.Windows;
using System.Windows.Controls;
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

        private bool EqualsIgnoringWhitespace(ITextViewLine line, string eq)
        {
            int j = 0; // eq must not have any whitespace.
            for (int i = line.Start; i < line.End; i++)
            {
                char c = line.Snapshot[i];
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                if (c != eq[j])
                {
                    return false;
                }
                j++;

            }
            return true;
        }

        public LineTransform GetLineTransformInternal(ITextViewLine line)
        {
            if (line.Length > 100)
            {
                return _defaultTransform;
            }
            if (EqualsIgnoringWhitespace(line, "///<summary>") || EqualsIgnoringWhitespace(line, "///</summary>"))
            {
                return _commentCruftTransform;
            }

            bool blankLine = true;
            for (int i = line.Start; i < line.End; i++)
            {
                char c = line.Snapshot[i];
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                if (char.IsLetterOrDigit(c))
                {
                    return _defaultTransform;
                }
                blankLine = false;
            }
            if (blankLine)
            {
                return _blankLineTransform;
            }
            else
            {
                return _braceTransform;
            }
        }
        public LineTransform GetLineTransform(ITextViewLine line, double yPosition, ViewRelativePosition placement)
        {
            LineTransform expected = GetLineTransformInternal(line);
            if (line.ContainsBufferPosition(_view.Selection.ActivePoint.Position))
            {
                var h = 1.0 / expected.VerticalScale;
                return new LineTransform(h/2, h/2, expected.VerticalScale);
            }
            return expected;
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
        private void CreateVisuals(ITextViewLine line)
        {
            var span = new SnapshotSpan(_view.TextSnapshot, Span.FromBounds(line.Start.Position, line.End.Position));
            IWpfTextViewLineCollection textViewLines = _view.TextViewLines;
            Geometry g = textViewLines.GetMarkerGeometry(span);
            if (g == null)
            {
                // This happens sometimes, not really sure why :(
                return;
            }
            Rect r = g.Bounds;
            r.Height += 20;
            g = new RectangleGeometry(r);

#if true
            var textblock = new TextBlock();
            textblock.Text = LineToString(line);
            textblock.FontSize = 20.0;
            textblock.FontWeight = FontWeights.Bold;
            textblock.Foreground = new SolidColorBrush(Colors.Black);
            VisualBrush b = new VisualBrush(textblock);
# else 
            Brush b = new RadialGradientBrush(Colors.Black, Colors.Aqua);
#endif
            
            GeometryDrawing drawing = new GeometryDrawing(b, new Pen(), g);
            DrawingImage drawingImage = new DrawingImage(drawing);
            Image image = new Image();
            image.Source = drawingImage;
            Canvas.SetLeft(image, g.Bounds.Left);
            Canvas.SetTop(image, g.Bounds.Top);


//
//            DrawingVisual visual = new DrawingVisual();
//            DrawingContext drawingContext = visual.RenderOpen();
//
//            FormattedText text = new FormattedText("Waterlilies",
//                    new CultureInfo("en-us"),
//                    FlowDirection.LeftToRight,
//                    new Typeface(this.FontFamily, FontStyles.Normal, FontWeights.Normal, new FontStretch()),
//                    this.FontSize,
//                    Brushes.White);
//            drawingContext.DrawText(text, g.Bounds.TopLeft);

            //_layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, image, null);
            _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, image, null);
        }
        void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            ITextViewLine newLine = GetLineByPos(e.NewPosition);
            ITextViewLine oldLine = GetLineByPos(e.OldPosition);
            if (newLine != oldLine)
            {
                var top = _view.GetTextViewLineContainingBufferPosition(e.NewPosition.BufferPosition).Top;
                
                // Forcefully invalidate the line we were on and the line we are moving to.
                // This feels like a bit more of a hack than it should be, but hey, it works!
                _view.DisplayTextLineContainingBufferPosition(
                    e.OldPosition.BufferPosition,
                    top,
                    ViewRelativePosition.Top);
                _view.DisplayTextLineContainingBufferPosition(
                    e.NewPosition.BufferPosition,
                    top,
                    ViewRelativePosition.Top);
            }
        }
    }
}