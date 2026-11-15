using FlightRecord.Gameplay;
using NUnit.Framework;

namespace FlightRecord.Gameplay.Tests
{
    public class ScoreBoardTests
    {
        [Test]
        public void StartsAtZero()
        {
            var board = new ScoreBoard();
            Assert.AreEqual(0, board.Score);
            Assert.AreEqual(0, board.HighScore);
            Assert.IsFalse(board.IsNewHighScore);
        }

        [Test]
        public void AddAccumulatesPoints()
        {
            var board = new ScoreBoard();
            board.Add(100);
            board.Add(50);
            Assert.AreEqual(150, board.Score);
        }

        [Test]
        public void ZeroAndNegativePointsAreIgnored()
        {
            var board = new ScoreBoard(score: 100, highScore: 100, previousHighScore: 100);
            board.Add(0);
            board.Add(-50);
            Assert.AreEqual(100, board.Score);
        }

        [Test]
        public void HighScoreFollowsScoreOncePassed()
        {
            var board = new ScoreBoard(score: 0, highScore: 300, previousHighScore: 300);
            board.Add(200);
            Assert.AreEqual(300, board.HighScore);
            Assert.IsFalse(board.IsNewHighScore);

            board.Add(200);
            Assert.AreEqual(400, board.HighScore);
            Assert.IsTrue(board.IsNewHighScore);
        }

        [Test]
        public void TyingTheHighScoreIsNotANewHighScore()
        {
            var board = new ScoreBoard(score: 0, highScore: 300, previousHighScore: 300);
            board.Add(300);
            Assert.AreEqual(300, board.HighScore);
            Assert.IsFalse(board.IsNewHighScore);
        }

        [Test]
        public void EndRunClearsScoreAndKeepsHighScore()
        {
            var board = new ScoreBoard();
            board.Add(500);
            board.EndRun();

            Assert.AreEqual(0, board.Score);
            Assert.AreEqual(500, board.HighScore);
            Assert.AreEqual(500, board.PreviousHighScore);
            Assert.IsFalse(board.IsNewHighScore);
        }

        [Test]
        public void RestoredMidRunStillKnowsItBeatTheOldHighScore()
        {
            // As saved by a run that had passed a 300 high score before the player quit.
            var board = new ScoreBoard(score: 450, highScore: 450, previousHighScore: 300);
            Assert.IsTrue(board.IsNewHighScore);
            board.Add(50);
            Assert.AreEqual(500, board.HighScore);
        }

        [Test]
        public void RestoreRepairsInconsistentValues()
        {
            // An older save with a score but no high score yet.
            var board = new ScoreBoard(score: 200, highScore: 0, previousHighScore: 0);
            Assert.AreEqual(200, board.HighScore);

            var negative = new ScoreBoard(score: -5, highScore: -5, previousHighScore: 999);
            Assert.AreEqual(0, negative.Score);
            Assert.AreEqual(0, negative.HighScore);
            Assert.AreEqual(0, negative.PreviousHighScore);
        }

        [Test]
        public void ScoreSaturatesInsteadOfOverflowing()
        {
            var board = new ScoreBoard(score: int.MaxValue - 10, highScore: int.MaxValue - 10, previousHighScore: 0);
            board.Add(100);
            Assert.AreEqual(int.MaxValue, board.Score);
            Assert.AreEqual(int.MaxValue, board.HighScore);
        }
    }
}
