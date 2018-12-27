namespace Oxide.Ext.Discord.Gateway
{
    using Newtonsoft.Json;

    public class SPayload<T>
    {
        [JsonProperty("op")]
        public OpCode OpCode { get; set; }

        [JsonProperty("d")]
        public T Data { get; set; }
    }
}
