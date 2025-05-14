using System.Threading.Channels;
using Toko.Models;
using static Toko.Models.Room;

namespace Toko.Services
{
    public class TurnScheduler : BackgroundService
    {
        public static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(20);
        private readonly Channel<Room> _queue;
        private readonly ILogger<TurnScheduler> _log;

        public TurnScheduler(Channel<Room> queue, ILogger<TurnScheduler> log)
        {
            _queue = queue;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
        }
    }
}
