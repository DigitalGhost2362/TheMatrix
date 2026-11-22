#nullable enable
using System;
using System.Collections.Generic;

namespace Feeder.Json.Serialization.Metadata
{
    public static class JsonTypeInfoResolver
    {
        public static IJsonTypeInfoResolver Combine(params IJsonTypeInfoResolver?[] resolvers)
        {
            if (resolvers == null)
                throw new ArgumentNullException(nameof(resolvers));
            var chain = new List<IJsonTypeInfoResolver>();
            foreach (IJsonTypeInfoResolver? resolver in resolvers)
            {
                if (resolver != null)
                    chain.Add(resolver);
            }
            return chain.Count == 1 ? chain[0] : new Chain(chain.ToArray());
        }

        sealed class Chain : IJsonTypeInfoResolver
        {
            readonly IJsonTypeInfoResolver[] resolvers;

            internal Chain(IJsonTypeInfoResolver[] resolvers)
            {
                this.resolvers = resolvers;
            }

            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            {
                foreach (IJsonTypeInfoResolver resolver in resolvers)
                {
                    JsonTypeInfo? info = resolver.GetTypeInfo(type, options);
                    if (info != null)
                        return info;
                }
                return null;
            }
        }
    }
}
