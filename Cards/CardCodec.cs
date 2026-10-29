using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Personnel.Cards
{
    /// <summary>What a card carries: one NPC entry of pack schema 3 plus the files it needs.</summary>
    public sealed class CardContents
    {
        /// <summary>The NPC entry as JSON (one element of a pack manifest's <c>npcs</c> array).</summary>
        public string NpcJson;

        /// <summary>Card metadata as JSON (card id, creator, date, editor version, original of a remix). Not part of the hash.</summary>
        public string MetaJson;

        /// <summary>Contact portrait PNG, or null.</summary>
        public byte[] Portrait;

        /// <summary>Extra image files by file name (tattoo art), stored under <c>layers/</c>.</summary>
        public Dictionary<string, byte[]> Layers = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }

    /// <summary>Why a card could not be read.</summary>
    public enum CardReadError
    {
        None,
        /// <summary>The file is not a PNG.</summary>
        NotPng,
        /// <summary>A PNG without card data.</summary>
        NotACard,
        /// <summary>The card header is there but the data does not match its checksum (lossy re-encode, edited pixels).</summary>
        Damaged,
        /// <summary>The card was written by a newer format version.</summary>
        TooNew,
        /// <summary>The data decodes but its contents break the format rules.</summary>
        Invalid,
    }

    /// <summary>Result of <see cref="CardCodec.Read"/>.</summary>
    public sealed class CardReadResult
    {
        public CardReadError Error;
        /// <summary>Readable reason when <see cref="Error"/> is not None.</summary>
        public string Message;
        public CardContents Contents;
        /// <summary>Duplicate key over everything except meta.json, lowercase hex SHA-256.</summary>
        public string ContentHash;
        public int Version;
        public int BitsPerChannel;

        public bool Ok => Error == CardReadError.None;
    }

    /// <summary>
    /// NPC Card v1: the card data hides in the lowest bits of the R, G and B channels of an opaque PNG. Channels run
    /// row by row from the top left, R then G then B of each pixel. The first 112 channels carry the header at one
    /// bit each: magic "NPCV", version, bits per channel, body length (uint32 big-endian), CRC-32 of the body. The
    /// body follows from channel 112 on with 1 or 2 bits per channel, most significant bit first. The body is a ZIP
    /// with npc.json, meta.json, portrait.png and layers/*.png. The website reads the same layout
    /// (PersonifyWeb src/lib/card), so both sides must change together.
    /// </summary>
    public static class CardCodec
    {
        public const int FormatVersion = 1;
        public const int Width = 900;
        public const int Height = 1260;

        private static readonly byte[] Magic = { (byte)'N', (byte)'P', (byte)'C', (byte)'V' };
        private const int HeaderBytes = 14;
        private const int HeaderChannels = HeaderBytes * 8;

        /// <summary>Upper bound for the unpacked ZIP, so a crafted card cannot inflate into a memory bomb.</summary>
        public const long MaxUnpackedBytes = 8L * 1024 * 1024;

        public const string NpcEntry = "npc.json";
        public const string MetaEntry = "meta.json";
        public const string PortraitEntry = "portrait.png";
        public const string LayersPrefix = "layers/";

        /// <summary>Body bytes an image of this size holds at the given bits per channel.</summary>
        public static int Capacity(int width, int height, int bitsPerChannel)
        {
            long channels = (long)width * height * 3 - HeaderChannels;
            return channels <= 0 ? 0 : (int)Math.Min(int.MaxValue, channels * bitsPerChannel / 8);
        }

        /// <summary>
        /// Packs the contents and hides them in <paramref name="rgba"/> (8-bit RGBA, rows top to bottom, changed in
        /// place). Uses 1 bit per channel when the data fits, otherwise 2. Throws when it does not fit at all.
        /// </summary>
        public static int Embed(byte[] rgba, int width, int height, CardContents contents)
        {
            if (rgba == null || rgba.Length != width * height * 4) throw new ArgumentException("pixel buffer does not match the size");
            byte[] body = Pack(contents);
            int bpc = body.Length <= Capacity(width, height, 1) ? 1 : body.Length <= Capacity(width, height, 2) ? 2 : 0;
            if (bpc == 0)
                throw new InvalidOperationException($"card data is {body.Length / 1024} KB, the image holds {Capacity(width, height, 2) / 1024} KB");

            var header = new byte[HeaderBytes];
            Buffer.BlockCopy(Magic, 0, header, 0, 4);
            header[4] = FormatVersion;
            header[5] = (byte)bpc;
            WriteUInt(header, 6, (uint)body.Length);
            WriteUInt(header, 10, Crc32.Compute(body));

            var w = new ChannelCursor(rgba);
            for (int i = 0; i < HeaderBytes; i++)
                for (int b = 7; b >= 0; b--) w.Write((header[i] >> b) & 1, 1);
            int mask = (1 << bpc) - 1;
            for (int i = 0; i < body.Length; i++)
                for (int shift = 8 - bpc; shift >= 0; shift -= bpc) w.Write((body[i] >> shift) & mask, bpc);

            // Every pixel must be opaque: some tools drop or premultiply colour under transparent pixels, which would
            // wipe the bits there.
            for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
            return bpc;
        }

        /// <summary>Embeds the contents into the picture and returns the finished card PNG.</summary>
        public static byte[] Write(byte[] rgba, int width, int height, CardContents contents)
        {
            var copy = (byte[])rgba.Clone();
            Embed(copy, width, height, contents);
            return CardPng.EncodeRgb(width, height, copy);
        }

        /// <summary>Reads a card PNG. Never throws for bad input; the result names the reason.</summary>
        public static CardReadResult Read(byte[] png)
        {
            if (!CardPng.IsPng(png)) return Fail(CardReadError.NotPng, "This file is not a PNG image.");
            CardPng.Image img;
            try { img = CardPng.Decode(png); }
            catch (Exception ex) { return Fail(CardReadError.NotPng, "The PNG could not be decoded: " + ex.Message); }
            return Read(img.Rgba, img.Width, img.Height);
        }

        /// <summary>Reads card data from decoded 8-bit RGBA pixels (rows top to bottom).</summary>
        public static CardReadResult Read(byte[] rgba, int width, int height)
        {
            if ((long)width * height * 3 < HeaderChannels) return Fail(CardReadError.NotACard, "This image is too small to be a card.");
            var r = new ChannelCursor(rgba);
            var header = new byte[HeaderBytes];
            for (int i = 0; i < HeaderBytes; i++)
            {
                int v = 0;
                for (int b = 0; b < 8; b++) v = (v << 1) | r.Read(1);
                header[i] = (byte)v;
            }
            for (int i = 0; i < 4; i++)
                if (header[i] != Magic[i]) return Fail(CardReadError.NotACard, "This image has no NPC card data.");

            int version = header[4], bpc = header[5];
            if (version > FormatVersion) return Fail(CardReadError.TooNew, $"This card uses format {version}; update Personnel to read it.");
            if (version < 1 || (bpc != 1 && bpc != 2)) return Fail(CardReadError.Damaged, "The card header is damaged.");
            uint length = ReadUInt(header, 6), crc = ReadUInt(header, 10);
            if (length == 0 || length > Capacity(width, height, bpc)) return Fail(CardReadError.Damaged, "The card header is damaged.");

            var body = new byte[length];
            for (int i = 0; i < body.Length; i++)
            {
                int v = 0;
                for (int n = 0; n < 8; n += bpc) v = (v << bpc) | r.Read(bpc);
                body[i] = (byte)v;
            }
            if (Crc32.Compute(body) != crc)
                return Fail(CardReadError.Damaged, "This image lost its card data. It was probably compressed or converted; use the original PNG.");

            CardContents contents;
            try { contents = Unpack(body); }
            catch (Exception ex) { return Fail(CardReadError.Invalid, "The card data is not valid: " + ex.Message); }

            return new CardReadResult
            {
                Contents = contents,
                ContentHash = ContentHash(contents),
                Version = version,
                BitsPerChannel = bpc,
            };
        }

        /// <summary>
        /// Duplicate key: SHA-256 over every entry except meta.json, sorted by name (ordinal). Each entry adds its
        /// UTF-8 name, a zero byte, its length as uint64 big-endian, then its bytes.
        /// </summary>
        public static string ContentHash(CardContents contents)
        {
            using var sha = SHA256.Create();
            using var buffer = new MemoryStream();
            foreach (var (name, data) in Entries(contents).Where(e => e.name != MetaEntry).OrderBy(e => e.name, StringComparer.Ordinal))
            {
                byte[] n = Encoding.UTF8.GetBytes(name);
                buffer.Write(n, 0, n.Length);
                buffer.WriteByte(0);
                var len = new byte[8];
                ulong l = (ulong)data.Length;
                for (int i = 7; i >= 0; i--) { len[i] = (byte)l; l >>= 8; }
                buffer.Write(len, 0, 8);
                buffer.Write(data, 0, data.Length);
            }
            return string.Concat(sha.ComputeHash(buffer.ToArray()).Select(b => b.ToString("x2")));
        }

        /// <summary>True for a layer file name a card may carry: one path segment, safe characters, .png.</summary>
        public static bool IsSafeLayerName(string name) =>
            !string.IsNullOrEmpty(name) && name.Length <= 96 && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            name[0] != '.' && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.');

        private static IEnumerable<(string name, byte[] data)> Entries(CardContents c)
        {
            if (string.IsNullOrWhiteSpace(c.NpcJson)) throw new InvalidOperationException("card has no npc.json");
            yield return (NpcEntry, Encoding.UTF8.GetBytes(c.NpcJson));
            if (c.MetaJson != null) yield return (MetaEntry, Encoding.UTF8.GetBytes(c.MetaJson));
            if (c.Portrait != null) yield return (PortraitEntry, c.Portrait);
            foreach (var kv in c.Layers)
            {
                if (!IsSafeLayerName(kv.Key)) throw new InvalidOperationException($"layer file name '{kv.Key}' is not allowed");
                yield return (LayersPrefix + kv.Key, kv.Value);
            }
        }

        private static byte[] Pack(CardContents contents)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var (name, data) in Entries(contents).OrderBy(e => e.name, StringComparer.Ordinal))
                {
                    // PNGs are already deflated; storing them saves time and changes nothing in size.
                    var level = name.EndsWith(".png", StringComparison.Ordinal) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    var entry = zip.CreateEntry(name, level);
                    entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var s = entry.Open();
                    s.Write(data, 0, data.Length);
                }
            }
            return ms.ToArray();
        }

        private static CardContents Unpack(byte[] body)
        {
            var c = new CardContents();
            long total = 0;
            using var zip = new ZipArchive(new MemoryStream(body), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                total += entry.Length;
                if (entry.Length < 0 || total > MaxUnpackedBytes) throw new InvalidDataException("card data is too large");
                byte[] data = ReadAll(entry);
                if (data.LongLength != entry.Length) throw new InvalidDataException($"entry {entry.FullName} has the wrong size");

                string name = entry.FullName;
                if (name == NpcEntry) c.NpcJson = Encoding.UTF8.GetString(data);
                else if (name == MetaEntry) c.MetaJson = Encoding.UTF8.GetString(data);
                else if (name == PortraitEntry) c.Portrait = data;
                else if (name.StartsWith(LayersPrefix, StringComparison.Ordinal) && IsSafeLayerName(name.Substring(LayersPrefix.Length)))
                    c.Layers[name.Substring(LayersPrefix.Length)] = data;
                else throw new InvalidDataException($"unexpected file '{name}'");
            }
            if (string.IsNullOrWhiteSpace(c.NpcJson)) throw new InvalidDataException("npc.json is missing");
            return c;
        }

        private static byte[] ReadAll(ZipArchiveEntry entry)
        {
            using var s = entry.Open();
            using var o = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                o.Write(buf, 0, n);
                if (o.Length > MaxUnpackedBytes) throw new InvalidDataException("card data is too large");
            }
            return o.ToArray();
        }

        private static CardReadResult Fail(CardReadError e, string message) => new CardReadResult { Error = e, Message = message };

        private static void WriteUInt(byte[] d, int o, uint v)
        {
            d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v;
        }

        private static uint ReadUInt(byte[] d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

        /// <summary>Walks the R, G, B channels of an RGBA buffer, skipping alpha.</summary>
        private sealed class ChannelCursor
        {
            private readonly byte[] _rgba;
            private int _index; // byte index into rgba

            public ChannelCursor(byte[] rgba) { _rgba = rgba; }

            private int Next()
            {
                if ((_index & 3) == 3) _index++;
                if (_index >= _rgba.Length) throw new InvalidOperationException("ran past the end of the image");
                return _index++;
            }

            public void Write(int value, int bits)
            {
                int i = Next();
                int mask = (1 << bits) - 1;
                _rgba[i] = (byte)((_rgba[i] & ~mask) | (value & mask));
            }

            public int Read(int bits) => _rgba[Next()] & ((1 << bits) - 1);
        }
    }
}
