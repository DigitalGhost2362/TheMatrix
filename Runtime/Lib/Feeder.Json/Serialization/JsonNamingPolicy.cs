namespace Feeder.Json
{
    public abstract class JsonNamingPolicy
    {
        protected JsonNamingPolicy()
        {
        }

        public static JsonNamingPolicy CamelCase { get; } = new CamelCasePolicy();

        public abstract string ConvertName(string name);

        sealed class CamelCasePolicy : JsonNamingPolicy
        {
            // lowers the leading run of capitals, keeping the last one when a lowercase letter follows ("URLValue" -> "urlValue")
            public override string ConvertName(string name)
            {
                if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
                    return name;
                char[] chars = name.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    if (i == 1 && !char.IsUpper(chars[i]))
                        break;
                    bool hasNext = i + 1 < chars.Length;
                    if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                    {
                        if (chars[i + 1] == ' ')
                            chars[i] = char.ToLowerInvariant(chars[i]);
                        break;
                    }
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }
                return new string(chars);
            }
        }
    }
}
