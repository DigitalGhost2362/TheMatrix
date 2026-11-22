#nullable enable

using System;
using System.Collections.Generic;
using Feeder.Reflect;
using Feeder.Reflect.Utils;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class TypeUtilsTests
    {
        [TestCase(typeof(int), "System.Int32")]
        [TestCase(typeof(int?), "System.Int32")]
        [TestCase(typeof(string), "System.String")]
        [TestCase(typeof(int[]), "System.Int32-1")]
        [TestCase(typeof(int[,]), "System.Int32-2")]
        [TestCase(typeof(List<string>), "System.Collections.Generic.List(System.String)")]
        [TestCase(typeof(Dictionary<string, int>), "System.Collections.Generic.Dictionary(System.String,System.Int32)")]
        [TestCase(typeof(Environment.SpecialFolder), "System.Environment-SpecialFolder")]
        [TestCase(typeof(Vector3), "UnityEngine.Vector3")]
        public void GetTypeId_IsSchemaSafe(Type type, string expected)
        {
            Assert.AreEqual(expected, type.GetTypeId());
        }

        [TestCase(typeof(int))]
        [TestCase(typeof(int[]))]
        [TestCase(typeof(List<string>))]
        [TestCase(typeof(Dictionary<string, int>))]
        [TestCase(typeof(Environment.SpecialFolder))]
        [TestCase(typeof(Vector3))]
        [TestCase(typeof(GameObject))]
        public void GetType_ResolvesItsOwnTypeId(Type type)
        {
            Assert.AreEqual(type, TypeUtils.GetType(type.GetTypeId()));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("No.Such.Namespace.MxTestMissingType")]
        public void GetType_UnknownName_IsNull(string? name)
        {
            Assert.IsNull(TypeUtils.GetType(name));
        }

        [TestCase(typeof(List<int>), "List<Int32>")]
        [TestCase(typeof(int?), "Int32?")]
        [TestCase(typeof(int[]), "Int32[]")]
        [TestCase(typeof(Environment.SpecialFolder), "Environment+SpecialFolder")]
        [TestCase(typeof(Dictionary<string, Vector3>), "Dictionary<String, Vector3>")]
        public void GetTypeShortName_IsReadable(Type type, string expected)
        {
            Assert.AreEqual(expected, type.GetTypeShortName());
        }

        [Test]
        public void GetTypeShortName_Null_IsTheWordNull()
        {
            Assert.AreEqual("null", ((Type?)null).GetTypeShortName());
        }

        [TestCase(typeof(int), typeof(long), true)]
        [TestCase(typeof(string), typeof(object), true)]
        [TestCase(typeof(int?), typeof(int), true)]
        [TestCase(typeof(ArgumentNullException), typeof(Exception), true)]
        [TestCase(typeof(string), typeof(int), false)]
        [TestCase(typeof(object), typeof(string), false)]
        public void IsCastable(Type from, Type to, bool expected)
        {
            Assert.AreEqual(expected, TypeUtils.IsCastable(from, to));
        }

        [Test]
        public void IsCastable_NullSource_IsFalse()
        {
            Assert.IsFalse(TypeUtils.IsCastable(null, typeof(int)));
        }

        [TestCase(typeof(List<string>), typeof(string))]
        [TestCase(typeof(int[]), typeof(int))]
        [TestCase(typeof(Dictionary<string, int>), typeof(KeyValuePair<string, int>))]
        [TestCase(typeof(HashSet<Vector3>), typeof(Vector3))]
        public void GetEnumerableItemType(Type collection, Type expected)
        {
            Assert.AreEqual(expected, TypeUtils.GetEnumerableItemType(collection));
        }

        [Test]
        public void GetEnumerableItemType_NonCollection_IsNull()
        {
            Assert.IsNull(TypeUtils.GetEnumerableItemType(typeof(int)));
        }

        [TestCase(typeof(Dictionary<string, int>), true)]
        [TestCase(typeof(SortedDictionary<int, string>), true)]
        [TestCase(typeof(IDictionary<string, object>), true)]
        [TestCase(typeof(List<KeyValuePair<string, int>>), false)]
        public void IsDictionary(Type type, bool expected)
        {
            Assert.AreEqual(expected, TypeUtils.IsDictionary(type));
        }

        [Test]
        public void GetDictionaryGenericArguments_ReturnsKeyAndValueTypes()
        {
            CollectionAssert.AreEqual(new[] { typeof(int), typeof(string) }, TypeUtils.GetDictionaryGenericArguments(typeof(SortedDictionary<int, string>)));
        }

        [TestCase(typeof(int), true)]
        [TestCase(typeof(DayOfWeek), true)]
        [TestCase(typeof(string), true)]
        [TestCase(typeof(decimal), true)]
        [TestCase(typeof(Guid), true)]
        [TestCase(typeof(DateTime), true)]
        [TestCase(typeof(Vector3), false)]
        [TestCase(typeof(object), false)]
        public void IsPrimitive_CoversScalarLikeTypes(Type type, bool expected)
        {
            Assert.AreEqual(expected, TypeUtils.IsPrimitive(type));
        }

        [TestCase(typeof(object), typeof(string), 1)]
        [TestCase(typeof(Exception), typeof(ArgumentNullException), 3)]
        [TestCase(typeof(Component), typeof(BoxCollider), 2)]
        [TestCase(typeof(string), typeof(int), -1)]
        public void GetInheritanceDistance_CountsBaseTypeHops(Type baseType, Type type, int expected)
        {
            Assert.AreEqual(expected, TypeUtils.GetInheritanceDistance(baseType, type));
        }
    }
}
