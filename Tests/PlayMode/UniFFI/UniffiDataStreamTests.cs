using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;
using Uniffi = uniffi.livekit_uniffi;

namespace LiveKit.PlayModeTests
{
    // Data streams v2 with the UniFFI stream managers on BOTH participants.
    //
    // Each UniffiStreamPeer borrows a protobuf-FFI Room as a dumb transport (see there), so what
    // these tests exercise is the C# UniFFI binding surface end to end over a real SFU round trip:
    // records with defaults and nullable members, sequences of the cross-crate `Bytes` custom
    // type, Rust async surfaced as Task, a foreign callback interface invoked from Rust threads,
    // typed exceptions, and object lifetimes.
    //
    // The wire framing (legacy header/chunks/trailer vs v2 single inline packet) is chosen by the
    // registry the outgoing manager consults, which the tests control through
    // FixedParticipantRegistry.
    public class UniffiDataStreamTests
    {
        private const string SenderIdentity = "uniffi-sender";
        private const string ReceiverIdentity = "uniffi-receiver";

        // livekit-data-stream STREAM_CHUNK_SIZE_BYTES.
        private const int ChunkSizeBytes = 15000;

        [SetUp]
        public void RequireUniffiLibrary()
        {
            if (!UniffiAvailability.IsAvailable) Assert.Ignore(UniffiAvailability.IgnoreReason);
        }

        private static TestRoomContext NewContext()
        {
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = SenderIdentity;
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = ReceiverIdentity;
            return new TestRoomContext(new[] { sender, receiver });
        }

        // The tunnel bypasses the SFU's identity stamping of the inner packet, so the sender
        // identity has to travel in the stream options.
        private static Uniffi.StreamTextOptions TextOptions(string topic, Dictionary<string, string>? attributes = null) =>
            new Uniffi.StreamTextOptions(
                Topic: topic,
                Attributes: attributes ?? new Dictionary<string, string>(),
                DestinationIdentities: Array.Empty<string>(),
                SenderIdentity: SenderIdentity);

        private static Uniffi.StreamByteOptions ByteOptions(string topic, string? name = null, string? mimeType = null, string[]? destinations = null) =>
            new Uniffi.StreamByteOptions(
                Topic: topic,
                Attributes: new Dictionary<string, string>(),
                DestinationIdentities: destinations ?? Array.Empty<string>(),
                MimeType: mimeType,
                Name: name,
                SenderIdentity: SenderIdentity);

        private static void AssertSucceeded(System.Threading.Tasks.Task task, string what)
        {
            Assert.IsTrue(UniffiTasks.Succeeded(task), $"{what} {UniffiTasks.Describe(task)}");
        }

        private static void AssertHealthy(UniffiStreamPeer sender, UniffiStreamPeer receiver)
        {
            CollectionAssert.IsEmpty(sender.Errors, "sender transport errors");
            CollectionAssert.IsEmpty(receiver.Errors, "receiver transport errors");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SendText_LegacyFraming_ArrivesWithIdentityTopicAndAttributes()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.Legacy(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity));
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            var attributes = new Dictionary<string, string> { ["kind"] = "greeting" };
            var send = sender.Outgoing.SendText("hello from uniffi", TextOptions("chat", attributes));
            yield return UniffiTasks.Await(send);
            AssertSucceeded(send, "SendText");
            Assert.AreEqual("chat", send.Result.Topic);

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            var stream = opened.Result;
            Assert.IsNotNull(stream, "incoming queue closed before a stream was opened");
            Assert.AreEqual(SenderIdentity, stream!.Identity);
            Assert.IsNotNull(stream.TextReader, "text stream surfaced without a text reader");
            Assert.IsNull(stream.ByteReader, "text stream surfaced with a byte reader");

            var info = stream.TextReader!.Info();
            Assert.AreEqual(send.Result.Id, info.Id);
            Assert.AreEqual("chat", info.Topic);
            Assert.AreEqual("greeting", info.Attributes["kind"]);
            Assert.AreEqual(Uniffi.EncryptionType.None, info.EncryptionType);

            var readAll = stream.TextReader.ReadAll();
            yield return UniffiTasks.Await(readAll);
            AssertSucceeded(readAll, "ReadAll");
            Assert.AreEqual("hello from uniffi", readAll.Result);

            // Legacy framing: header, one chunk, trailer.
            var published = new Expectation(() => sender.PacketsPublished >= 3, 5f);
            yield return published.Wait();
            Assert.IsNull(published.Error, published.Error);
            yield return null;
            Assert.AreEqual(3, sender.PacketsPublished, "legacy send should be header + chunk + trailer");
            AssertHealthy(sender, receiver);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SendText_V2Recipient_InlinedIntoSinglePacket()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.V2(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity));
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            // Highly compressible and well under the chunk size, so the packet is inlined and the
            // deflate path runs on both ends.
            var text = string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog. ", 200));
            var send = sender.Outgoing.SendText(text, TextOptions("chat"));
            yield return UniffiTasks.Await(send);
            AssertSucceeded(send, "SendText");

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            Assert.IsNotNull(opened.Result?.TextReader, "no text stream opened");

            var readAll = opened.Result!.TextReader!.ReadAll();
            yield return UniffiTasks.Await(readAll);
            AssertSucceeded(readAll, "ReadAll");
            Assert.AreEqual(text, readAll.Result);
            Assert.AreEqual((ulong)text.Length, opened.Result.TextReader.Info().TotalLength);

            var published = new Expectation(() => sender.PacketsPublished >= 1, 5f);
            yield return published.Wait();
            Assert.IsNull(published.Error, published.Error);
            yield return null;
            Assert.AreEqual(1, sender.PacketsPublished, "v2 send should be a single inline header packet");
            Assert.AreEqual(1, receiver.PacketsReceived);
            AssertHealthy(sender, receiver);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator StreamText_IncrementalWriter_ReadAllConcatenatesWrites()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.Legacy(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity));
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            var open = sender.Outgoing.StreamText(TextOptions("notes"));
            yield return UniffiTasks.Await(open);
            AssertSucceeded(open, "StreamText");
            using var writer = open.Result;

            foreach (var part in new[] { "Hello, ", "UniFFI ", "world" })
            {
                var write = writer.Write(part);
                yield return UniffiTasks.Await(write);
                AssertSucceeded(write, $"Write(\"{part}\")");
            }

            var isOpen = writer.IsOpen();
            yield return UniffiTasks.Await(isOpen);
            AssertSucceeded(isOpen, "IsOpen");
            Assert.IsTrue(isOpen.Result, "writer reported closed before Close");

            var close = writer.Close();
            yield return UniffiTasks.Await(close);
            AssertSucceeded(close, "Close");

            isOpen = writer.IsOpen();
            yield return UniffiTasks.Await(isOpen);
            AssertSucceeded(isOpen, "IsOpen after Close");
            Assert.IsFalse(isOpen.Result, "writer still open after Close");

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            Assert.IsNotNull(opened.Result?.TextReader, "no text stream opened");
            Assert.IsNull(opened.Result!.TextReader!.Info().TotalLength, "incremental stream should not declare a total length");

            var readAll = opened.Result.TextReader.ReadAll();
            yield return UniffiTasks.Await(readAll);
            AssertSucceeded(readAll, "ReadAll");
            Assert.AreEqual("Hello, UniFFI world", readAll.Result);
            AssertHealthy(sender, receiver);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SendBytes_TargetedAtReceiver_PreservesPayloadNameAndMimeType()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.Legacy(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity));
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            var payload = new byte[4096];
            new Random(42).NextBytes(payload);
            var options = ByteOptions("files", name: "blob.bin", mimeType: "application/x-test", destinations: new[] { ReceiverIdentity });
            var send = sender.Outgoing.SendBytes(new ReadOnlyMemory<byte>(payload), options);
            yield return UniffiTasks.Await(send);
            AssertSucceeded(send, "SendBytes");
            Assert.AreEqual("blob.bin", send.Result.Name);
            Assert.AreEqual((ulong)payload.Length, send.Result.TotalLength);

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            var stream = opened.Result;
            Assert.IsNotNull(stream?.ByteReader, "byte stream surfaced without a byte reader");
            Assert.IsNull(stream!.TextReader, "byte stream surfaced with a text reader");
            Assert.AreEqual(SenderIdentity, stream.Identity);

            var info = stream.ByteReader!.Info();
            Assert.AreEqual(send.Result.Id, info.Id);
            Assert.AreEqual("files", info.Topic);
            Assert.AreEqual("blob.bin", info.Name);
            Assert.AreEqual("application/x-test", info.MimeType);
            Assert.AreEqual((ulong)payload.Length, info.TotalLength);

            var readAll = stream.ByteReader.ReadAll();
            yield return UniffiTasks.Await(readAll);
            AssertSucceeded(readAll, "ReadAll");
            CollectionAssert.AreEqual(payload, readAll.Result.ToArray());
            AssertHealthy(sender, receiver);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator StreamBytes_IncrementalWriter_NextYieldsChunksThenNull()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.Legacy(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity));
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            var open = sender.Outgoing.StreamBytes(ByteOptions("bytes"));
            yield return UniffiTasks.Await(open);
            AssertSucceeded(open, "StreamBytes");
            using var writer = open.Result;

            var expected = new List<byte>();
            for (int i = 0; i < 3; i++)
            {
                var chunk = Enumerable.Repeat((byte)(i + 1), 5000).ToArray();
                expected.AddRange(chunk);
                var write = writer.Write(new ReadOnlyMemory<byte>(chunk));
                yield return UniffiTasks.Await(write);
                AssertSucceeded(write, $"Write #{i}");
            }
            var close = writer.Close();
            yield return UniffiTasks.Await(close);
            AssertSucceeded(close, "Close");

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            Assert.IsNotNull(opened.Result?.ByteReader, "no byte stream opened");
            var reader = opened.Result!.ByteReader!;

            var received = new List<byte>();
            for (int reads = 0; ; reads++)
            {
                Assert.Less(reads, 100, "reader never signalled end of stream");
                var next = reader.Next();
                yield return UniffiTasks.Await(next);
                AssertSucceeded(next, $"Next #{reads}");
                if (!next.Result.HasValue) break;
                received.AddRange(next.Result.Value.ToArray());
            }
            CollectionAssert.AreEqual(expected, received);
            AssertHealthy(sender, receiver);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SendText_LargerThanChunkSize_SpansMultiplePackets()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.Legacy(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity));
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            // ASCII only, so byte length equals character count.
            var builder = new StringBuilder(40000);
            for (int i = 0; i < 40000; i++) builder.Append((char)('a' + i % 26));
            var text = builder.ToString();
            var expectedChunks = (text.Length + ChunkSizeBytes - 1) / ChunkSizeBytes;

            var send = sender.Outgoing.SendText(text, TextOptions("bulk"));
            yield return UniffiTasks.Await(send);
            AssertSucceeded(send, "SendText");

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            Assert.IsNotNull(opened.Result?.TextReader, "no text stream opened");

            var readAll = opened.Result!.TextReader!.ReadAll();
            yield return UniffiTasks.Await(readAll, 20f);
            AssertSucceeded(readAll, "ReadAll");
            Assert.AreEqual(text, readAll.Result);

            var expectedPackets = 1 + expectedChunks + 1;
            var published = new Expectation(() => sender.PacketsPublished >= expectedPackets, 5f);
            yield return published.Wait();
            Assert.IsNull(published.Error, published.Error);
            yield return null;
            Assert.AreEqual(expectedPackets, sender.PacketsPublished, "header + chunks + trailer");
            Assert.AreEqual(expectedPackets, receiver.PacketsReceived);
            AssertHealthy(sender, receiver);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator StreamClosed_IsReportedAndOpenStreamCountReturnsToZero()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.Legacy(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity));
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            var send = sender.Outgoing.SendText("bye", TextOptions("chat"));
            yield return UniffiTasks.Await(send);
            AssertSucceeded(send, "SendText");

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            Assert.IsNotNull(opened.Result?.TextReader, "no text stream opened");

            var readAll = opened.Result!.TextReader!.ReadAll();
            yield return UniffiTasks.Await(readAll);
            AssertSucceeded(readAll, "ReadAll");

            var closed = receiver.NextClosedStream();
            yield return UniffiTasks.Await(closed);
            AssertSucceeded(closed, "NextClosedStream");
            Assert.IsNotNull(closed.Result, "closed-stream queue ended without a notification");
            Assert.AreEqual(send.Result.Id, closed.Result!.StreamId);
            Assert.AreEqual(SenderIdentity, closed.Result.Identity);

            var openCount = receiver.Incoming.OpenStreamCount();
            yield return UniffiTasks.Await(openCount);
            AssertSucceeded(openCount, "OpenStreamCount");
            Assert.AreEqual(0UL, openCount.Result);
            AssertHealthy(sender, receiver);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Incoming_WithPayloadLimit_ReaderFailsWithPayloadTooLarge()
        {
            using var context = NewContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            using var sender = new UniffiStreamPeer(context.Rooms[0], SenderIdentity, FixedParticipantRegistry.Legacy(ReceiverIdentity));
            using var receiver = new UniffiStreamPeer(context.Rooms[1], ReceiverIdentity, FixedParticipantRegistry.Legacy(SenderIdentity), maxIncomingPayloadBytes: 512);
            yield return sender.WaitUntilReachable(receiver);
            Assert.Greater(receiver.PingsReceived, 0, "tunnel warm-up never reached the receiver");

            // The cap is enforced by the receiver, so the send itself succeeds.
            var send = sender.Outgoing.SendText(new string('x', 2000), TextOptions("chat"));
            yield return UniffiTasks.Await(send);
            AssertSucceeded(send, "SendText");

            var opened = receiver.NextOpenedStream();
            yield return UniffiTasks.Await(opened);
            AssertSucceeded(opened, "NextOpenedStream");
            Assert.IsNotNull(opened.Result?.TextReader, "no text stream opened");

            var readAll = opened.Result!.TextReader!.ReadAll();
            yield return UniffiTasks.Await(readAll);
            Assert.IsTrue(readAll.IsCompleted, "ReadAll did not complete");
            Assert.IsTrue(readAll.IsFaulted, "ReadAll succeeded despite exceeding the payload cap");
            Assert.IsInstanceOf<Uniffi.DataStreamException.PayloadTooLarge>(UniffiTasks.Flatten(readAll.Exception));

            // The failure still closes the stream exactly once.
            var closed = receiver.NextClosedStream();
            yield return UniffiTasks.Await(closed);
            AssertSucceeded(closed, "NextClosedStream");
            Assert.AreEqual(send.Result.Id, closed.Result?.StreamId);
            AssertHealthy(sender, receiver);
        }
    }
}
