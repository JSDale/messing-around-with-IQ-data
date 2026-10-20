using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HackRfIqDataTest.ViewModels;

namespace HackRfIqDataTest.Views;

public partial class AnalysisView : UserControl
{
    private const double ReadoutOffset = 12;

    public AnalysisView()
    {
        InitializeComponent();
    }

    private void OnWaterfallPointerMoved(object? sender, PointerEventArgs e)
    {
        var plot = (Control)sender!;
        var position = e.GetPosition(plot);
        var text = (this.DataContext as AnalysisViewModel)?.DescribePoint(
            position.X / plot.Bounds.Width, position.Y / plot.Bounds.Height);
        if (text is null)
        {
            this.Readout.IsVisible = false;
            return;
        }

        this.ReadoutText.Text = text;
        this.Readout.IsVisible = true;

        // Sit below-right of the cursor, flipping to the other side near the right and bottom edges.
        this.Readout.Measure(Size.Infinity);
        var size = this.Readout.DesiredSize;
        var x = position.X + ReadoutOffset;
        if (x + size.Width > plot.Bounds.Width)
        {
            x = position.X - ReadoutOffset - size.Width;
        }

        var y = position.Y + ReadoutOffset;
        if (y + size.Height > plot.Bounds.Height)
        {
            y = position.Y - ReadoutOffset - size.Height;
        }

        Canvas.SetLeft(this.Readout, Math.Max(0, x));
        Canvas.SetTop(this.Readout, Math.Max(0, y));
    }

    private void OnWaterfallPointerExited(object? sender, PointerEventArgs e)
    {
        this.Readout.IsVisible = false;
    }
}
