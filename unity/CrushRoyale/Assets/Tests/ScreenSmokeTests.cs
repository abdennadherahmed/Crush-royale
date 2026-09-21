using System.Collections.Generic;
using CrushRoyale.EditorTools;
using CrushRoyale.Game;
using CrushRoyale.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CrushRoyale.Tests
{
    /// <summary>
    /// Every screen is built from code, so a typo in a layout only shows up when the player opens that page.
    /// This builds each of them the way UIRoot does, with and without a profile (the offline case the owner hits
    /// whenever the free server is asleep), and fails on the first exception.
    /// </summary>
    public sealed class ScreenSmokeTests
    {
        private GameRoot _game;

        public static IEnumerable<ScreenCase> Cases => OfflineHarness.Screens();

        [SetUp]
        public void SetUp()
        {
            // Offline services log warnings and errors by design (no server); only exceptions are failures here.
            LogAssert.ignoreFailingMessages = true;
            _game = OfflineHarness.BootGame();
        }

        [TearDown]
        public void TearDown()
        {
            OfflineHarness.Shutdown(_game);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Screen_BuildsAndDestroys([ValueSource(nameof(Cases))] ScreenCase test, [Values(true, false)] bool withProfile)
        {
            OfflineHarness.SetProfile(_game, withProfile);
            Canvas canvas = _game.UI.Canvas;
            RectTransform host = UIFactory.Stretch(UIFactory.Rect(test.Name, canvas.transform));
            try
            {
                object args = test.Args != null ? test.Args(_game) : null;
                var screen = (UIScreen)host.gameObject.AddComponent(test.Type);
                screen.Setup(_game.UI, args);
                Assert.IsTrue(host.childCount > 0, test.Name + " built nothing at all.");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
