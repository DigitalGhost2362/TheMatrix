#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Feeder.MCP.Editor.Query
{
    internal enum TokenKind { End, Identifier, Number, String, Meta, Pipe, Comma, Dot, LParen, RParen, LBracket, RBracket, Operator }

    internal readonly struct Token
    {
        public readonly TokenKind Kind;
        public readonly string Text;
        public readonly object? Value;
        public readonly int Position;

        public Token(TokenKind kind, string text, object? value, int position)
        {
            Kind = kind;
            Text = text;
            Value = value;
            Position = position;
        }

        public bool IsWord(string word) => Kind == TokenKind.Identifier && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);
        public bool IsOperator(string op) => Kind == TokenKind.Operator && Text == op;

        public override string ToString() => Kind == TokenKind.End ? "end of query" : $"'{Text}'";
    }

    public sealed class QueryException : Exception
    {
        public int Position { get; }

        public QueryException(string message, int position = -1) : base(message) => Position = position;

        public string Describe(string? source)
        {
            if (Position < 0 || string.IsNullOrEmpty(source))
                return Message;
            var at = Math.Min(Position, source!.Length);
            var lineStart = source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
            var lineEnd = source.IndexOf('\n', at);
            var line = source.Substring(lineStart, (lineEnd < 0 ? source.Length : lineEnd) - lineStart);
            return $"{Message} (at {at})\n{line}\n{new string(' ', at - lineStart)}^";
        }
    }

    internal static class QueryLexer
    {
        static readonly string[] Operators = { "==", "!=", "<=", ">=", "&&", "||", "??", "<", ">", "=", "!", "+", "-", "*", "/", "%" };

        public static List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < source.Length)
            {
                var c = source[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                var start = i;
                if (char.IsLetter(c) || c == '_')
                {
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                        i++;
                    var word = source.Substring(start, i - start);
                    tokens.Add(new Token(TokenKind.Identifier, word, null, start));
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
                {
                    tokens.Add(ReadNumber(source, ref i));
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    tokens.Add(ReadString(source, ref i));
                    continue;
                }
                if (c == '$')
                {
                    i++;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                        i++;
                    var name = source.Substring(start + 1, i - start - 1);
                    tokens.Add(new Token(TokenKind.Meta, "$" + name, name, start));
                    continue;
                }
                switch (c)
                {
                    case ',': tokens.Add(new Token(TokenKind.Comma, ",", null, i++)); continue;
                    case '.': tokens.Add(new Token(TokenKind.Dot, ".", null, i++)); continue;
                    case '(': tokens.Add(new Token(TokenKind.LParen, "(", null, i++)); continue;
                    case ')': tokens.Add(new Token(TokenKind.RParen, ")", null, i++)); continue;
                    case '[': tokens.Add(new Token(TokenKind.LBracket, "[", null, i++)); continue;
                    case ']': tokens.Add(new Token(TokenKind.RBracket, "]", null, i++)); continue;
                }
                if (c == '|' && !(i + 1 < source.Length && source[i + 1] == '|'))
                {
                    tokens.Add(new Token(TokenKind.Pipe, "|", null, i++));
                    continue;
                }
                var op = MatchOperator(source, i);
                if (op == null)
                    throw new QueryException($"Unexpected character '{c}'", i);
                tokens.Add(new Token(TokenKind.Operator, op, null, i));
                i += op.Length;
            }
            tokens.Add(new Token(TokenKind.End, string.Empty, null, source.Length));
            return tokens;
        }

        static string? MatchOperator(string source, int i)
        {
            foreach (var op in Operators)
                if (string.CompareOrdinal(source, i, op, 0, op.Length) == 0)
                    return op;
            return null;
        }

        static Token ReadNumber(string source, ref int i)
        {
            var start = i;
            var isFloat = false;
            while (i < source.Length && char.IsDigit(source[i]))
                i++;
            if (i < source.Length && source[i] == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
            {
                isFloat = true;
                i++;
                while (i < source.Length && char.IsDigit(source[i]))
                    i++;
            }
            if (i < source.Length && (source[i] == 'e' || source[i] == 'E'))
            {
                var save = i;
                i++;
                if (i < source.Length && (source[i] == '+' || source[i] == '-'))
                    i++;
                if (i < source.Length && char.IsDigit(source[i]))
                {
                    isFloat = true;
                    while (i < source.Length && char.IsDigit(source[i]))
                        i++;
                }
                else
                    i = save;
            }
            var text = source.Substring(start, i - start);
            if (i < source.Length && (source[i] == 'f' || source[i] == 'F'))
            {
                isFloat = true;
                i++;
            }
            object value;
            if (!isFloat && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                value = whole;
            else
                value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return new Token(TokenKind.Number, text, value, start);
        }

        static Token ReadString(string source, ref int i)
        {
            var quote = source[i];
            var start = i++;
            var sb = new StringBuilder();
            while (i < source.Length && source[i] != quote)
            {
                var c = source[i++];
                if (c != '\\' || i >= source.Length)
                {
                    sb.Append(c);
                    continue;
                }
                var e = source[i++];
                sb.Append(e switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '0' => '\0',
                    _ => e,
                });
            }
            if (i >= source.Length)
                throw new QueryException("Unterminated string", start);
            i++;
            return new Token(TokenKind.String, source.Substring(start, i - start), sb.ToString(), start);
        }
    }
}
