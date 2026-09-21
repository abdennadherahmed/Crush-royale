using CrushRoyale.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Tests
{
    /// <summary>Legibility floor of the UI: text on a phone must never be smaller than the agreed minimum.</summary>
    public sealed class UIFactoryTests
    {
        [Test]
        public void Label_NeverFallsUnderTheMinimumFontSize(
            [Values(-20, 0, 1, 12, UIFactory.MinFontSize - 1, UIFactory.MinFontSize, Theme.SmallSize, Theme.TitleSize)] int requested)
        {
            var host = new GameObject("Host", typeof(RectTransform));
            try
            {
                Text label = UIFactory.Label(host.transform, "Crush Royale", requested);
                Assert.GreaterOrEqual(label.fontSize, UIFactory.MinFontSize, "Requested size " + requested + " went through unclamped.");
                // Best-fit shrinks the drawn text down to resizeTextMinSize, so that floor matters just as much.
                Assert.GreaterOrEqual(label.resizeTextMinSize, 14);
                Assert.AreEqual(label.fontSize, label.resizeTextMaxSize);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
