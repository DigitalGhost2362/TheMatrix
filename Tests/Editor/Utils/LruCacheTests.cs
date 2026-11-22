#nullable enable

using System;
using System.Collections.Generic;
using Feeder.Reflect.Utils;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class LruCacheTests
    {
        [Test]
        public void Full_EvictsTheLeastRecentlyUsedEntry()
        {
            var cache = new LruCache<string, int>(2);
            cache["a"] = 1;
            cache["b"] = 2;
            Assert.IsTrue(cache.TryGetValue("a", out _));

            cache["c"] = 3;

            Assert.IsTrue(cache.ContainsKey("a"));
            Assert.IsFalse(cache.ContainsKey("b"));
            Assert.IsTrue(cache.ContainsKey("c"));
            Assert.AreEqual(2, cache.Count);
        }

        [Test]
        public void GetOrAdd_CallsTheFactoryOncePerKey()
        {
            var cache = new LruCache<int, string>(4);
            var calls = 0;

            string Factory(int key)
            {
                calls++;
                return "v" + key;
            }

            Assert.AreEqual("v7", cache.GetOrAdd(7, Factory));
            Assert.AreEqual("v7", cache.GetOrAdd(7, Factory));
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void GetOrAdd_CountsAsAUse()
        {
            var cache = new LruCache<int, int>(2);
            cache[1] = 10;
            cache[2] = 20;
            cache.GetOrAdd(1, _ => -1);

            cache[3] = 30;

            Assert.IsTrue(cache.ContainsKey(1));
            Assert.IsFalse(cache.ContainsKey(2));
        }

        [Test]
        public void Setter_OverwritesWithoutGrowing()
        {
            var cache = new LruCache<string, int>(3);
            cache["k"] = 1;
            cache["k"] = 2;

            Assert.AreEqual(1, cache.Count);
            Assert.AreEqual(2, cache["k"]);
        }

        [Test]
        public void Indexer_MissingKey_Throws()
        {
            var cache = new LruCache<string, int>(1);

            Assert.Throws<KeyNotFoundException>(() => _ = cache["missing"]);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var cache = new LruCache<string, int>(2);
            cache["a"] = 1;
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
            Assert.IsFalse(cache.TryGetValue("a", out _));
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void Capacity_BelowOne_Throws(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(capacity));
        }
    }
}
