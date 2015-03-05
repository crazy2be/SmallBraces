using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.TextFormatting;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Outlining;
using Microsoft.VisualStudio.Utilities;

namespace SmallBraces
{
    public class SmallBraces
    {
        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;

        public SmallBraces(IWpfTextView view)
        {
            _view = view;
            _layer = view.GetAdornmentLayer("SmallBracesAdornment");
            Debug.Assert(_layer != null);
        }
        private TextRunProperties _textProps {
            get { return _view.FormattedLineSource != null ? _view.FormattedLineSource.DefaultTextProperties : null; }
        }
        private double _normalLineHeight { get { return _textProps != null ? _textProps.FontRenderingEmSize : 13; } }
        private Image makeCursor()
        {
            // Ghetto cursor
            Debug.Assert(_normalLineHeight > 0.0);
            Rect r = new Rect(0, 0, 1, _normalLineHeight);
            Geometry g = new RectangleGeometry(r);

            Brush b = _textProps.ForegroundBrush.Clone();
            GeometryDrawing drawing = new GeometryDrawing(b, new Pen(), g);
            DrawingImage drawingImage = new DrawingImage(drawing);
            Image image = new Image();
            image.Source = drawingImage;
            
            var anim2 = new DoubleAnimationUsingKeyFrames();
            anim2.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromPercent(0.5)));
            anim2.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)));
            anim2.RepeatBehavior = RepeatBehavior.Forever;
            anim2.BeginTime = TimeSpan.Zero;
            // http://www.windowsnetworking.com/kbase/WindowsTips/WindowsNT/RegistryTips/Miscellaneous/RegistryTipCursorBlinkRate.html
            anim2.Duration = new Duration(TimeSpan.FromMilliseconds(530*2));
            b.BeginAnimation(Brush.OpacityProperty, anim2);

            return image;
        }

        private enum LineType
        {
            JustWhitespace,
            JustCruft,
            JustBraces,
            Default
        };
        private LineType GetLineStringType(string line)
        {
            line = line.Trim();
            if (line.Equals("")) return LineType.JustWhitespace;
            if (line.Equals("/// <summary>") || line.Equals("/// </summary>")
                || line.StartsWith("#region") || line.StartsWith("#endregion")
                || line.Equals("/// <remarks>") || line.Equals("/// </remarks>")
                || line.Equals("[Pure]") || line.Equals("break;"))
                    return LineType.JustCruft;
            if (Regex.IsMatch(line, @"^[(){};,\s]+$")) return LineType.JustBraces;
            return LineType.Default;
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
        private LineType GetLineType(ITextViewLine line)
        {
            if (line.Length > 100) return LineType.Default;
            return GetLineStringType(LineToString(line));
        }
        public LineTransform GetLineTransform(ITextViewLine line)
        {
            switch (GetLineType(line))
            {
                case LineType.JustWhitespace:
                    return new LineTransform(0.5);
                case LineType.JustCruft:
                case LineType.JustBraces:
                    return new LineTransform(0.0000000001);
                case LineType.Default:
                    return new LineTransform(1.0);
            }
            throw new SystemException("Should not get here");
        }

        private ITextViewLine GetLineByPos(CaretPosition pos)
        {
            return _view.GetTextViewLineContainingBufferPosition(pos.BufferPosition);
        }
        private SnapshotSpan LineSpan(ITextViewLine line)
        {
            var start = line.Start.Position;
            while (start < line.End.Position && Char.IsWhiteSpace(line.Snapshot[start])) start++;
            return new SnapshotSpan(_view.TextSnapshot, Span.FromBounds(start, line.End.Position));
        }

        private UIElement GetVisuals(ITextViewLine line)
        {
            var h = _normalLineHeight/2;
            if (line.ContainsBufferPosition(_view.Selection.ActivePoint.Position))
            {
                h = _normalLineHeight;
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
            textblock.FontWeight = _textProps.Typeface.Weight;
            textblock.FontFamily = _textProps.Typeface.FontFamily;
            textblock.Foreground = _textProps.ForegroundBrush.Clone();
            if (h != _normalLineHeight) textblock.Foreground.Opacity = 0.5;
            var t = new TranslateTransform(g.Bounds.Left, g.Bounds.Top);
            textblock.LayoutTransform = t;
            textblock.RenderTransform = t;
            return textblock;
        }
        public void CreateVisuals(ITextViewLine line)
        {
            var element = GetVisuals(line);
            if (element != null)
            {
                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, LineSpan(line), null, element, null);
            }
        }

        private Image _cursor;
        public void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            ITextViewLine newLine = GetLineByPos(e.NewPosition);
            ITextViewLine oldLine = GetLineByPos(e.OldPosition);

            if (oldLine != newLine)
            {
                // Fisheye effect for current line
                _layer.RemoveAdornmentsByVisualSpan(LineSpan(oldLine));
                _layer.RemoveAdornmentsByVisualSpan(LineSpan(newLine));
                CreateVisuals(newLine);
                CreateVisuals(oldLine);
            }

            if (_cursor != null) _layer.RemoveAdornment(_cursor);
            // We remake this each time because it seems fast enough, and it lets us have the cursor
            // not blink when it is moving (just like the real visual studio!)
            _cursor = makeCursor();
            if (GetLineType(newLine) != LineType.Default)
            {
                SnapshotSpan span;
                try
                {
                    span = new SnapshotSpan(_view.TextSnapshot, e.NewPosition.BufferPosition, 1);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    // TODO: End of document makes span invalid. What we really want is a length
                    // of zero, but GetMarkerGeometry doesn't like that (gives us a null rectangle)
                    return;
                }
                Geometry g = _view.TextViewLines.GetMarkerGeometry(span);
                Canvas.SetLeft(_cursor, g.Bounds.Left);
                Canvas.SetTop(_cursor, g.Bounds.Top - _normalLineHeight/2 + 2.0 /*fudge it yeeeeeah*/);
                
                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, _cursor, null);
            }
        }
    }

    // Visual studio doesn't give us an easy way to debug exceptions when the happen, just
    // displaying a message box that says "an exception has occurred...". Thus, we make this
    // wrapper class so that we can catch all the exceptions we might throw and debug them
    // properly.
    public class SmallBracesWrapper : ILineTransformSource
    {
        private readonly SmallBraces _wrapped;
        private SmallBracesWrapper(IWpfTextView view)
        {
            try
            {
                _wrapped = new SmallBraces(view);
                view.LayoutChanged += OnLayoutChanged;
                view.Caret.PositionChanged += OnCaretPositionChanged;
            }
            catch (Exception ex)
            {
                Debugger.Break();
            }
        }
        void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            foreach (ITextViewLine line in e.NewOrReformattedLines)
            {
                CreateVisuals(line);
            }
        }
        public static SmallBracesWrapper Create(IWpfTextView view)
        {
            return view.Properties.GetOrCreateSingletonProperty(() => new SmallBracesWrapper(view));
        }
        public void CreateVisuals(ITextViewLine line)
        {
            try
            {
                _wrapped.CreateVisuals(line);
            }
            catch (Exception ex)
            {
                Debugger.Break();
            }
        }

        public void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            try
            {
                _wrapped.OnCaretPositionChanged(sender, e);
            }
            catch (Exception ex)
            {
                Debugger.Break();
            }
        }
        public LineTransform GetLineTransform(ITextViewLine line, double yPosition, ViewRelativePosition placement)
        {
            try
            {
                return _wrapped.GetLineTransform(line);
            }
            catch (Exception ex)
            {
                Debugger.Break();
                return new LineTransform(1.0);
            }
        }
    }

    // Boilerplate to register us with Visual Studio
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
            SmallBracesWrapper.Create(textView);
        }
    }

    [Export(typeof(ILineTransformSourceProvider)), ContentType("text"), TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SmallBracesFactory : ILineTransformSourceProvider
    {
        ILineTransformSource ILineTransformSourceProvider.Create(IWpfTextView textView)
        {
            return SmallBracesWrapper.Create(textView);
        }
    }


    //////////////////////////////
    // Code to disable regions (they break our other code, and also just suck in general).
    // Inspired by https://visualstudiogallery.msdn.microsoft.com/0ca60d35-1e02-43b7-bf59-ac7deb9afbca
    public class TextViewHandler
    {
        private IWpfTextView _textView;
        private IOutliningManager _outliningManager;

        public TextViewHandler(IWpfTextView textView, IOutliningManagerService outliningManagerService)
        {
            _textView = textView;
            _outliningManager = outliningManagerService.GetOutliningManager(textView);
            if (_outliningManager == null)
                return;
            _textView.Closed += TextViewClosed;
            _outliningManager.RegionsCollapsed += ClassifierProviderRegionsCollapsed;
        }

        private void TextViewClosed(object sender, EventArgs e)
        {
            if (_textView != null)
                _textView.Closed -= TextViewClosed;
            if (_outliningManager != null)
                _outliningManager.RegionsCollapsed -= ClassifierProviderRegionsCollapsed;
            _outliningManager = null;
            _textView = null;
        }

        private void ClassifierProviderRegionsCollapsed(object sender, RegionsCollapsedEventArgs e)
        {
            foreach (ICollapsed collapsible in e.CollapsedRegions)
            {
                if (collapsible.Extent.TextBuffer != _textView.TextBuffer) continue;
                if (!collapsible.IsCollapsed) continue;

                string text = collapsible.Extent.GetText(collapsible.Extent.TextBuffer.CurrentSnapshot);
                if (!text.TrimStart().ToLower().StartsWith("#region")) continue;
                try
                {
                    ((IOutliningManager)sender).Expand(collapsible);
                }
                catch (InvalidOperationException ex) {}
            }

            //((IOutliningManager)sender).RegionsCollapsed -= new EventHandler<RegionsCollapsedEventArgs>(this.ClassifierProviderRegionsCollapsed);
        }
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("CSharp"), ContentType("Basic")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal class TestViewCreationListener : IWpfTextViewCreationListener
    {
        [Import(typeof(IOutliningManagerService))]
        public IOutliningManagerService OutliningManagerService { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView == null || OutliningManagerService == null)
                return;
            TextViewHandler textViewHandler = new TextViewHandler(textView, OutliningManagerService);
        }
    }
}