using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Sentry.Protocol
{
    [DataContract]
    public class Span
    {
        [DataMember(Name = "parent_span_id", EmitDefaultValue = false)]
        public SentryId? ParentSpanId { get; set; }

        [DataMember(Name = "span_id", EmitDefaultValue = false)]
        public SentryId SpanId { get; set; }

        [DataMember(Name = "trace_id", EmitDefaultValue = false)]
        public SentryId TraceId { get; set; }

        [DataMember(Name = "start_timestamp", EmitDefaultValue = false)]
        public DateTimeOffset StartTimestamp { get; set; }

        [DataMember(Name = "timestamp", EmitDefaultValue = false)]
        public DateTimeOffset EndTimestamp { get; set; }

        [DataMember(Name = "op", EmitDefaultValue = false)]
        public string? Operation { get; set; }

        [DataMember(Name = "description", EmitDefaultValue = false)]
        public string? Description { get; set; }

        [DataMember(Name = "status", EmitDefaultValue = false)]
        public string? Status { get; set; }

        [DataMember(Name = "sampled", EmitDefaultValue = false)]
        public bool IsSampled { get; set; }

        [DataMember(Name = "tags", EmitDefaultValue = false)]
        public IReadOnlyDictionary<string, string> Tags { get; } = new ConcurrentDictionary<string, string>();

        [DataMember(Name = "data", EmitDefaultValue = false)]
        public IReadOnlyDictionary<string, object> Data { get; } = new ConcurrentDictionary<string, object>();

        public Span StartChild() => new Span
        {
            ParentSpanId = SpanId
        };
    }
}
