#nullable enable
using System.Collections;
using System.Collections.Generic;

namespace Feeder.Json
{
    public readonly partial struct JsonElement
    {
        public struct ArrayEnumerator : IEnumerable<JsonElement>, IEnumerator<JsonElement>
        {
            readonly JsonElement target;
            readonly int end;
            int current;

            internal ArrayEnumerator(JsonElement target)
            {
                this.target = target;
                end = target.Parent.GetEndIndex(target.idx, false);
                current = -1;
            }

            public JsonElement Current => current < 0 ? default : new JsonElement(target.Parent, current);

            object IEnumerator.Current => Current;

            public ArrayEnumerator GetEnumerator()
            {
                ArrayEnumerator copy = this;
                copy.current = -1;
                return copy;
            }

            IEnumerator<JsonElement> IEnumerable<JsonElement>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public bool MoveNext()
            {
                if (current >= end)
                    return false;
                current = current < 0 ? target.idx + 1 : target.Parent.GetEndIndex(current, true);
                return current < end;
            }

            public void Reset() => current = -1;

            public void Dispose() => current = end;
        }
    }
}
