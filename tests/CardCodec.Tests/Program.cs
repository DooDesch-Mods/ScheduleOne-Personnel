using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Personnel.Cards;

// Card codec tests.
//   unit                 in-memory checks (round trip, 2-bit fallback, limits, hash, damage)
//   write <dir>          writes the sample cards the survival script and the website tests read
//   check <dir>          reads every PNG in <dir>; a file named *.expect-<error>.png must fail with that error
//                        ("lost" = NotACard or Damaged),
//                        every other file must decode to the hash in hashes.txt of its sample
internal static class Program
{
    private static int _failures;

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "unit";
        switch (mode)
        {
            case "unit": Unit(); break;
            case "write": WriteSamples(args[1]); break;
            case "check": Check(args[1]); break;
            default: Console.Error.WriteLine($"unknown mode '{mode}'"); return 2;
        }
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Expect(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) _failures++;
    }

    // A picture with smooth gradients and flat areas, like a real card, so filters and compression behave normally.
    private static byte[] Picture(int w, int h, int seed)
    {
        var px = new byte[w * h * 4];
        var rnd = new Random(seed);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            bool frame = x < 40 || y < 40 || x >= w - 40 || y >= h - 40;
            px[i] = frame ? (byte)30 : (byte)(x * 255 / w);
            px[i + 1] = frame ? (byte)34 : (byte)(y * 255 / h);
            px[i + 2] = frame ? (byte)40 : (byte)((x + y + rnd.Next(8)) & 255);
            px[i + 3] = 255;
        }
        return px;
    }

    private static byte[] Noise(int size, int seed)
    {
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        return b;
    }

    private static CardContents Sample(int layerKb, string meta = "{\"cardId\":\"k7m2qp\",\"creator\":\"DooDesch\"}")
    {
        var c = new CardContents
        {
            NpcJson = "{\"id\":\"tick_ruiz\",\"name\":\"Tick Ruiz\",\"saveId\":\"card_tick_ruiz\",\"lines\":{\"greeting\":[\"Yo. You got the good stuff?\"]}}",
            MetaJson = meta,
            Portrait = CardPng.EncodeRgb(64, 64, Picture(64, 64, 7)),
        };
        if (layerKb > 0) c.Layers["tattoo_arm.png"] = Noise(layerKb * 1024, 11);
        return c;
    }

    private static void Unit()
    {
        int w = CardCodec.Width, h = CardCodec.Height;
        var pic = Picture(w, h, 1);

        var small = Sample(0);
        var png = CardCodec.Write(pic, w, h, small);
        var r = CardCodec.Read(png);
        Expect(r.Ok && r.BitsPerChannel == 1, "small card round trip at 1 bit");
        Expect(r.Ok && r.Contents.NpcJson == small.NpcJson && r.Contents.MetaJson == small.MetaJson, "npc.json and meta.json survive");
        Expect(r.Ok && r.Contents.Portrait.SequenceEqual(small.Portrait), "portrait survives byte for byte");
        Expect(r.Ok && r.ContentHash == CardCodec.ContentHash(small), "hash of read card equals hash of source");

        var big = Sample(600);
        r = CardCodec.Read(CardCodec.Write(pic, w, h, big));
        Expect(r.Ok && r.BitsPerChannel == 2 && r.Contents.Layers["tattoo_arm.png"].SequenceEqual(big.Layers["tattoo_arm.png"]), "600 KB layer switches to 2 bits and survives");

        bool threw = false;
        try { CardCodec.Write(pic, w, h, Sample(900)); } catch (InvalidOperationException) { threw = true; }
        Expect(threw, "900 KB of layers is refused with a clear error");

        Expect(CardCodec.ContentHash(Sample(0)) == CardCodec.ContentHash(Sample(0, "{\"cardId\":\"other\"}")), "hash ignores meta.json");
        var changed = Sample(0);
        changed.NpcJson = changed.NpcJson.Replace("Tick", "Tock");
        Expect(CardCodec.ContentHash(changed) != CardCodec.ContentHash(small), "hash changes with npc.json");

        Expect(CardCodec.Read(CardPng.EncodeRgb(w, h, pic)).Error == CardReadError.NotACard, "plain picture reads as NotACard");
        Expect(CardCodec.Read(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }).Error == CardReadError.NotPng, "JPEG bytes read as NotPng");

        // Flip one low bit in the body: the checksum must catch it.
        var dec = CardPng.Decode(png);
        int target = 100 * 4 + 1; // pixel 100 lies inside the body of a small card
        dec.Rgba[target] ^= 1;
        Expect(CardCodec.Read(dec.Rgba, w, h).Error == CardReadError.Damaged, "one flipped body bit reads as Damaged");

        threw = false;
        var evil = Sample(0);
        evil.Layers["../evil.png"] = new byte[1];
        try { CardCodec.Write(pic, w, h, evil); } catch (InvalidOperationException) { threw = true; }
        Expect(threw, "layer name with a path is refused");

        // Alpha 0 pixels are made opaque, so no tool can drop their colour.
        var holes = (byte[])pic.Clone();
        for (int i = 3; i < holes.Length; i += 4 * 97) holes[i] = 0;
        r = CardCodec.Read(CardCodec.Write(holes, w, h, small));
        Expect(r.Ok, "transparent pixels in the source picture do not break the card");
    }

    private static void WriteSamples(string dir)
    {
        Directory.CreateDirectory(dir);
        int w = CardCodec.Width, h = CardCodec.Height;
        var lines = new List<string>();
        foreach (var (name, layerKb) in new[] { ("small", 0), ("large", 600) })
        {
            var c = Sample(layerKb);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), CardCodec.Write(Picture(w, h, 3), w, h, c));
            lines.Add($"{name} {CardCodec.ContentHash(c)}");
            Console.WriteLine($"wrote {name}.png");
        }
        File.WriteAllLines(Path.Combine(dir, "hashes.txt"), lines);
    }

    private static void Check(string dir)
    {
        var hashes = File.ReadAllLines(Path.Combine(dir, "hashes.txt"))
            .Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1]);
        foreach (string file in Directory.GetFiles(dir, "*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var r = CardCodec.Read(File.ReadAllBytes(file));
            int at = name.IndexOf(".expect-", StringComparison.Ordinal);
            if (at >= 0)
            {
                // "lost" accepts both ways a re-encode shows: header gone (NotACard) or body changed (Damaged).
                string want = name.Substring(at + 8);
                bool match = want == "lost"
                    ? r.Error == CardReadError.NotACard || r.Error == CardReadError.Damaged
                    : string.Equals(r.Error.ToString(), want, StringComparison.OrdinalIgnoreCase);
                Expect(!r.Ok && match,
                    $"{Path.GetFileName(file)} -> {r.Error} ({r.Message})");
            }
            else
            {
                string sample = name.Split('.')[0];
                Expect(r.Ok && hashes.TryGetValue(sample, out var h) && h == r.ContentHash,
                    $"{Path.GetFileName(file)} -> {(r.Ok ? "ok " + r.BitsPerChannel + " bit" : r.Error + " " + r.Message)}");
            }
        }
    }
}
