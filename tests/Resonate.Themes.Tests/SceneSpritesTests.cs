namespace Resonate.Themes.Tests;

/// <summary>The special looks' little pictures: petals, blossoms, crystals and glints.</summary>
public sealed class SceneSpritesTests
{
    [Theory]
    [InlineData(SceneSprite.PetalPink)]
    [InlineData(SceneSprite.PetalDeep)]
    [InlineData(SceneSprite.PetalPale)]
    [InlineData(SceneSprite.PetalWhite)]
    [InlineData(SceneSprite.Blossom)]
    [InlineData(SceneSprite.Crystal)]
    [InlineData(SceneSprite.CrystalPlate)]
    [InlineData(SceneSprite.Glint)]
    public void Each_picture_is_a_centred_shape_on_clear_corners(SceneSprite sprite)
    {
        foreach (var size in (int[])[24, 48, SceneSprites.Size])
        {
            var pixels = SceneSprites.Pixels(sprite, size);
            Assert.Equal(size * size * 4, pixels.Length);
            byte Alpha(int x, int y) => pixels[(((y * size) + x) * 4) + 3];

            // Nothing in the corners; something solid in the middle (a glint is only fine rays and a small heart).
            Assert.Equal(0, Alpha(0, 0));
            Assert.Equal(0, Alpha(size - 1, 0));
            Assert.Equal(0, Alpha(0, size - 1));
            Assert.Equal(0, Alpha(size - 1, size - 1));
            var solid = sprite == SceneSprite.Glint
                ? Enumerable.Range(0, size * size).Count(i => pixels[(i * 4) + 3] > 100) > size * size / 250
                : Enumerable.Range(0, size * size).Count(i => pixels[(i * 4) + 3] > 200) > size * size / 40;
            Assert.True(solid);
        }

        Assert.Equal(SceneSprites.Pixels(sprite), SceneSprites.Pixels(sprite));
        var png = SceneSprites.Png(sprite, 24);
        Assert.Equal((byte[])[0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [Fact]
    public void A_petal_is_narrow_at_its_base_widest_past_its_middle_and_notched_at_its_tip()
    {
        Assert.True(SceneSprites.PetalHalfWidth(0.02) < SceneSprites.PetalHalfWidth(0.3));
        Assert.True(SceneSprites.PetalHalfWidth(0.3) < SceneSprites.PetalHalfWidth(0.64));
        Assert.True(SceneSprites.PetalHalfWidth(0.95) < SceneSprites.PetalHalfWidth(0.64));
        Assert.Equal(0, SceneSprites.PetalHalfWidth(1.1));
        Assert.False(SceneSprites.InPetal(0, 0.99));
        Assert.True(SceneSprites.InPetal(0.3, 0.92));
        Assert.True(SceneSprites.InPetal(0, 0.5));
    }
}
