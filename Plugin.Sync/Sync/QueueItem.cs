using System.Threading;

namespace Plugin.Sync.Sync
{
    public class QueueItem
    {
        public Trigger Trigger { get; }
        public CancellationToken CancellationToken { get; }

        public QueueItem(Trigger trigger, CancellationToken cancellationToken)
        {
            this.Trigger = trigger;
            this.CancellationToken = cancellationToken;
        }

        public QueueItem(Trigger trigger)
        {
            this.Trigger = trigger;
            this.CancellationToken = CancellationToken.None;
        }
    }
}