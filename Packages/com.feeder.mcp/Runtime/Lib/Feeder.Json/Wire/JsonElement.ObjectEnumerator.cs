#nullable enable
using System.Collections;
using System.Collections.Generic;

namespace Feeder.Json
{
    public readonly partial struct JsonElement
    {
        public struct ObjectEnumerator : IEnumerable<JsonProperty>, IEnumerator<JsonProperty>
        {
            readonly JsonElement target;
            readonly int end;
            // index of the current property's value row
            int current;

            internal ObjectEnumerator(JsonElement target)
            {
                this.target = target;
                end = target.Parent.GetEndIndex(target.idx, false);
                current = -1;
            }

            public JsonProperty Current => current < 0 ? default : new JsonProperty(new JsonElement(target.Parent, current));

            object IEnumerator.Current => Current;

            public ObjectEnumerator GetEnumerator()
            {
                ObjectEnumerator copy = this;
                copy.current = -1;
                return copy;
            }

            IEnumerator<JsonProperty> IEnumerable<JsonProperty>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public bool MoveNext()
            {
                if (current >= end)
                    return false;
                current = (current < 0 ? target.idx + 1 : target.Parent.GetEndIndex(current, true)) + 1;
                return current < end;
            }

            public void Reset() => current = -1;

            public void Dispose() => current = end;
        }
    }
}
