using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Feeder.Fmp.Tests
{
    public sealed class FrameScannerTests
    {
        readonly ITestOutputHelper _output;

        public FrameScannerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        static FrameFields Scan(string json, out byte[] bytes)
        {
            bytes = Encoding.UTF8.GetBytes(json);
            Assert.True(FmpFrame.TryScan(bytes, 0, bytes.Length, out FrameFields fields), "the frame should scan: " + json);
            return fields;
        }

        static bool Fails(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            return !FmpFrame.TryScan(bytes, 0, bytes.Length, out _);
        }

        public static TheoryData<string> Arguments => new TheoryData<string>
        {
            "[]",
            "[1,2,3]",
            "[\"plain\"]",
            "[\"quote \\\" inside\"]",
            "[\"ends with a backslash \\\\\"]",
            "[\"\\\\\\\\\\\"\"]",
            "[\"brace } and bracket ] and { and [ inside\"]",
            "[\"comma, colon: and \\\"quoted, text\\\"\"]",
            "[[[[1],[2]],{\"a\":[{\"b\":\"]}\"}]}],[]]",
            "[{\"type\":3,\"target\":\"nested\",\"arguments\":[9],\"error\":\"e\",\"result\":0}]",
            "[\"\\u00e9\\ud83d\\ude00\\u001e\"]",
            "[\"\u00e9 \U0001F600 raw utf-8, \u4e2d\u6587\"]",
            " [ 1 , { \"k\" : \"v\" } ,\n\t null ] ",
            "[true,false,null,-12.5e+3,0]",
            "[{\"\":\"\"},{\"a\\\"b\":1}]",
        };

        [Theory]
        [MemberData(nameof(Arguments))]
        public void Arguments_are_sliced_out_exactly(string arguments)
        {
            string frame = "{\"type\":1,\"invocationId\":\"7\",\"target\":\"T\",\"arguments\":" + arguments + "}";
            FrameFields f = Scan(frame, out byte[] bytes);

            Assert.Equal(1, f.Type);
            Assert.Equal(arguments.Trim(), FmpFrame.Raw(bytes, f.Arguments));
            Assert.Equal("T", FmpFrame.Text(bytes, f.Target));
            Assert.Equal("7", FmpFrame.Text(bytes, f.InvocationId));
        }

        [Theory]
        [MemberData(nameof(Arguments))]
        public void Key_order_does_not_matter(string arguments)
        {
            FrameFields f = Scan("{\"arguments\":" + arguments + ",\"target\":\"Later\",\"type\":1}", out byte[] bytes);

            Assert.Equal(1, f.Type);
            Assert.Equal("Later", FmpFrame.Text(bytes, f.Target));
            Assert.Equal(arguments.Trim(), FmpFrame.Raw(bytes, f.Arguments));
            Assert.True(f.InvocationId.IsEmpty);
        }

        [Fact]
        public void Keys_inside_nested_values_never_shadow_the_top_level_ones()
        {
            FrameFields f = Scan("{\"type\":1,\"arguments\":[{\"type\":6,\"target\":\"inner\",\"invocationId\":\"inner\"}],\"target\":\"outer\"}", out byte[] bytes);

            Assert.Equal(1, f.Type);
            Assert.Equal("outer", FmpFrame.Text(bytes, f.Target));
            Assert.True(f.InvocationId.IsEmpty);
        }

        [Theory]
        [InlineData("{\"a\":1}")]
        [InlineData("\"text with \\\" quote\"")]
        [InlineData("\"\"")]
        [InlineData("12.5")]
        [InlineData("-0")]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("[\"a\",{\"b\":[1]}]")]
        public void Results_of_every_json_kind_are_sliced_out_exactly(string result)
        {
            FrameFields f = Scan("{\"type\":3,\"invocationId\":\"1\",\"result\":" + result + "}", out byte[] bytes);

            Assert.Equal(3, f.Type);
            Assert.Equal(result, FmpFrame.Raw(bytes, f.Result));
        }

        [Fact]
        public void A_completion_without_a_result_has_an_empty_result_slice()
        {
            FrameFields f = Scan("{\"type\":3,\"invocationId\":\"1\"}", out _);

            Assert.True(f.Result.IsEmpty);
            Assert.True(f.Error.IsEmpty);
        }

        [Fact]
        public void Error_text_is_unescaped()
        {
            FrameFields f = Scan("{\"type\":3,\"invocationId\":\"4\",\"error\":\"An error occurred invoking \\u0027Register\\u0027: \\\"bad\\\"\\nline two\\ttab \\\\ \\/ \\ud83d\\ude00\"}", out byte[] bytes);

            Assert.Equal("An error occurred invoking 'Register': \"bad\"\nline two\ttab \\ / \U0001F600", FmpFrame.Text(bytes, f.Error));
        }

        [Fact]
        public void Text_of_a_value_that_is_not_a_string_is_null()
        {
            FrameFields f = Scan("{\"type\":7,\"error\":null,\"allowReconnect\":true,\"target\":5,\"invocationId\":[\"x\"]}", out byte[] bytes);

            Assert.Null(FmpFrame.Text(bytes, f.Error));
            Assert.Null(FmpFrame.Text(bytes, f.Target));
            Assert.Null(FmpFrame.Text(bytes, f.InvocationId));
            Assert.Null(FmpFrame.Text(bytes, f.Result));
            Assert.True(FmpFrame.IsTrue(bytes, f.AllowReconnect));
        }

        [Fact]
        public void Escapes_that_look_like_a_key_or_quote_do_not_end_a_string_early()
        {
            FrameFields f = Scan("{\"target\":\"a\\\"},\\\"type\\\":9,\\\"x\\\":\\\"\",\"type\":1}", out byte[] bytes);

            Assert.Equal(1, f.Type);
            Assert.Equal("a\"},\"type\":9,\"x\":\"", FmpFrame.Text(bytes, f.Target));
        }

        [Fact]
        public void An_empty_object_scans_as_a_handshake_reply_without_error()
        {
            FrameFields f = Scan("{}", out byte[] bytes);

            Assert.Equal(-1, f.Type);
            Assert.Null(FmpFrame.Text(bytes, f.Error));
        }

        [Fact]
        public void A_handshake_error_is_found()
        {
            FrameFields f = Scan("{\"error\":\"Requested protocol \\u0027xml\\u0027 is not available.\"}", out byte[] bytes);

            Assert.Equal("Requested protocol 'xml' is not available.", FmpFrame.Text(bytes, f.Error));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("[1]")]
        [InlineData("\"text\"")]
        [InlineData("{")]
        [InlineData("{\"type\":1")]
        [InlineData("{\"type\":1,")]
        [InlineData("{\"type\" 1}")]
        [InlineData("{\"type\":}")]
        [InlineData("{type:1}")]
        [InlineData("{\"type\":1}}")]
        [InlineData("{\"type\":1} x")]
        [InlineData("{\"type\":\"1\"}")]
        [InlineData("{\"type\":1.5}")]
        [InlineData("{\"type\":-1}")]
        [InlineData("{\"type\":99999}")]
        [InlineData("{\"target\":\"never ends}")]
        [InlineData("{\"target\":\"ends with an escaped quote\\\"}")]
        [InlineData("{\"arguments\":[1,[2}")]
        [InlineData("{\"arguments\":[\"unterminated]}")]
        [InlineData("{\"arguments\":{\"a\":[}}")]
        [InlineData("{\"a\":1,}")]
        public void Malformed_frames_are_rejected(string json)
        {
            Assert.True(Fails(json), json);
        }

        [Fact]
        public void Deeply_nested_input_neither_overflows_the_stack_nor_takes_long()
        {
            const int depth = 500000;
            string arguments = new string('[', depth) + new string(']', depth);
            var watch = Stopwatch.StartNew();

            FrameFields f = Scan("{\"type\":1,\"target\":\"Deep\",\"arguments\":" + arguments + "}", out byte[] bytes);

            Assert.Equal(depth * 2, f.Arguments.End - f.Arguments.Start);
            Assert.True(watch.ElapsedMilliseconds < 2000, "took " + watch.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void A_twenty_megabyte_string_is_skipped_in_linear_time()
        {
            string big = new string('x', 20 * 1024 * 1024);
            byte[] bytes = Encoding.UTF8.GetBytes("{\"type\":1,\"target\":\"Big\",\"arguments\":[\"" + big + "\",\"" + big + "\\\"\"]}");
            var watch = Stopwatch.StartNew();

            Assert.True(FmpFrame.TryScan(bytes, 0, bytes.Length, out FrameFields f));
            long scanMs = watch.ElapsedMilliseconds;
            string raw = FmpFrame.Raw(bytes, f.Arguments);

            _output.WriteLine("scanned a {0:N0} byte frame in {1} ms", bytes.Length, scanMs);
            Assert.Equal(2 * big.Length + 9, raw.Length);
            Assert.True(scanMs < 3000, "scan took " + scanMs + " ms");
        }

        [Fact]
        public void A_frame_inside_a_larger_buffer_is_scanned_by_offset()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("garbage{\"type\":6}trailing");

            Assert.True(FmpFrame.TryScan(bytes, 7, 7 + 10, out FrameFields f));
            Assert.Equal(6, f.Type);
        }

        [Fact]
        public void An_invocation_frame_is_valid_json_with_the_target_and_arguments_intact()
        {
            const string target = "We\"ird\\Name\n\u00e9\u0001";
            const string arguments = " [ {\"a\":\"x\\\"y\"}, [1,2], \"\\u00e9\" ] ";

            byte[] frame = FmpFrame.Invocation("12", target, arguments);

            Assert.Equal(0x1E, frame[frame.Length - 1]);
            using (JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(frame, 0, frame.Length - 1)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("type").GetInt32());
                Assert.Equal("12", root.GetProperty("invocationId").GetString());
                Assert.Equal(target, root.GetProperty("target").GetString());
                Assert.Equal(JsonValueKind.Array, root.GetProperty("arguments").ValueKind);
                Assert.Equal("x\"y", root.GetProperty("arguments")[0].GetProperty("a").GetString());
            }
        }

        [Fact]
        public void A_fire_and_forget_frame_has_no_invocation_id_and_an_exact_shape()
        {
            byte[] frame = FmpFrame.Invocation(null, "Heartbeat", "[{\"x\":1}]");

            Assert.Equal("{\"type\":1,\"target\":\"Heartbeat\",\"arguments\":[{\"x\":1}]}\u001e", Encoding.UTF8.GetString(frame));
        }

        [Fact]
        public void Large_arguments_are_copied_without_changing_a_byte()
        {
            string arguments = "[\"" + new string('\u00e9', 3_000_000) + "\"]";

            byte[] frame = FmpFrame.Invocation("1", "Big", arguments);

            Assert.True(FmpFrame.TryScan(frame, 0, frame.Length - 1, out FrameFields f));
            Assert.Equal(arguments, FmpFrame.Raw(frame, f.Arguments));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{}")]
        [InlineData("\"text\"")]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("[1")]
        [InlineData("1]")]
        [InlineData("  ")]
        public void Arguments_that_are_not_a_json_array_are_refused(string arguments)
        {
            Assert.Throws<ArgumentException>(() => FmpFrame.Invocation("1", "T", arguments));
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("  []  ")]
        [InlineData("\n[1]\r\n")]
        public void Arguments_that_are_a_json_array_are_accepted(string arguments)
        {
            Assert.NotEmpty(FmpFrame.Invocation("1", "T", arguments));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void A_target_is_required(string target)
        {
            Assert.Throws<ArgumentException>(() => FmpFrame.Invocation("1", target, "[]"));
        }

        [Fact]
        public void Completion_frames_carry_either_a_null_result_or_an_escaped_error()
        {
            Assert.Equal("{\"type\":3,\"invocationId\":\"a\\\"b\",\"result\":null}\u001e", Encoding.UTF8.GetString(FmpFrame.Completion("a\"b", null)));

            byte[] error = FmpFrame.Completion("9", "bad \"thing\"\n");
            using (JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(error, 0, error.Length - 1)))
                Assert.Equal("bad \"thing\"\n", document.RootElement.GetProperty("error").GetString());
        }
    }
}
