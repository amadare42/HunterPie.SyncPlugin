namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Used to define how to encode data that will be sent over WebSockets
    /// </summary>
    public interface IMessageEncoder
    {
        (char[] buffer, int length) EncodeMessage(object data);
    }
}