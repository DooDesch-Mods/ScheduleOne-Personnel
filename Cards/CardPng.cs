using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Personnel.Cards
{
    /// <summary>
    /// Reads and writes PNG files without Unity, so the card codec runs (and is tested) outside the game. Reading
    /// accepts every 8- and 16-bit colour type and Adam7 interlacing, because an image host or an optimiser may hand
    /// back a card in a different, still lossless, PNG layout than the one the game wrote. Writing produces 8-bit RGB,
    /// which is all an opaque card needs.
    /// </summary>
    public static class CardPng
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>A decoded image: 8-bit RGBA, rows top to bottom.</summary>
        public sealed class Image
        {
            public int Width;
            public int Height;
            public byte[] Rgba;
        }

        /// <summary>True when the bytes start with the PNG signature.</summary>
        public static bool IsPng(byte[] data)
        {
            if (data == null || data.Length < Signature.Length) return false;
            for (int i = 0; i < Signature.Length; i++) if (data[i] != Signature[i]) return false;
            return true;
        }

        /// <summary>Decodes a PNG. Throws <see cref="InvalidDataException"/> with a readable reason on a broken file.</summary>
        public static Image Decode(byte[] data)
        {
            if (!IsPng(data)) throw new InvalidDataException("not a PNG file");
            int pos = Signature.Length;
            int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
            byte[] palette = null, paletteAlpha = null;
            var idat = new MemoryStream();
            bool sawEnd = false;

            while (pos + 8 <= data.Length)
            {
                int length = ReadInt(data, pos);
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                int body = pos + 8;
                if (length < 0 || body + length + 4 > data.Length) throw new InvalidDataException($"chunk {type} runs past the end of the file");
                switch (type)
                {
                    case "IHDR":
                        width = ReadInt(data, body);
                        height = ReadInt(data, body + 4);
                        bitDepth = data[body + 8];
                        colorType = data[body + 9];
                        interlace = data[body + 12];
                        break;
                    case "PLTE":
                        palette = new byte[length];
                        Buffer.BlockCopy(data, body, palette, 0, length);
                        break;
                    case "tRNS":
                        paletteAlpha = new byte[length];
                        Buffer.BlockCopy(data, body, paletteAlpha, 0, length);
                        break;
                    case "IDAT":
                        idat.Write(data, body, length);
                        break;
                    case "IEND":
                        sawEnd = true;
                        break;
                }
                pos = body + length + 4;
                if (sawEnd) break;
            }

            if (width <= 0 || height <= 0) throw new InvalidDataException("PNG has no valid IHDR");
            if ((long)width * height > 16_000_000) throw new InvalidDataException($"PNG is too large ({width}x{height})");
            if (bitDepth != 8 && bitDepth != 16 && !(colorType == 3 || colorType == 0)) throw new InvalidDataException($"unsupported bit depth {bitDepth}");
            if (colorType == 3 && palette == null) throw new InvalidDataException("palette PNG without PLTE");

            byte[] raw;
            using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                z.CopyTo(o);
                raw = o.ToArray();
            }

            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException($"unsupported colour type {colorType}") };
            int bitsPerPixel = channels * bitDepth;
            var rgba = new byte[width * height * 4];

            if (interlace == 0)
            {
                Unfilter(raw, 0, width, height, bitsPerPixel, out _, (x, y, row, offset) => Put(rgba, width, x, y, row, offset, colorType, bitDepth, palette, paletteAlpha));
            }
            else
            {
                // Adam7: seven passes, each a smaller image placed on a fixed grid.
                int[] sx = { 0, 4, 0, 2, 0, 1, 0 }, sy = { 0, 0, 4, 0, 2, 0, 1 };
                int[] dx = { 8, 8, 4, 4, 2, 2, 1 }, dy = { 8, 8, 8, 4, 4, 2, 2 };
                int offset = 0;
                for (int p = 0; p < 7; p++)
                {
                    int pw = (width - sx[p] + dx[p] - 1) / dx[p];
                    int ph = (height - sy[p] + dy[p] - 1) / dy[p];
                    if (pw <= 0 || ph <= 0) continue;
                    int pass = p;
                    Unfilter(raw, offset, pw, ph, bitsPerPixel, out int used,
                        (x, y, row, o) => Put(rgba, width, sx[pass] + x * dx[pass], sy[pass] + y * dy[pass], row, o, colorType, bitDepth, palette, paletteAlpha));
                    offset += used;
                }
            }
            return new Image { Width = width, Height = height, Rgba = rgba };
        }

        /// <summary>Encodes 8-bit RGBA as an 8-bit RGB PNG (alpha is dropped; cards are opaque).</summary>
        public static byte[] EncodeRgb(int width, int height, byte[] rgba)
        {
            if (rgba == null || rgba.Length != width * height * 4) throw new ArgumentException("pixel buffer does not match the size");
            int stride = width * 3;
            var prev = new byte[stride];
            var cur = new byte[stride];
            var best = new byte[stride + 1];
            var trial = new byte[stride + 1];
            var raw = new MemoryStream(height * (stride + 1));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int s = (y * width + x) * 4;
                    cur[x * 3] = rgba[s];
                    cur[x * 3 + 1] = rgba[s + 1];
                    cur[x * 3 + 2] = rgba[s + 2];
                }
                // Pick the filter with the smallest sum of absolute values, the usual heuristic that keeps the file
                // small without trying every compression.
                long bestScore = long.MaxValue;
                for (byte f = 0; f <= 4; f++)
                {
                    trial[0] = f;
                    long score = 0;
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= 3 ? cur[i - 3] : 0, b = prev[i], c = i >= 3 ? prev[i - 3] : 0;
                        int v = f switch
                        {
                            0 => cur[i],
                            1 => cur[i] - a,
                            2 => cur[i] - b,
                            3 => cur[i] - ((a + b) >> 1),
                            _ => cur[i] - Paeth(a, b, c),
                        };
                        trial[i + 1] = (byte)v;
                        score += Math.Abs((int)(sbyte)(byte)v);
                    }
                    if (score < bestScore) { bestScore = score; Buffer.BlockCopy(trial, 0, best, 0, stride + 1); }
                }
                raw.Write(best, 0, stride + 1);
                var t = prev; prev = cur; cur = t;
            }

            byte[] compressed;
            using (var o = new MemoryStream())
            {
                using (var z = new ZLibStream(o, CompressionLevel.Optimal, true)) { raw.Position = 0; raw.CopyTo(z); }
                compressed = o.ToArray();
            }

            var png = new MemoryStream();
            png.Write(Signature, 0, Signature.Length);
            var ihdr = new byte[13];
            WriteInt(ihdr, 0, width);
            WriteInt(ihdr, 4, height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 2;  // RGB
            WriteChunk(png, "IHDR", ihdr);
            WriteChunk(png, "IDAT", compressed);
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private delegate void PixelSink(int x, int y, byte[] row, int offset);

        // Reverses the per-row filters of one (sub)image starting at raw[start]; calls sink for every pixel with the
        // unfiltered row and the pixel's bit offset in it.
        private static void Unfilter(byte[] raw, int start, int width, int height, int bitsPerPixel, out int used, PixelSink sink)
        {
            int stride = (width * bitsPerPixel + 7) / 8;
            int bpp = Math.Max(1, bitsPerPixel / 8);
            var prev = new byte[stride];
            var cur = new byte[stride];
            int pos = start;
            for (int y = 0; y < height; y++)
            {
                if (pos + 1 + stride > raw.Length) throw new InvalidDataException("image data is shorter than the image");
                byte filter = raw[pos++];
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int v = raw[pos + i];
                    cur[i] = filter switch
                    {
                        0 => (byte)v,
                        1 => (byte)(v + a),
                        2 => (byte)(v + b),
                        3 => (byte)(v + ((a + b) >> 1)),
                        4 => (byte)(v + Paeth(a, b, c)),
                        _ => throw new InvalidDataException($"unknown PNG filter {filter}"),
                    };
                }
                pos += stride;
                for (int x = 0; x < width; x++) sink(x, y, cur, x * bitsPerPixel);
                var t = prev; prev = cur; cur = t;
                Array.Clear(cur, 0, cur.Length);
            }
            used = pos - start;
        }

        private static void Put(byte[] rgba, int width, int x, int y, byte[] row, int bitOffset, int colorType, int bitDepth, byte[] palette, byte[] paletteAlpha)
        {
            int d = (y * width + x) * 4;
            // 16-bit samples keep their high byte: a lossless 8 -> 16 bit conversion writes v * 257, whose high byte
            // is v again, so the hidden bits survive.
            byte Sample(int index)
            {
                if (bitDepth == 16) return row[bitOffset / 8 + index * 2];
                if (bitDepth == 8) return row[bitOffset / 8 + index];
                int bit = bitOffset + index * bitDepth;
                int v = (row[bit / 8] >> (8 - bitDepth - bit % 8)) & ((1 << bitDepth) - 1);
                return colorType == 3 ? (byte)v : (byte)(v * 255 / ((1 << bitDepth) - 1));
            }
            switch (colorType)
            {
                case 0: { byte g = Sample(0); rgba[d] = g; rgba[d + 1] = g; rgba[d + 2] = g; rgba[d + 3] = 255; break; }
                case 2: rgba[d] = Sample(0); rgba[d + 1] = Sample(1); rgba[d + 2] = Sample(2); rgba[d + 3] = 255; break;
                case 3:
                {
                    int i = Sample(0);
                    if (i * 3 + 2 >= palette.Length) throw new InvalidDataException("palette index out of range");
                    rgba[d] = palette[i * 3]; rgba[d + 1] = palette[i * 3 + 1]; rgba[d + 2] = palette[i * 3 + 2];
                    rgba[d + 3] = paletteAlpha != null && i < paletteAlpha.Length ? paletteAlpha[i] : (byte)255;
                    break;
                }
                case 4: { byte g = Sample(0); rgba[d] = g; rgba[d + 1] = g; rgba[d + 2] = g; rgba[d + 3] = Sample(1); break; }
                case 6: rgba[d] = Sample(0); rgba[d + 1] = Sample(1); rgba[d + 2] = Sample(2); rgba[d + 3] = Sample(3); break;
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static int ReadInt(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

        private static void WriteInt(byte[] d, int o, int v)
        {
            d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v;
        }

        private static void WriteChunk(Stream s, string type, byte[] body)
        {
            var len = new byte[4];
            WriteInt(len, 0, body.Length);
            s.Write(len, 0, 4);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            s.Write(body, 0, body.Length);
            uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, typeBytes, 0, 4), body, 0, body.Length) ^ 0xFFFFFFFFu;
            var c = new byte[4];
            WriteInt(c, 0, (int)crc);
            s.Write(c, 0, 4);
        }
    }

    /// <summary>CRC-32 (IEEE), as PNG and ZIP use it.</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = Build();

        private static uint[] Build()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        public static uint Update(uint crc, byte[] data, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++) crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        public static uint Compute(byte[] data) => Update(0xFFFFFFFFu, data, 0, data.Length) ^ 0xFFFFFFFFu;
    }
}
