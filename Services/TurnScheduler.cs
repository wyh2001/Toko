using System.Threading.Channels;
using Toko.Models;

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
            while (!ct.IsCancellationRequested)
            {
                var room = await _queue.Reader.ReadAsync(ct);

                // Start game if waiting (controller decides when to push to queue)
                //if (room.MainSM == null) room.InitStateMachines();
                if (room.MainSM.State == RoomStatus.Waiting)
                    room.MainSM.Fire(Trigger.Start);

                if (room.MainSM.State == RoomStatus.Playing && room.SubSM.State == PlayingPhase.Collecting)
                {
                    if (DateTime.UtcNow >= room.PlayerDeadlineUtc)
                        room.CollectSM.Fire(CollectTrigger.PlayerTimeout);
                }

                if (room.MainSM.State != RoomStatus.Finished)
                    await _queue.Writer.WriteAsync(room, ct);
                else
                    _log.LogInformation("Room {Id} finished", room.Id);
            }
        }
    }
}
