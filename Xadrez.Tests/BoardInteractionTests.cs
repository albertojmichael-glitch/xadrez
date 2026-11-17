using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;

namespace XadrezApp.Tests
{
    public class BoardInteractionTests
    {
        [Fact]
        public Task MainWindow_ConstructsOnAnStaThread() => RunSta(() =>
        {
            using var main = new MainForm();
            Assert.Contains("Chess", main.Text);
        });

        [Fact]
        public Task Drag_SelectsOriginAndDropsOnTargetExactlyOnce() => RunSta(() =>
        {
            using var view = new TestBoard();
            var events = new List<Point>();
            view.SquareLeftClicked += events.Add;
            view.CanDragPiece = square => square == new Point(4, 6);
            Point origin = Pixel(4, 6);
            Point destination = Pixel(4, 4);

            view.Press(origin);
            view.MovePointer(destination);
            view.Release(destination);

            Assert.Equal(new[] { new Point(4, 6), new Point(4, 4) }, events);
            events.Clear();
            view.Press(destination);
            view.Release(destination);
            Assert.Equal(new[] { new Point(4, 4) }, events);
        });

        [Fact]
        public Task Escape_ClearsPremoveAndAnnouncesCancellation() => RunSta(() =>
        {
            using var view = new TestBoard { QueuedPremove = (new Point(4, 6), new Point(4, 4)) };
            int cancellations = 0;
            view.PremoveCancelled += () => cancellations++;
            view.Escape();
            Assert.Null(view.QueuedPremove);
            Assert.Equal(1, cancellations);
        });

        private static Point Pixel(int col, int row) => new(
            BoardView.BoardOffset + BoardView.SquareSize * col + BoardView.SquareSize / 2,
            BoardView.BoardOffset + BoardView.SquareSize * row + BoardView.SquareSize / 2);

        private static Task RunSta(Action run)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { run(); done.SetResult(); }
                catch (Exception error) { done.SetException(error); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return done.Task;
        }

        private sealed class TestBoard : BoardView
        {
            public TestBoard() : base(new ChessRules()) { }

            public void Press(Point point) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
            public void MovePointer(Point point) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, point.X, point.Y, 0));
            public void Release(Point point) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
            public void Escape() => OnKeyDown(new KeyEventArgs(Keys.Escape));
        }
    }
}
