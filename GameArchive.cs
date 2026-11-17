using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace XadrezApp
{
    public sealed record ArchivedGame(Guid Id, string White, string Black, string Event,
        DateTimeOffset StartedAt, string Result, string Opening, string Pgn, MoveEvaluationRecord[] Analysis)
    {
        public string ToHtml()
        {
            static string H(string value) => WebUtility.HtmlEncode(value);
            var html = new StringBuilder("""
                <!doctype html><html lang="pt-BR"><meta charset="utf-8">
                <title>Relatório de partida de xadrez</title>
                <style>body{font:16px system-ui;max-width:72rem;margin:2rem auto;padding:0 1rem}
                td,th{padding:.5rem;border-bottom:1px solid #ddd;text-align:left}pre{white-space:pre-wrap}</style>
                <h1>Relatório de partida</h1>
                """);
            html.Append($"<p>{H(White)} × {H(Black)} · {H(Result)}</p>")
                .Append($"<p>{H(Event)} · {StartedAt:dd/MM/yyyy HH:mm zzz} · {H(Opening)}</p>");
            if (Analysis.Length == 0)
                html.Append("<p>Sem lances avaliados pela IA.</p>");
            else
            {
                html.Append($"<p>Precisão média das brancas: {Analysis.Average(x => x.Accuracy):0.0}%</p>");
                html.Append("<table><thead><tr><th>Lance</th><th>Classificação</th><th>Antes → depois</th><th>Precisão</th><th>Alternativa</th><th>Comentário</th></tr></thead><tbody>");
                foreach (var move in Analysis)
                    html.Append($"<tr><td>{move.MoveNumber}. {H(move.WhiteSan)}</td><td>{H(move.Description)}</td>")
                        .Append($"<td>{move.EvalBefore:+0.00;-0.00;0.00} → {move.EvalAfter:+0.00;-0.00;0.00}</td>")
                        .Append($"<td>{move.Accuracy:0.0}%</td><td>{H(move.BestAlternative)}</td><td>{H(move.CoachNote)}</td></tr>");
                html.Append("</tbody></table>");
            }
            return html.Append($"<h2>PGN comentado</h2><pre>{H(Pgn)}</pre></html>").ToString();
        }
    }

    public sealed class GameArchive
    {
        internal static string DataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Xadrez");
        private readonly string directory;

        public GameArchive(string? directory = null)
        {
            this.directory = directory ?? Path.Combine(DataFolder, "Partidas");
        }

        public static ArchivedGame Capture(Guid id, ChessRules rules, DateTimeOffset startedAt,
            string white, string black, string gameEvent, IEnumerable<MoveEvaluationRecord> evaluations)
        {
            var analysis = evaluations.ToArray();
            var byNumber = analysis.ToLookup(move => move.MoveNumber);
            var comments = new Dictionary<int, string>();
            for (int ply = 0; ply < rules.SanMoves.Count; ply++)
            {
                int halfMove = ply + (rules.InitialWhiteTurn ? 0 : 1);
                if (halfMove % 2 != 0) continue;
                int number = rules.InitialMoveNumber + halfMove / 2;
                var entry = byNumber[number].LastOrDefault(move => move.WhiteSan == rules.SanMoves[ply]);
                if (entry == null) continue;
                comments[ply] = FormattableString.Invariant(
                    $"[%eval {entry.EvalAfter:0.00}] {entry.Description}. Precisão: {entry.Accuracy:0.0}%. Alternativa: {entry.BestAlternative}. {entry.CoachNote}");
            }

            return new ArchivedGame(id, white, black, gameEvent, startedAt, rules.GameResult,
                rules.InitialFen == ChessRules.StandardStartFen ? StudyLibrary.DetectOpening(rules.SanMoves) : "Posição personalizada",
                rules.ToPgn(blackPlayerName: black, eventName: gameEvent, whitePlayerName: white,
                    playedAt: startedAt, comments: comments), analysis);
        }

        public void Save(ArchivedGame game) => SaveJson(directory, game.Id, game);

        public IReadOnlyList<ArchivedGame> ReadAll() => ReadJson<ArchivedGame>(directory)
            .OrderByDescending(game => game.StartedAt).ToArray();

        public static IEnumerable<ArchivedGame> Filter(IEnumerable<ArchivedGame> games, string query, string? opening)
        {
            var compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
            return games.Where(game => (string.IsNullOrEmpty(opening) || game.Opening == opening) &&
                new[] { game.White, game.Black, game.Event, game.Opening, game.Result, game.StartedAt.ToString("dd/MM/yyyy") }
                    .Any(field => compare.IndexOf(field, query.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
        }

        internal static void SaveJson<T>(string folder, Guid id, T value)
        {
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, $"{id:N}.json");
            string temp = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(value));
                File.Move(temp, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        internal static IEnumerable<T> ReadJson<T>(string folder) where T : class
        {
            if (!Directory.Exists(folder)) yield break;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                T? value;
                try { value = JsonSerializer.Deserialize<T>(File.ReadAllText(file)); }
                catch (JsonException ex)
                {
                    throw new IOException($"Arquivo inválido: {Path.GetFileName(file)}", ex);
                }
                if (value != null) yield return value;
            }
        }
    }
}
