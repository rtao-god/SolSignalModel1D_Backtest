using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	public static class NdjsonReaders
		{
		public static List<Candle1m> Load1mRange ( string baseDir, string symbol, DateTime fromUtc, DateTime toUtc )
			{
			var path = Path.Combine (baseDir, $"{symbol}-1m.ndjson");
			if (!File.Exists (path))
				throw new FileNotFoundException ($"ndjson not found: {path}");

			var list = new List<Candle1m> (1024);
			using var sr = new StreamReader (path);
			string? line;
			while ((line = sr.ReadLine ()) != null)
				{
				if (string.IsNullOrWhiteSpace (line)) continue;
				using var doc = JsonDocument.Parse (line);
				var root = doc.RootElement;

				var t = DateTime.Parse (root.GetProperty ("t").GetString ()!,
									   null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
				if (t < fromUtc) continue;
				if (t >= toUtc) break;

				double o = root.GetProperty ("o").GetDouble ();
				double h = root.GetProperty ("h").GetDouble ();
				double l = root.GetProperty ("l").GetDouble ();
				double c = root.GetProperty ("c").GetDouble ();

				list.Add (new Candle1m { OpenTimeUtc = t, Open = o, High = h, Low = l, Close = c });
				}
			return list;
			}
		}
	}
