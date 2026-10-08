namespace Resonate.Themes.Tests;

public sealed class WindowShapeTests
{
    private static readonly (double Width, double Height) BigScreen = (3413, 1400);

    [Theory]
    [InlineData(1280, 820, WindowShape.Full)]
    [InlineData(3413, 1400, WindowShape.Full)]
    [InlineData(1099, 820, WindowShape.Compact)]
    [InlineData(960, 1032, WindowShape.Compact)] // half of a 1920 x 1080 screen
    [InlineData(960, 516, WindowShape.Compact)] // a quarter of it
    [InlineData(639, 900, WindowShape.Column)]
    [InlineData(853, 1392, WindowShape.Column)] // a third of a 2560 x 1440 screen
    [InlineData(1137, 1400, WindowShape.Column)] // a third of a 5120 x 2160 screen at 150 %
    [InlineData(2275, 1400, WindowShape.Full)] // two thirds of it
    [InlineData(1280, 199, WindowShape.Strip)]
    [InlineData(400, 120, WindowShape.Strip)]
    public void The_shape_follows_the_size(double width, double height, WindowShape shape) =>
        Assert.Equal(shape, WindowShapes.For(width, height));

    [Theory]
    [InlineData(WindowShape.Full)]
    [InlineData(WindowShape.Compact)]
    [InlineData(WindowShape.Column)]
    [InlineData(WindowShape.Strip)]
    public void A_shape_picked_from_the_menu_gets_a_size_of_that_shape(WindowShape shape)
    {
        var (width, height) = WindowShapes.PickedSize(shape, BigScreen);

        Assert.Equal(shape, WindowShapes.For(width, height));
        Assert.True(width >= WindowShapes.MinimumWidth && height >= WindowShapes.MinimumHeight);
    }

    [Fact]
    public void Full_goes_back_to_the_last_full_size() =>
        Assert.Equal((1600.0, 1000.0), WindowShapes.PickedSize(WindowShape.Full, BigScreen, (1600, 1000)));

    [Fact]
    public void A_picked_size_stays_on_the_screen() =>
        Assert.Equal((1024.0, 700.0), WindowShapes.PickedSize(WindowShape.Full, (1024, 700)));
}
