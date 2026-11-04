using System;
using System.Threading;

namespace CustomSong
{
    internal static class MainThread
    {
        private static SynchronizationContext _context;
        private static int _thread;

        internal static void Capture()
        {
            if (_context != null) return;
            _thread = Thread.CurrentThread.ManagedThreadId;
            _context = SynchronizationContext.Current;
        }

        internal static void Run(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _thread)
            {
                action();
                return;
            }

            Post(action);
        }

        internal static void Post(Action action)
        {
            SynchronizationContext context = _context;
            if (context == null) return;

            context.Post(_ =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    ModLog.Warn("a hand-off to the main thread failed: " + ModLog.Describe(e));
                }
            }, null);
        }

        internal static void DecodeAndBuild(Func<Png.Image> decode, Action<Png.Image> build)
        {
            if (_context == null) return;
            Png.Image image = decode();
            if (image == null) return;

            using (var done = new System.Threading.ManualResetEventSlim(false))
            {
                Post(() =>
                {
                    try
                    {
                        build(image);
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                done.Wait(TimeSpan.FromSeconds(10));
            }
        }
    }
}
