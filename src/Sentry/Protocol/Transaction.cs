using System.Runtime.Serialization;

namespace Sentry.Protocol
{
    [DataContract]
    public class Transaction : Span
    {
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string? Name { get; set; }
    }
}
