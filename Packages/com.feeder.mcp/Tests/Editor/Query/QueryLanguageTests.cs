#nullable enable
using System.Collections.Generic;
using System.Linq;
using Feeder.MCP.Editor.Query;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Query
{
    public class QueryLanguageTests
    {
        static List<object?> Items(string query) => MatrixQuery.Run(query, 1000).Items.ToList();
        static object? Single(string query) => Items(query).Single();

        [Test]
        public void Where_FiltersAListSource()
        {
            Assert.AreEqual(2L, Single("[1, 2, 3, 4] | where $ > 2 | count"));
        }

        [Test]
        public void Arithmetic_KeepsWholeNumbersAndDividesAsReal()
        {
            Assert.AreEqual(7L, Single("1 + 2 * 3"));
            Assert.AreEqual(3.5, Single("7 / 2"));
            Assert.AreEqual(3L, Single("7 % 4"));
            Assert.AreEqual(9L, Single("(1 + 2) * 3"));
            Assert.AreEqual(-4L, Single("-4"));
            Assert.AreEqual("ab3", Single("\"ab\" + 3"));
        }

        [Test]
        public void TakeAndSkip_ReadWholeNumbers()
        {
            CollectionAssert.AreEqual(new object[] { 2L, 3L }, Items("[1, 2, 3, 4] | skip 1 | take 2"));
            CollectionAssert.AreEqual(new object[] { 1L }, Items("[1, 2, 3] | first"));
            CollectionAssert.AreEqual(new object[] { 3L }, Items("[1, 2, 3] | last"));
            CollectionAssert.AreEqual(new object[] { 3L, 2L, 1L }, Items("[1, 2, 3] | reverse"));
        }

        [Test]
        public void TextOperators_IgnoreCaseExceptRegexAndEquality()
        {
            Assert.AreEqual(2L, Single("[\"Enemy_1\", \"enemy_2\", \"Player\"] | where $ startswith \"ENEMY\" | count"));
            Assert.AreEqual(1L, Single("[\"Enemy_1\", \"enemy_2\", \"Player\"] | where $ endswith \"YER\" | count"));
            Assert.AreEqual(3L, Single("[\"Enemy_1\", \"enemy_2\", \"Player\"] | where $ contains \"e\" | count"));
            Assert.AreEqual(1L, Single("[\"Enemy_1\", \"enemy_2\", \"Player\"] | where $ matches \"^E\" | count"));
            Assert.AreEqual(0L, Single("[\"Enemy\"] | where $ == \"enemy\" | count"));
        }

        [Test]
        public void InAndCoalesceAndNot()
        {
            Assert.AreEqual(2L, Single("[1, 2, 3] | where $ in [2, 3] | count"));
            Assert.AreEqual(5L, Single("null ?? 5"));
            Assert.AreEqual(1L, Single("[1, 2, 3] | where not ($ >= 2) | count"));
            Assert.AreEqual(true, Single("1 < 2 and 2 < 3 or false"));
        }

        [Test]
        public void SortDistinctGroup()
        {
            CollectionAssert.AreEqual(new object[] { 3L, 2L, 1L }, Items("[3, 1, 2] | sort $ desc"));
            CollectionAssert.AreEqual(new object[] { "a", "B", "c" }, Items("[\"c\", \"a\", \"B\"] | sort $"));
            Assert.AreEqual(3L, Single("[1, 1, 2, 2, 3] | distinct | count"));
            var groups = Items("[\"a\", \"b\", \"a\"] | group $").Cast<Row>().ToList();
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("a", groups[0].Values[0]);
            Assert.AreEqual(2L, groups[0].Values[1]);
            Assert.AreEqual("count", groups[0].Columns[1]);
        }

        [Test]
        public void Aggregates()
        {
            Assert.AreEqual(6L, Single("[1, 2, 3] | sum"));
            Assert.AreEqual(2.0, Single("[1, 2, 3] | avg"));
            Assert.AreEqual(1L, Single("[3, 1, 2] | min"));
            Assert.AreEqual(3L, Single("[3, 1, 2] | max"));
            Assert.AreEqual(6.5, Single("[1, 2.5, 3] | sum"));
            Assert.IsNull(Single("[] | avg"));
        }

        [Test]
        public void Select_NamesColumnsByAliasOrText()
        {
            var row = (Row)Items("[4] | select $ as n, $ * 10, len(\"abc\") as size").Single()!;
            CollectionAssert.AreEqual(new[] { "n", "$ * 10", "size" }, row.Columns);
            CollectionAssert.AreEqual(new object[] { 4L, 40L, 3L }, row.Values);
        }

        [Test]
        public void Select_ColumnsReadLikeMembersInLaterStages()
        {
            Assert.AreEqual(1L, Single("[1, 2, 3] | select $ * 2 as twice | where twice > 4 | count"));
        }

        [Test]
        public void Functions()
        {
            Assert.AreEqual(new Vector3(1, 2, 3), Single("vec(1, 2, 3)"));
            Assert.AreEqual(Color.red, Single("color(\"#ff0000\")"));
            Assert.AreEqual(new Color(0, 0.5f, 1, 1), Single("color(0, 0.5, 1)"));
            Assert.AreEqual(3L, Single("len(\"abc\")"));
            Assert.AreEqual("ABC", Single("upper(\"abc\")"));
            Assert.AreEqual(2.57, (double)Single("round(2.567, 2)")!, 1e-9);
            Assert.AreEqual("yes", Single("iif(2 > 1, \"yes\", \"no\")"));
            Assert.AreEqual(2L, Single("max(1, 2)"));
            Assert.AreEqual(5.0, Single("distance(vec(0, 0, 0), vec(3, 4, 0))"));
        }

        [Test]
        public void VectorMembersAndOperators()
        {
            Assert.AreEqual(true, Single("vec(0.1, 0, 0).x == 0.1"));
            Assert.AreEqual(new Vector3(2, 4, 6), Single("vec(1, 2, 3) * 2"));
            Assert.AreEqual(new Vector3(2, 2, 2), Single("vec(1, 1, 1) + vec(1, 1, 1)"));
            Assert.AreEqual(true, Single("vec(1, 2, 3) == [1, 2, 3]"));
        }

        [Test]
        public void MissingMember_ReadsNullWithAWarning()
        {
            var result = MatrixQuery.Run("[1] | get nothingHere");
            Assert.IsNull(result.Items.Single());
            Assert.That(result.Warnings.Single(), Does.Contain("nothingHere"));
            Assert.That(result.Text, Does.Contain("warnings:"));
        }

        [Test]
        public void ParseErrors_PointAtTheProblem()
        {
            var empty = Assert.Throws<QueryException>(() => MatrixQuery.Run("[1] | where"));
            Assert.That(empty.Message, Does.Contain("needs an expression").And.Contain("^"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("[1] | frobnicate")).Message, Does.Contain("Unknown stage"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("[1, 2")).Message, Does.Contain("']'"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("1 = 2")).Message, Does.Contain("=="));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("\"open")).Message, Does.Contain("Unterminated"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("nosuchsource")).Message, Does.Contain("Unknown source"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("")).Message, Does.Contain("empty"));
        }

        [Test]
        public void ReadQuery_RefusesChangingStagesAndMethods()
        {
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("[1] | set x = 1")).Message, Does.Contain("rql-mutate"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("new(\"X\")")).Message, Does.Contain("rql-mutate"));
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Run("[[1, 2]] | get Clear()")).Message, Does.Contain("rql-mutate"));
            Assert.AreEqual("ABC", Single("[\"abc\"] | get ToUpperInvariant()"));
        }

        [Test]
        public void Mutate_RefusesQueriesThatChangeNothing()
        {
            Assert.That(Assert.Throws<QueryException>(() => MatrixQuery.Mutate("[1] | count")).Message, Does.Contain("changes nothing"));
            Assert.Throws<QueryException>(() => MatrixQuery.Mutate("[1] | set x = 1", dryRun: true, save: true));
        }

        [Test]
        public void TableAndJsonOutput()
        {
            Assert.AreEqual("2 rows\nvalue\n1\n2", MatrixQuery.Run("[1, 2]").Text);
            Assert.That(MatrixQuery.Run("[1, 2, 3]", limit: 2).Text, Does.StartWith("3 rows (showing 2;"));
            Assert.AreEqual("{\"total\":2,\"items\":[{\"value\":1},{\"value\":\"a\"}]}", MatrixQuery.Run("[1, \"a\"]", format: "json").Text);
            Assert.AreEqual("0 rows", MatrixQuery.Run("[] | where $ > 1").Text);
            Assert.Throws<QueryException>(() => MatrixQuery.Run("[1]", format: "xml"));
        }

        [Test]
        public void ParsedQueries_AreCachedAndStillCorrect()
        {
            Assert.AreEqual(2L, Single("[1, 2, 3] | where $ >= 2 | count"));
            Assert.AreEqual(2L, Single("[1, 2, 3] | where $ >= 2 | count"));
        }

        [Test]
        public void Lexer_ReadsNumbersStringsAndOperators()
        {
            var tokens = QueryLexer.Tokenize("a.b >= 2.5e1 && 'x\\'y' | $path");
            Assert.AreEqual(TokenKind.Identifier, tokens[0].Kind);
            Assert.AreEqual(TokenKind.Dot, tokens[1].Kind);
            Assert.AreEqual(">=", tokens[3].Text);
            Assert.AreEqual(25.0, tokens[4].Value);
            Assert.AreEqual("&&", tokens[5].Text);
            Assert.AreEqual("x'y", tokens[6].Value);
            Assert.AreEqual(TokenKind.Pipe, tokens[7].Kind);
            Assert.AreEqual(TokenKind.Meta, tokens[8].Kind);
            Assert.AreEqual(42L, QueryLexer.Tokenize("42")[0].Value);
        }

        [Test]
        public void Convert_CoercesToMemberTypes()
        {
            Assert.AreEqual(3f, QueryConvert.To(3L, typeof(float), 0));
            Assert.AreEqual(LightType.Spot, QueryConvert.To("spot", typeof(LightType), 0));
            Assert.AreEqual(new Vector3(1, 2, 0), QueryConvert.To(new List<object?> { 1L, 2L }, typeof(Vector3), 0));
            Assert.AreEqual(Quaternion.Euler(0, 90, 0), QueryConvert.To(new List<object?> { 0L, 90L, 0L }, typeof(Quaternion), 0));
            Assert.AreEqual(Color.green, QueryConvert.To("#00ff00", typeof(Color), 0));
            Assert.AreEqual(typeof(Light), QueryConvert.To("Light", typeof(System.Type), 0));
            Assert.Throws<QueryException>(() => QueryConvert.To(2.5, typeof(int), 0));
            Assert.Throws<QueryException>(() => QueryConvert.To("nope", typeof(LightType), 0));
            Assert.Throws<QueryException>(() => QueryConvert.To(null, typeof(float), 0));
        }
    }
}
