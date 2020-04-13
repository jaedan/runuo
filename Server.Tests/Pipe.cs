using Server.Network;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Server.Tests
{
    public class PacketWriterTest
    {
        [Fact]
        public void TestWriteAsciiFixed()
        {
            Span<byte> data = new byte[100];
            Pipe.BufferWriter writer = new Pipe.BufferWriter();
            writer.First = data.Slice(50, 50);
            writer.Second = data.Slice(0, 50);

            // All data in first span
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteAsciiFixed("this is a test", 14);

            // Data crosses spans
            data.Fill(0);
            writer.Seek(44, SeekOrigin.Begin);
            writer.WriteAsciiFixed("this is a test", 14);

            // All data in second span
            data.Fill(0);
            writer.Seek(60, SeekOrigin.Begin);
            writer.WriteAsciiFixed("this is a test", 14);

            // Truncate string
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteAsciiFixed("this is a test", 4);

            // Pad string
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteAsciiFixed("this is a test", 30);
        }

        [Fact]
        public void TestWriteAsciiNull()
        {
            Span<byte> data = new byte[100];
            Pipe.BufferWriter writer = new Pipe.BufferWriter();
            writer.First = data.Slice(50, 50);
            writer.Second = data.Slice(0, 50);

            // All data in first span
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteAsciiNull("this is a test");

            // Data crosses spans
            data.Fill(0);
            writer.Seek(44, SeekOrigin.Begin);
            writer.WriteAsciiNull("this is a test");

            // All data in second span
            data.Fill(0);
            writer.Seek(60, SeekOrigin.Begin);
            writer.WriteAsciiNull("this is a test");
        }

        [Fact]
        public void TestWriteLittleUniNull()
        {
            Span<byte> data = new byte[100];
            Pipe.BufferWriter writer = new Pipe.BufferWriter();
            writer.First = data.Slice(50, 50);
            writer.Second = data.Slice(0, 50);

            // All data in first span
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteLittleUniNull("this is a test");

            // Data crosses spans without splitting any characters
            data.Fill(0);
            writer.Seek(44, SeekOrigin.Begin);
            writer.WriteLittleUniNull("this is a test");

            // Data crosses spans and splits a character
            data.Fill(0);
            writer.Seek(45, SeekOrigin.Begin);
            writer.WriteLittleUniNull("this is a test");

            // All data in second span
            data.Fill(0);
            writer.Seek(60, SeekOrigin.Begin);
            writer.WriteLittleUniNull("this is a test");
        }

        [Fact]
        public void TestWriteLittleUniFixed()
        {
            Span<byte> data = new byte[100];
            Pipe.BufferWriter writer = new Pipe.BufferWriter();
            writer.First = data.Slice(50, 50);
            writer.Second = data.Slice(0, 50);

            // All data in first span
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteLittleUniFixed("this is a test", 14);

            // Data crosses spans without splitting any characters
            data.Fill(0);
            writer.Seek(44, SeekOrigin.Begin);
            writer.WriteLittleUniFixed("this is a test", 14);

            // Data crosses spans and splits a character
            data.Fill(0);
            writer.Seek(45, SeekOrigin.Begin);
            writer.WriteLittleUniFixed("this is a test", 14);

            // All data in second span
            data.Fill(0);
            writer.Seek(60, SeekOrigin.Begin);
            writer.WriteLittleUniFixed("this is a test", 14);

            // Truncate string
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteLittleUniFixed("this is a test", 4);

            // Pad string
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteLittleUniFixed("this is a test", 30);
        }

        [Fact]
        public void TestWriteBigUniNull()
        {
            Span<byte> data = new byte[100];
            Pipe.BufferWriter writer = new Pipe.BufferWriter();
            writer.First = data.Slice(50, 50);
            writer.Second = data.Slice(0, 50);

            // All data in first span
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteBigUniNull("this is a test");

            // Data crosses spans without splitting any characters
            data.Fill(0);
            writer.Seek(44, SeekOrigin.Begin);
            writer.WriteBigUniNull("this is a test");

            // Data crosses spans and splits a character
            data.Fill(0);
            writer.Seek(45, SeekOrigin.Begin);
            writer.WriteBigUniNull("this is a test");

            // All data in second span
            data.Fill(0);
            writer.Seek(60, SeekOrigin.Begin);
            writer.WriteBigUniNull("this is a test");
        }

        [Fact]
        public void TestWriteBigUniFixed()
        {
            Span<byte> data = new byte[100];
            Pipe.BufferWriter writer = new Pipe.BufferWriter();
            writer.First = data.Slice(50, 50);
            writer.Second = data.Slice(0, 50);

            // All data in first span
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteBigUniFixed("this is a test", 14);

            // Data crosses spans without splitting any characters
            data.Fill(0);
            writer.Seek(44, SeekOrigin.Begin);
            writer.WriteBigUniFixed("this is a test", 14);

            // Data crosses spans and splits a character
            data.Fill(0);
            writer.Seek(45, SeekOrigin.Begin);
            writer.WriteBigUniFixed("this is a test", 14);

            // All data in second span
            data.Fill(0);
            writer.Seek(60, SeekOrigin.Begin);
            writer.WriteBigUniFixed("this is a test", 14);

            // Truncate string
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteBigUniFixed("this is a test", 4);

            // Pad string
            data.Fill(0);
            writer.Seek(0, SeekOrigin.Begin);
            writer.WriteBigUniFixed("this is a test", 30);
        }
    }

    public class PipeTest
    {
        private async void DelayedExecute(Action action)
        {
            await Task.Delay(5);

            action();
        }

        [Fact]
        public async void Await()
        {
            var pipe = new Pipe(new byte[100]);

            var reader = pipe.Reader;
            var writer = pipe.Writer;

            DelayedExecute(() =>
            {
                // Write some data into the pipe
                var buffer = writer.GetBytes();
                Assert.True(buffer.Length == 99);

                buffer.Write((byte)1);
                buffer.Write((byte)2);
                buffer.Write((byte)3);

                writer.Advance((uint)buffer.Position);
            });

            var buffer = await reader.GetBytes();

            Assert.True(buffer.First.Length == 3);


        }
    }
}
