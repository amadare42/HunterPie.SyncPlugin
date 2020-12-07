namespace Plugin.Sync.Connectivity.Model
{
    public class SetNameMessage : IMessage
    {
        public string Type { get; } = MessageCodes.SetName;
        public string Name { get; set; }

        public SetNameMessage()
        {
        }
        
        public SetNameMessage(string name)
        {
            this.Name = name;
        }

    }
}