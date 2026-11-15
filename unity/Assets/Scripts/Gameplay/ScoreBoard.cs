using System;

namespace FlightRecord.Gameplay
{
    // The run's score and the saved high score (backlog 4.14, D-013). Engine-free so the
    // rules can be unit-tested in EditMode; GameControl owns one and saves its three numbers.
    //
    // The high score follows the score live, so a run that crashes or is quit mid-way still
    // keeps its best. PreviousHighScore is the high score as it stood when the run began,
    // which is what "new high score" is measured against (and is saved too, so Continue
    // still knows).
    public sealed class ScoreBoard
    {
        public int Score { get; private set; }
        public int HighScore { get; private set; }
        public int PreviousHighScore { get; private set; }

        // True once this run's score has passed the high score it started with.
        public bool IsNewHighScore => Score > PreviousHighScore;

        public ScoreBoard(int score = 0, int highScore = 0, int previousHighScore = 0)
        {
            Score = Math.Max(0, score);
            HighScore = Math.Max(Math.Max(0, highScore), Score);
            PreviousHighScore = Math.Min(Math.Max(0, previousHighScore), HighScore);
        }

        // Adds a kill's points. Zero or negative amounts are ignored; the total saturates
        // instead of overflowing.
        public void Add(int points)
        {
            if (points <= 0) return;
            Score = Score > int.MaxValue - points ? int.MaxValue : Score + points;
            if (Score > HighScore) HighScore = Score;
        }

        // The run is over: clear the score and make the current high score the one the next
        // run has to beat.
        public void EndRun()
        {
            Score = 0;
            PreviousHighScore = HighScore;
        }
    }
}
