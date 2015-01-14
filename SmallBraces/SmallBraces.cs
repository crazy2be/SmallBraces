using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;

namespace SmallBraces
{
    ///<summary>
    ///SmallBraces places red boxes behind all the "A"s in the editor window
    ///</summary>
    public class SmallBraces : ILineTransformSource
    {
        private static readonly double AlmostZero = 0.0000000001;
        private static readonly LineTransform _defaultTransform = new LineTransform(0.0, 0.0, 1.0);
        private static readonly LineTransform _braceTransform = new LineTransform(0.0, 0.0, AlmostZero);
        private static readonly LineTransform _blankLineTransform = new LineTransform(0.0, 0.0, 0.5);
        private static readonly LineTransform _commentCruftTransform = _braceTransform;
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

        private LineType GetLineStringType(string line)
        {
            line = line.Trim();
            if (line.Equals("")) return LineType.JustWhitespace;
            if (line.Equals("/// <summary>") || line.Equals("/// </summary>")) return LineType.JustCommentCruft;
            if (Regex.IsMatch(line, @"^[(){}\s]+$")) return LineType.JustBraces;
            return LineType.Default;
        }

        private LineType GetLineType(ITextViewLine line)
        {
            if (line.Length > 100) return LineType.Default;
            return GetLineStringType(LineToString(line));
        }
        public LineTransform GetLineTransformInternal(ITextViewLine line)
        {
            switch (GetLineType(line))
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
            if (GetLineType(line) == LineType.Default) return null;

            IWpfTextViewLineCollection textViewLines = _view.TextViewLines;
            Geometry g = textViewLines.GetMarkerGeometry(LineSpan(line));
            if (g == null) return null; // This happens sometimes, not really sure why :(
            Rect r = g.Bounds;
            r.Y -= h/2;
            g = new RectangleGeometry(r);

            var textblock = new TextBlock();
            textblock.Text = LineToString(line).Trim();
            textblock.FontSize = h;
            textblock.FontWeight = FontWeights.Normal;
            textblock.FontFamily = new FontFamily("Consolas");
            textblock.Foreground = new SolidColorBrush(Colors.Black);
            var t = new TranslateTransform(g.Bounds.Left, g.Bounds.Top);
            textblock.LayoutTransform = t;
            textblock.RenderTransform = t;
            return textblock;
        }
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
        
        private Image cursor = makeCursor();

        private static Image makeCursor()
        {
            try
            {
                return makeCursorInternal();
            }
            catch (Exception e)
            {
                Debugger.Break();
                return null;
            }
        }
        private static Image makeCursorInternal()
        {
            // Ghetto cursor
            Rect r = new Rect(0, 0, 1, normalLineHeight);
            Geometry g = new RectangleGeometry(r);
            Brush b = new SolidColorBrush(Colors.Black);
            GeometryDrawing drawing = new GeometryDrawing(b, new Pen(), g);
            DrawingImage drawingImage = new DrawingImage(drawing);
            Image image = new Image();
            image.Source = drawingImage;
            var anim = new ObjectAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible));
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Hidden));
            anim.RepeatBehavior = RepeatBehavior.Forever;
            anim.Duration = TimeSpan.FromMilliseconds(300);
            anim.BeginTime = TimeSpan.Zero;
            image.BeginAnimation(Image.VisibilityProperty, anim);

            return image;
        }
        private void AddLineAdornment(ITextViewLine line)
        {
            UIElement newAdornment = CreateVisualsInternal(line);
            if (newAdornment != null)
            {
                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, LineSpan(line), null, newAdornment, null);
            }
        }
        void OnCaretPositionChangedInternal(object sender, CaretPositionChangedEventArgs e)
        {
            ITextViewLine newLine = GetLineByPos(e.NewPosition);
            ITextViewLine oldLine = GetLineByPos(e.OldPosition);

            if (oldLine != newLine)
            {
                // Fisheye effect for current line
                _layer.RemoveAdornmentsByVisualSpan(LineSpan(oldLine));
                _layer.RemoveAdornmentsByVisualSpan(LineSpan(newLine));
                AddLineAdornment(newLine);
                AddLineAdornment(oldLine);
            }

            _layer.RemoveAdornment(cursor);
            if (GetLineType(newLine) != LineType.Default)
            {
                var span = new SnapshotSpan(_view.TextSnapshot, e.NewPosition.BufferPosition, 1);
                Geometry g = _view.TextViewLines.GetMarkerGeometry(span);
                Canvas.SetLeft(cursor, g.Bounds.Left);
                Canvas.SetTop(cursor, g.Bounds.Top - normalLineHeight/2 + 2.0 /*fudge it yeeeeeah*/);
                try
                {
                    _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, cursor, null);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    // TODO: End of document makes span invalid. What we really want is a length
                    // of zero, but AddAdornment pukes when we try that...
                }
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

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SmallBracesAdornmentFactory : IWpfTextViewCreationListener
    {
        /// <summary>
        /// Defines the adornment layer for the adornment. This layer is ordered 
        /// after the selection layer in the Z-order
        /// </summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("SmallBracesAdornment")]
        [Order(After = PredefinedAdornmentLayers.Selection, Before = PredefinedAdornmentLayers.Text)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
        public AdornmentLayerDefinition editorAdornmentLayer = null;

        public void TextViewCreated(IWpfTextView textView)
        {
            SmallBraces.Create(textView);
        }
    }

    [Export(typeof(ILineTransformSourceProvider)), ContentType("text"), TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SmallBracesFactory : ILineTransformSourceProvider
    {
        ILineTransformSource ILineTransformSourceProvider.Create(IWpfTextView textView)
        {
            return SmallBraces.Create(textView);
        }
    }
}