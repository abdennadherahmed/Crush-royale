using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Tests.Economy;

public class VipGiftTests
{
    [Fact]
    public void Gifts_StartAtVip6_AndGrowWithTheTier()
    {
        Assert.Null(VipGifts.For(5, 100));
        VipGift six = VipGifts.For(6, 100);
        VipGift ten = VipGifts.For(10, 100);
        Assert.NotNull(six);
        Assert.True(ten.Reward.Orbes > six.Reward.Orbes);
        Assert.True(ten.Reward.Coins > six.Reward.Coins);
        Assert.True(ten.PetFragments > six.PetFragments);
        Assert.True(ten.Reward.PowerUps.Values.Sum() > six.Reward.PowerUps.Values.Sum());
    }

    [Fact]
    public void Gift_IsClaimedOncePerDay()
    {
        Assert.False(VipGifts.CanClaim(5, -1, 10));
        Assert.True(VipGifts.CanClaim(6, -1, 10));
        Assert.False(VipGifts.CanClaim(6, 10, 10));
        Assert.True(VipGifts.CanClaim(6, 10, 11));
    }
}
