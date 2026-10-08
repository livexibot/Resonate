using Microsoft.UI.Xaml;
using Resonate.Themes;

namespace Resonate.App.Themes;

/// <summary>Turns the theme model's spaces into XAML's.</summary>
public static class EdgeInsetsExtensions
{
    public static Thickness ToThickness(this EdgeInsets edges) => new(edges.Left, edges.Top, edges.Right, edges.Bottom);
}
