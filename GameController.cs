using System;
using System.Collections.Generic;
using System.Drawing;

namespace XadrezApp
{
    public class GameController
    {
        public ChessRules Rules { get; } = new ChessRules();
        public Stack<GameSnapshot> History { get; } = new Stack<GameSnapshot>();

        public bool IsBotThinking { get; set; } = false;
        public bool IsHintRunning { get; set; } = false;
        public bool GameOver { get; set; } = false;

        public int GameGeneration { get; private set; } = 0;
        public int HintGeneration { get; private set; } = 0;

        public TacticalPuzzle? ActivePuzzle { get; private set; } = null;
        public int CurrentPuzzleIndex { get; set; } = 0;

        public (Point from, Point to)? LastMove { get; set; } = null;

        public int IncrementGameGeneration() => ++GameGeneration;
        public int IncrementHintGeneration() => ++HintGeneration;

        public void ResetStateForNewGame()
        {
            GameGeneration++;
            HintGeneration++;
            ActivePuzzle = null;
            History.Clear();
            IsBotThinking = false;
            IsHintRunning = false;
            GameOver = false;
            LastMove = null;
            Rules.Reset();
        }

        public bool StartPuzzle(TacticalPuzzle puzzle, out string error)
        {
            ResetStateForNewGame();
            if (!Rules.TryLoadFen(puzzle.Fen, out error))
            {
                Rules.Reset();
                return false;
            }

            int idx = StudyLibrary.Puzzles.IndexOf(puzzle);
            if (idx >= 0) CurrentPuzzleIndex = idx;
            ActivePuzzle = puzzle;
            return true;
        }

        public void StartFromValidatedFen(string fen)
        {
            ResetStateForNewGame();
            Rules.LoadFen(fen);
        }

        public bool TryTakeback()
        {
            if (IsBotThinking || History.Count == 0) return false;
            HintGeneration++;
            IsHintRunning = false;

            Rules.RestoreSnapshot(History.Pop());
            Rules.GameResult = "*";
            GameOver = false;
            LastMove = null;
            return true;
        }

        public void MarkGameEnded()
        {
            GameOver = true;
            GameGeneration++;
            HintGeneration++;
            IsBotThinking = false;
            IsHintRunning = false;
        }

        public Point? FindKingSquare(bool whiteKing)
        {
            char target = whiteKing ? 'K' : 'k';
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (Rules.Board[r, c] == target)
                        return new Point(c, r);
            return null;
        }
    }
}