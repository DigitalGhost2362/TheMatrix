#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Feeder.MCP.Editor.Query
{
    internal sealed class ParsedQuery
    {
        public string Text = string.Empty;
        public Eval Source = null!;
        public List<Stage> Stages = new List<Stage>();
        public bool HasMutation => Stages.Any(s => s.IsMutation);
        public bool CreatesObjects;
    }

    internal sealed class QueryParser
    {
        static readonly HashSet<string> ComparisonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "contains", "startswith", "endswith", "matches", "in" };

        static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "and", "or", "not", "is", "as", "asc", "desc", "contains", "startswith", "endswith", "matches", "in" };

        public static readonly string[] StageKeywords =
        {
            "where", "select", "sort", "take", "skip", "first", "last", "count", "reverse", "distinct", "group",
            "sum", "avg", "min", "max", "get", "components", "children", "descendants", "parent", "gameobject",
            "set", "call", "add", "destroy",
        };

        readonly string _source;
        readonly List<Token> _tokens;
        int _p;

        QueryParser(string source)
        {
            _source = source;
            _tokens = QueryLexer.Tokenize(source);
        }

        public static ParsedQuery Parse(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new QueryException("The query is empty. Start with a source such as 'objects', 'assets(\"t:Material\")' or 'find(\"Player\")'.");
            var parser = new QueryParser(source);
            var query = new ParsedQuery { Text = source };
            var sourceNode = parser.ParseExpression();
            query.Source = sourceNode.Compile();
            query.CreatesObjects = sourceNode is CallNode { Target: null } call && string.Equals(call.Name, "new", StringComparison.OrdinalIgnoreCase);
            while (parser.Peek.Kind == TokenKind.Pipe)
            {
                parser._p++;
                query.Stages.Add(parser.ParseStage());
            }
            if (parser.Peek.Kind != TokenKind.End)
                throw new QueryException($"Unexpected {parser.Peek}; stages are separated by '|'", parser.Peek.Position);
            return query;
        }

        Token Peek => _tokens[_p];
        Token PeekAt(int offset) => _tokens[Math.Min(_p + offset, _tokens.Count - 1)];
        Token Next() => _tokens[_p++];
        bool AtStageEnd => Peek.Kind == TokenKind.Pipe || Peek.Kind == TokenKind.End;

        Token Expect(TokenKind kind, string what)
        {
            if (Peek.Kind != kind)
                throw new QueryException($"Expected {what} but found {Peek}", Peek.Position);
            return Next();
        }

        #region Stages

        Stage ParseStage()
        {
            var token = Next();
            if (token.Kind != TokenKind.Identifier)
                throw new QueryException($"Expected a stage name after '|' but found {token}. Stages: {string.Join(", ", StageKeywords)}", token.Position);
            var keyword = token.Text.ToLowerInvariant();
            var stage = keyword switch
            {
                "where" => new WhereStage(RequireExpression("where")),
                "select" => ParseSelect(),
                "sort" or "orderby" => ParseSort(),
                "take" or "limit" => new TakeStage(ParseCount(keyword)),
                "skip" => new SkipStage(ParseCount(keyword)),
                "first" => new TakeStage(1),
                "last" => new LastStage(),
                "count" => new CountStage(),
                "reverse" => new ReverseStage(),
                "distinct" => new DistinctStage(AtStageEnd ? null : ParseExpression()),
                "group" => ParseGroup(),
                "sum" or "avg" or "min" or "max" => new AggregateStage(keyword, AtStageEnd ? null : ParseExpression()),
                "get" => new GetStage(RequireExpression("get")),
                "components" => new ComponentsStage(AtStageEnd ? null : ParseTypeName()),
                "children" => new ChildrenStage(),
                "descendants" => new DescendantsStage(AtStageEnd ? (int?)null : ParseCount(keyword)),
                "parent" => new ParentStage(),
                "gameobject" => new GameObjectStage(),
                "set" => ParseSet(),
                "call" => ParseCall(),
                "add" => new AddStage(QueryTypes.Resolve(ParseTypeName(), token.Position)),
                "destroy" => new DestroyStage(),
                _ => throw new QueryException($"Unknown stage '{token.Text}'. Stages: {string.Join(", ", StageKeywords)}", token.Position),
            };
            stage.Keyword = keyword;
            stage.Position = token.Position;
            if (!AtStageEnd)
                throw new QueryException($"Unexpected {Peek} after '{keyword}' stage", Peek.Position);
            return stage;
        }

        Node RequireExpression(string keyword)
        {
            if (AtStageEnd)
                throw new QueryException($"'{keyword}' needs an expression", Peek.Position);
            return ParseExpression();
        }

        int ParseCount(string keyword)
        {
            var token = Peek;
            if (token.Kind != TokenKind.Number || token.Value is not long n || n < 0 || n > int.MaxValue)
                throw new QueryException($"'{keyword}' needs a whole number", token.Position);
            _p++;
            return (int)n;
        }

        Stage ParseSelect()
        {
            var columns = new List<(Eval, string)>();
            do
            {
                var expr = RequireExpression("select");
                var name = expr.Text;
                if (Peek.IsWord("as"))
                {
                    _p++;
                    var alias = Next();
                    if (alias.Kind != TokenKind.Identifier && alias.Kind != TokenKind.String)
                        throw new QueryException("Expected a column name after 'as'", alias.Position);
                    name = alias.Kind == TokenKind.String ? (string)alias.Value! : alias.Text;
                }
                columns.Add((expr.Compile(), name));
            } while (TryComma());
            return new SelectStage(columns);
        }

        Stage ParseSort()
        {
            if (Peek.IsWord("by"))
                _p++;
            var keys = new List<(Eval, bool)>();
            do
            {
                var expr = RequireExpression("sort").Compile();
                var descending = false;
                if (Peek.IsWord("desc"))
                {
                    descending = true;
                    _p++;
                }
                else if (Peek.IsWord("asc"))
                    _p++;
                keys.Add((expr, descending));
            } while (TryComma());
            return new SortStage(keys);
        }

        Stage ParseGroup()
        {
            if (Peek.IsWord("by"))
                _p++;
            var key = RequireExpression("group");
            return new GroupStage(key.Compile(), key.Text);
        }

        Stage ParseSet()
        {
            var assignments = new List<(QueryPath, Eval, string)>();
            do
            {
                var start = Peek.Position;
                var target = ParsePostfix();
                var path = QueryPath.From(target, start);
                if (!Peek.IsOperator("="))
                    throw new QueryException($"Expected '=' after '{target.Text}' in 'set'", Peek.Position);
                _p++;
                var value = RequireExpression("set");
                assignments.Add((path, value.Compile(), target.Text));
            } while (TryComma());
            return new SetStage(assignments);
        }

        Stage ParseCall()
        {
            var start = Peek.Position;
            var expr = ParsePostfix();
            if (expr is not CallNode call)
                throw new QueryException("'call' needs a method call such as call SetActive(false) or call transform.SetParent(find(\"Root\").transform)", start);
            return new CallStage(call);
        }

        string ParseTypeName()
        {
            var token = Next();
            if (token.Kind == TokenKind.String)
                return (string)token.Value!;
            if (token.Kind != TokenKind.Identifier)
                throw new QueryException($"Expected a type name but found {token}", token.Position);
            var name = token.Text;
            while (Peek.Kind == TokenKind.Dot && PeekAt(1).Kind == TokenKind.Identifier)
            {
                _p++;
                name += "." + Next().Text;
            }
            return name;
        }

        bool TryComma()
        {
            if (Peek.Kind != TokenKind.Comma)
                return false;
            _p++;
            return true;
        }

        #endregion

        #region Expressions

        public Node ParseExpression()
        {
            var start = Peek.Position;
            var node = ParseCoalesce();
            return Finish(node, start);
        }

        Node Finish(Node node, int start)
        {
            var end = _p > 0 ? EndOf(_tokens[_p - 1]) : start;
            if (string.IsNullOrEmpty(node.Text))
                node.Text = _source.Substring(start, Math.Max(0, end - start)).Trim();
            if (node.Position == 0 && start > 0)
                node.Position = start;
            return node;
        }

        static int EndOf(Token token) => token.Position + Math.Max(1, token.Text.Length);

        Node ParseCoalesce()
        {
            var start = Peek.Position;
            var left = ParseOr();
            while (Peek.IsOperator("??"))
            {
                var op = Next();
                left = Finish(new BinaryNode("??", left, ParseOr()) { Position = op.Position }, start);
            }
            return left;
        }

        Node ParseOr()
        {
            var start = Peek.Position;
            var left = ParseAnd();
            while (Peek.IsOperator("||") || Peek.IsWord("or"))
            {
                var op = Next();
                left = Finish(new BinaryNode("or", left, ParseAnd()) { Position = op.Position }, start);
            }
            return left;
        }

        Node ParseAnd()
        {
            var start = Peek.Position;
            var left = ParseNot();
            while (Peek.IsOperator("&&") || Peek.IsWord("and"))
            {
                var op = Next();
                left = Finish(new BinaryNode("and", left, ParseNot()) { Position = op.Position }, start);
            }
            return left;
        }

        Node ParseNot()
        {
            if (Peek.IsOperator("!") || Peek.IsWord("not"))
            {
                var start = Peek.Position;
                var op = Next();
                return Finish(new UnaryNode("not", ParseNot()) { Position = op.Position }, start);
            }
            return ParseComparison();
        }

        Node ParseComparison()
        {
            var start = Peek.Position;
            var left = ParseAdditive();
            var token = Peek;
            if (token.Kind == TokenKind.Operator && token.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
            {
                _p++;
                return Finish(new BinaryNode(token.Text, left, ParseAdditive()) { Position = token.Position }, start);
            }
            if (token.IsOperator("="))
                throw new QueryException("Use '==' to compare ('=' only assigns inside 'set')", token.Position);
            if (token.Kind == TokenKind.Identifier && ComparisonWords.Contains(token.Text))
            {
                _p++;
                return Finish(new BinaryNode(token.Text.ToLowerInvariant(), left, ParseAdditive()) { Position = token.Position }, start);
            }
            if (token.IsWord("is"))
            {
                _p++;
                var negate = false;
                if (Peek.IsWord("not"))
                {
                    negate = true;
                    _p++;
                }
                Node node;
                if (Peek.IsWord("null"))
                {
                    _p++;
                    node = new BinaryNode("==", left, new LiteralNode(null)) { Position = token.Position };
                }
                else
                    node = new IsNode(left, ParseTypeName()) { Position = token.Position };
                if (negate)
                    node = new UnaryNode("not", node) { Position = token.Position };
                return Finish(node, start);
            }
            return left;
        }

        Node ParseAdditive()
        {
            var start = Peek.Position;
            var left = ParseMultiplicative();
            while (Peek.IsOperator("+") || Peek.IsOperator("-"))
            {
                var op = Next();
                left = Finish(new BinaryNode(op.Text, left, ParseMultiplicative()) { Position = op.Position }, start);
            }
            return left;
        }

        Node ParseMultiplicative()
        {
            var start = Peek.Position;
            var left = ParseUnary();
            while (Peek.IsOperator("*") || Peek.IsOperator("/") || Peek.IsOperator("%"))
            {
                var op = Next();
                left = Finish(new BinaryNode(op.Text, left, ParseUnary()) { Position = op.Position }, start);
            }
            return left;
        }

        Node ParseUnary()
        {
            if (Peek.IsOperator("-"))
            {
                var start = Peek.Position;
                var op = Next();
                var operand = ParseUnary();
                if (operand is LiteralNode { Value: long l })
                    return Finish(new LiteralNode(-l) { Position = op.Position }, start);
                if (operand is LiteralNode { Value: double d })
                    return Finish(new LiteralNode(-d) { Position = op.Position }, start);
                return Finish(new UnaryNode("-", operand) { Position = op.Position }, start);
            }
            return ParsePostfix();
        }

        Node ParsePostfix()
        {
            var start = Peek.Position;
            var node = ParsePrimary();
            while (true)
            {
                if (Peek.Kind == TokenKind.Dot)
                {
                    _p++;
                    var name = Expect(TokenKind.Identifier, "a member name after '.'");
                    if (Peek.Kind == TokenKind.LParen)
                        node = new CallNode(node, name.Text, ParseArguments()) { Position = name.Position };
                    else
                        node = new MemberNode(node, name.Text) { Position = name.Position };
                    node = Finish(node, start);
                    continue;
                }
                if (Peek.Kind == TokenKind.LBracket)
                {
                    var open = Next();
                    var index = ParseExpression();
                    Expect(TokenKind.RBracket, "']'");
                    node = Finish(new IndexNode(node, index) { Position = open.Position }, start);
                    continue;
                }
                return node;
            }
        }

        Node ParsePrimary()
        {
            var start = Peek.Position;
            var token = Next();
            switch (token.Kind)
            {
                case TokenKind.Number:
                case TokenKind.String:
                    return Finish(new LiteralNode(token.Value) { Position = token.Position }, start);
                case TokenKind.Meta:
                    return Finish(new MetaNode((string)token.Value!) { Position = token.Position }, start);
                case TokenKind.LParen:
                {
                    var inner = ParseExpression();
                    Expect(TokenKind.RParen, "')'");
                    return inner;
                }
                case TokenKind.LBracket:
                {
                    var items = new List<Node>();
                    if (Peek.Kind != TokenKind.RBracket)
                    {
                        do items.Add(ParseExpression());
                        while (TryComma());
                    }
                    Expect(TokenKind.RBracket, "']'");
                    return Finish(new ListNode(items) { Position = token.Position }, start);
                }
                case TokenKind.Identifier:
                {
                    if (token.IsWord("true") || token.IsWord("false"))
                        return Finish(new LiteralNode(token.IsWord("true")) { Position = token.Position }, start);
                    if (token.IsWord("null"))
                        return Finish(new LiteralNode(null) { Position = token.Position }, start);
                    if (Reserved.Contains(token.Text))
                        throw new QueryException($"'{token.Text}' is an operator, not a value", token.Position);
                    if (Peek.Kind == TokenKind.LParen)
                        return Finish(new CallNode(null, token.Text, ParseArguments()) { Position = token.Position }, start);
                    return Finish(new IdentNode(token.Text) { Position = token.Position }, start);
                }
                default:
                    throw new QueryException($"Expected a value but found {token}", token.Position);
            }
        }

        List<Node> ParseArguments()
        {
            Expect(TokenKind.LParen, "'('");
            var args = new List<Node>();
            if (Peek.Kind != TokenKind.RParen)
            {
                do args.Add(ParseExpression());
                while (TryComma());
            }
            Expect(TokenKind.RParen, "')'");
            return args;
        }

        #endregion
    }
}
