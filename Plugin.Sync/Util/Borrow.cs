using System;

namespace Plugin.Sync.Util
{
    /// <summary>
    /// Represents concept of temporarily taken ownership of locked data.
    /// Until disposed, no external changes are permitted for this data.
    /// </summary>
    public class Borrow<T> : IDisposable
    {
        private readonly Action free;
        public T Value { get; }

        public Borrow(T value, Action free)
        {
            this.free = free;
            this.Value = value;
        }

        public void Dispose()
        {
            this.free();
            GC.SuppressFinalize(this);
        }

        ~Borrow() => this.free();
    }
}
