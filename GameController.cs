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

        // "Geração" da partida: incrementada a cada reinício / FEN / puzzle / fim de jogo.
        // Toda tarefa assíncrona guarda o valor no início e o confere depois de cada await;
        // se mudou, o resultado é descartado (evita o bot jogar no tabuleiro novo).
        public int GameGeneration { get; private set; } = 0;

        // "Geração" da dica/análise: incrementada a cada lance ou mudança de posição para
        // invalidar qualquer dica atrasada calculada para uma posição anterior.
        public int HintGeneration { get; private set; } = 0;

        public TacticalPuzzle? ActivePuzzle { get; set; } = null;

        public int IncrementGameGeneration() => ++GameGeneration;
        public int IncrementHintGeneration() => ++HintGeneration;

        public void InvalidateGenerations()
        {
            GameGeneration++;
            HintGeneration++;
        }

        // Zera todo o estado da partida (sem mexer no tabuleiro) e invalida tarefas assíncronas antigas.
        public void ResetGameState(AnalysisController analysis, ClockController clock, BoardView boardView, string selectedClockMode)
        {
            GameGeneration++;
            HintGeneration++;
            analysis.StopSearch();

            ActivePuzzle = null;
            History.Clear();
            analysis.ClearEvaluations();
            IsBotThinking = false;
            IsHintRunning = false;
            GameOver = false;
            clock.StopAndResetStopwatch();
            boardView.ResetAllOverlays();

            clock.ConfigureClockMode(selectedClockMode, resetRemainingTime: true);
        }

        // Encerra a partida atual (desistência, empate, tempo, mate) descartando o lance do bot em cálculo.
        public void EndCurrentGame(AnalysisController analysis, ClockController clock, BoardView boardView)
        {
            GameOver = true;
            clock.StopAndResetStopwatch();
            GameGeneration++;
            HintGeneration++;
            if (IsBotThinking || IsHintRunning)
            {
                IsBotThinking = false;
                IsHintRunning = false;
                analysis.StopSearch();
            }
            boardView.ClearActiveSelectionAndPremove();
            boardView.Invalidate();
        }

        public void CancelRunningHint(AnalysisController analysis)
        {
            HintGeneration++;
            if (IsHintRunning)
            {
                IsHintRunning = false;
                analysis.StopSearch();
            }
        }

        public bool TryTakeback(AnalysisController analysis, ClockController clock, BoardView boardView)
        {
            if (IsBotThinking || History.Count == 0) return false;

            CancelRunningHint(analysis);

            Rules.RestoreSnapshot(History.Pop());
            Rules.GameResult = "*";
            GameOver = false;
            boardView.ClearSelectionAndHint();

            analysis.RemoveEvaluationsFrom(Rules.FullMoveNumber);

            if (Rules.SanMoves.Count == 0)
                clock.StopAndResetStopwatch();

            return true;
        }

        public bool EvaluateDrawOffer()
        {
            int evalWhite = EngineService.EvaluateBoardWhiteRelative(Rules.Board);
            if (evalWhite >= -150)
            {
                Rules.GameResult = "1/2-1/2";
                return true;
            }
            return false;
        }

        // Confere se o lance devolvido pela engine é legal na posição atual antes de executar;
        // caso contrário, escolhe um lance legal alternativo das Pretas.
        public (Point from, Point to, char? promo, bool usedFallback) EnsureValidBotMove(
            (Point from, Point to)? candidateMove,
            char? candidatePromo)
        {
            if (candidateMove.HasValue &&
                Rules.GetLegalMovesForSquare(candidateMove.Value.from.X, candidateMove.Value.from.Y, false).Contains(candidateMove.Value.to) &&
                Rules.Board[candidateMove.Value.from.Y, candidateMove.Value.from.X] != '.' &&
                !char.IsUpper(Rules.Board[candidateMove.Value.from.Y, candidateMove.Value.from.X]))
            {
                return (candidateMove.Value.from, candidateMove.Value.to, candidatePromo, false);
            }

            var legal = Rules.GetAllLegalMoves(false);
            if (legal.Count == 0)
                throw new InvalidOperationException("O bot não tem lances legais nesta posição.");

            var fallback = legal[Random.Shared.Next(legal.Count)];
            return (fallback.from, fallback.to, null, true);
        }

        public string BuildPgn(bool isUsingStockfish)
        {
            string blackName = isUsingStockfish ? "Stockfish IA" : "Engine Tática Interna";
            string? evtName = ActivePuzzle != null ? $"Puzzle: {ActivePuzzle.Title}" : null;
            return Rules.ToPgn(blackPlayerName: blackName, eventName: evtName);
        }
    }
}
