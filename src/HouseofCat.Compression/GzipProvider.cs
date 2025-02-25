﻿using HouseofCat.Utilities.Errors;
using CommunityToolkit.HighPerformance;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace HouseofCat.Compression
{
    public class GzipProvider : ICompressionProvider
    {
        public string Type { get; } = "GZIP";
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

        public ArraySegment<byte> Compress(ReadOnlyMemory<byte> inputData)
        {
            Guard.AgainstEmpty(inputData, nameof(inputData));

            using var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel, false))
            {
                gzipStream.Write(inputData.Span);
            }

            if (compressedStream.TryGetBuffer(out var buffer))
            { return buffer; }
            else
            { return compressedStream.ToArray(); }
        }

        public async ValueTask<ArraySegment<byte>> CompressAsync(ReadOnlyMemory<byte> inputData)
        {
            Guard.AgainstEmpty(inputData, nameof(inputData));

            using var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel, false))
            {
                await gzipStream
                    .WriteAsync(inputData)
                    .ConfigureAwait(false);
            }

            if (compressedStream.TryGetBuffer(out var buffer))
            { return buffer; }
            else
            { return compressedStream.ToArray(); }
        }

        /// <summary>
        /// Retrieve a new <c>MemoryStream</c> object with the contents unzipped and copied from the provided
        /// stream. The provided stream is optionally closed.
        /// </summary>
        /// <remarks>The new stream's position is set to the beginning of the stream when returned.</remarks>
        /// <param name="data"></param>
        /// <returns></returns>
        public MemoryStream Compress(Stream inputStream, bool leaveStreamOpen = false)
        {
            Guard.AgainstNullOrEmpty(inputStream, nameof(inputStream));

            if (inputStream.Position == inputStream.Length) { inputStream.Seek(0, SeekOrigin.Begin); }

            var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel, true))
            {
                inputStream.CopyTo(gzipStream);
            }
            if (!leaveStreamOpen) { inputStream.Close(); }

            compressedStream.Seek(0, SeekOrigin.Begin);
            return compressedStream;
        }

        /// <summary>
        /// Retrieve a new <c>MemoryStream</c> object with the contents unzipped and copied from the provided
        /// stream. The provided stream is optionally closed.
        /// </summary>
        /// <remarks>The new stream's position is set to the beginning of the stream when returned.</remarks>
        /// <param name="data"></param>
        /// <returns></returns>
        public async ValueTask<MemoryStream> CompressAsync(Stream inputStream, bool leaveStreamOpen = false)
        {
            Guard.AgainstNullOrEmpty(inputStream, nameof(inputStream));

            if (inputStream.Position == inputStream.Length) { inputStream.Seek(0, SeekOrigin.Begin); }

            var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel, true))
            {
                await inputStream
                    .CopyToAsync(gzipStream)
                    .ConfigureAwait(false);
            }
            if (!leaveStreamOpen) { inputStream.Close(); }

            compressedStream.Seek(0, SeekOrigin.Begin);
            return compressedStream;
        }

        /// <summary>
        /// Retrieve a new <c>MemoryStream</c> object with the contents contained zipped data writen from the unzipped
        /// bytes in <c>ReadOnlyMemory&lt;byte&gt;</c>.
        /// </summary>
        /// <remarks>The new stream's position is set to the beginning of the stream when returned.</remarks>
        /// <param name="data"></param>
        /// <returns></returns>
        public MemoryStream CompressToStream(ReadOnlyMemory<byte> inputData)
        {
            Guard.AgainstEmpty(inputData, nameof(inputData));

            var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel, true))
            {
                gzipStream.Write(inputData.Span);
            }

            compressedStream.Seek(0, SeekOrigin.Begin);
            return compressedStream;
        }

        /// <summary>
        /// Retrieve a new <c>MemoryStream</c> object with the contents contained zipped data writen from the unzipped
        /// bytes in <c>ReadOnlyMemory&lt;byte&gt;</c>.
        /// </summary>
        /// <remarks>The new stream's position is set to the beginning of the stream when returned.</remarks>
        /// <param name="data"></param>
        /// <returns></returns>
        public async ValueTask<MemoryStream> CompressToStreamAsync(ReadOnlyMemory<byte> compressedData)
        {
            Guard.AgainstEmpty(compressedData, nameof(compressedData));

            var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel, true))
            {
                await gzipStream
                    .WriteAsync(compressedData)
                    .ConfigureAwait(false);
            }

            compressedStream.Seek(0, SeekOrigin.Begin);
            return compressedStream;
        }

        public ArraySegment<byte> Decompress(ReadOnlyMemory<byte> compressedData)
        {
            Guard.AgainstEmpty(compressedData, nameof(compressedData));

            using var uncompressedStream = new MemoryStream(CompressionHelpers.GetGzipUncompressedLength(compressedData));
            using (var gzipStream = new GZipStream(compressedData.AsStream(), CompressionMode.Decompress, false))
            {
                gzipStream.CopyTo(uncompressedStream);
            }

            if (uncompressedStream.TryGetBuffer(out var buffer))
            { return buffer; }
            else
            { return uncompressedStream.ToArray(); }
        }

        public async ValueTask<ArraySegment<byte>> DecompressAsync(ReadOnlyMemory<byte> compressedData)
        {
            Guard.AgainstEmpty(compressedData, nameof(compressedData));

            using var uncompressedStream = new MemoryStream(CompressionHelpers.GetGzipUncompressedLength(compressedData));
            using (var gzipStream = new GZipStream(compressedData.AsStream(), CompressionMode.Decompress, false))
            {
                await gzipStream
                    .CopyToAsync(uncompressedStream)
                    .ConfigureAwait(false);
            }

            if (uncompressedStream.TryGetBuffer(out var buffer))
            { return buffer; }
            else
            { return uncompressedStream.ToArray(); }
        }

        /// <summary>
        /// Returns a new <c>MemoryStream</c> that has decompressed data inside. The provided stream is optionally closed.
        /// </summary>
        /// <remarks>The new stream's position is set to the beginning of the stream when returned.</remarks>
        /// <param name="compressedStream"></param>
        /// <returns></returns>
        public MemoryStream Decompress(Stream compressedStream, bool leaveStreamOpen = false)
        {
            Guard.AgainstNullOrEmpty(compressedStream, nameof(compressedStream));

            if (compressedStream.Position == compressedStream.Length) { compressedStream.Seek(0, SeekOrigin.Begin); }

            var uncompressedStream = new MemoryStream(CompressionHelpers.GetGzipUncompressedLength(compressedStream));
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress, leaveStreamOpen))
            {
                gzipStream.CopyTo(uncompressedStream);
            }

            return uncompressedStream;
        }

        /// <summary>
        /// Returns a new <c>MemoryStream</c> that has decompressed data inside. The provided stream is optionally closed.
        /// </summary>
        /// <param name="compressedStream"></param>
        /// <returns></returns>
        public async ValueTask<MemoryStream> DecompressAsync(Stream compressedStream, bool leaveStreamOpen = false)
        {
            Guard.AgainstNullOrEmpty(compressedStream, nameof(compressedStream));

            if (compressedStream.Position == compressedStream.Length) { compressedStream.Seek(0, SeekOrigin.Begin); }

            var uncompressedStream = new MemoryStream(CompressionHelpers.GetGzipUncompressedLength(compressedStream));
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress, leaveStreamOpen))
            {
                await gzipStream
                    .CopyToAsync(uncompressedStream)
                    .ConfigureAwait(false);
            }

            return uncompressedStream;
        }

        /// <summary>
        /// Returns a new <c>MemoryStream</c> that has decompressed data inside.
        /// </summary>
        /// <param name="compressedData"></param>
        /// <returns>A <c>new MemoryStream</c>.</returns>
        public MemoryStream DecompressToStream(ReadOnlyMemory<byte> compressedData)
        {
            var uncompressedStream = new MemoryStream(CompressionHelpers.GetGzipUncompressedLength(compressedData));
            using (var gzipStream = new GZipStream(compressedData.AsStream(), CompressionMode.Decompress, false))
            {
                gzipStream.CopyTo(uncompressedStream);
            }

            return uncompressedStream;
        }
    }
}
