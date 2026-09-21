using System.Collections.Generic;
using System.Reflection;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;
using CrushRoyale.EditorTools;
using CrushRoyale.Game;
using CrushRoyale.Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CrushRoyale.Tests
{
    /// <summary>Rules of the match driver that only show up on a device: they cost a lost stage to discover.</summary>
    public sealed class MatchControllerTests
    {
        private GameRoot _game;

        [SetUp]
        public void SetUp() => _game = OfflineHarness.BootGame();

        [TearDown]
        public void TearDown() => OfflineHarness.Shutdown(_game);

        /// <summary>
        /// The bug this test exists for: losing a stage disabled the board (Finish) and paying for a continue never
        /// gave it back, so the continued stage could not be played at all.
        /// </summary>
        [Test]
        public void Continue_AfterALostStage_GivesTheBoardBack()
        {
            GameSession session = LostStorySession();
            var go = new GameObject("Match");
            try
            {
                BoardInput input = go.AddComponent<BoardInput>();
                MatchController controller = go.AddComponent<MatchController>();
                // Begin() needs a live board and a bound session; only the end-of-match wiring is under test here.
                SetProperty(controller, "Session", session);
                SetProperty(controller, "Input", input);

                Invoke(controller, "Finish");
                Assert.IsFalse(input.Interactable, "A finished match must lock the board.");

                Assert.IsTrue(controller.Continue(), "A lost story stage must accept a continue.");
                Assert.IsTrue(session.IsRunning, "The session must be running again after a continue.");
                Assert.IsTrue(input.Interactable, "The board must be playable again after a continue.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>A stage whose clock ran out without a single move: the state a continue is offered from.</summary>
        private GameSession LostStorySession()
        {
            var stage = new StageData
            {
                Id = 1,
                Act = 1,
                Chapter = 1,
                IndexInChapter = 1,
                TimeLimitMs = 30000,
                MoveLimit = 20,
                TargetScore = 1000000,
                TwoStarScore = 1500000,
                ThreeStarScore = 2000000,
                DifficultyPermille = 300,
                Seed = 1234
            };
            stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ReachScore, Target = stage.TargetScore });

            GameBalance balance = _game.Backend.Balance;
            SessionConfig config = SessionConfig.ForStage(stage, balance, new List<LoadoutEntry>(), League.Bronze);
            var session = new GameSession(config, balance, "test-player");
            session.Tick(session.TimeLimitMs);
            Assert.AreEqual(SessionState.Lost, session.State, "The fixture must start from a lost stage.");
            return session;
        }

        private static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name).GetSetMethod(true).Invoke(target, new[] { value });

        private static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
