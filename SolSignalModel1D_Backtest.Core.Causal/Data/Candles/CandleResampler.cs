using System;
using System.Collections.Generic;
using System.IO;
using SolSignalModel1D_Backtest.Core.Causal.Infra;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles
	{
	/// <summary>
	/// Ресэмплер: собирает 6h из 1h или 1m NDJSON.
	/// Пишет в cache/candles/{SYMBOL}-6h.ndjson.
	/// Используется как fallback, если 6h-файл ещё не построен напрямую.
	/// </summary>
	public static class CandleResampler
		{
		private static string Path6h ( string symbol ) => CandlePaths.File (symbol, "6h");
		private static string Path1h ( string symbol ) => CandlePaths.File (symbol, "1h");
		private static string Path1m ( string symbol ) => CandlePaths.File (symbol, "1m");

		public static void Ensure6hAvailable ( string symbol )
			{
			Directory.CreateDirectory (PathConfig.CandlesDir);

			var p6 = Path6h (symbol);
			if (File.Exists (p6) && new FileInfo (p6).Length > 0)
				{
				return;
				}

			var p1h = Path1h (symbol);
			if (File.Exists (p1h) && new FileInfo (p1h).Length > 0)
				{
				var oneHour = ReadAllLines (p1h);
				var sixHour = ResampleTo6h (oneHour, sourceIsOneHour: true);
				WriteAll (p6, sixHour);
				return;
				}

			var p1m = Path1m (symbol);
			if (File.Exists (p1m) && new FileInfo (p1m).Length > 0)
				{
				var oneMin = ReadAllLines (p1m);
				var sixHour = ResampleTo6h (oneMin, sourceIsOneHour: false);
				WriteAll (p6, sixHour);
				return;
				}

			throw new InvalidOperationException (
				$"[resample] Нет ни 1h, ни 1m свечей для {symbol} в {PathConfig.CandlesDir}. " +
				$"Ожидались файлы: {Path.GetFileName (p1h)} или {Path.GetFileName (p1m)}");
			}

		private static List<CandleNdjsonStore.CandleLine> ReadAllLines ( string path )
			{
			var st = new CandleNdjsonStore (path);
			return st.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			}

		private static void WriteAll ( string path, IEnumerable<CandleNdjsonStore.CandleLine> lines )
			{
			if (File.Exists (path)) File.Delete (path);
			var store = new CandleNdjsonStore (path);
			store.Append (lines);
			}

		private static List<CandleNdjsonStore.CandleLine> ResampleTo6h (
			List<CandleNdjsonStore.CandleLine> src,
			bool sourceIsOneHour )
			{
			src.Sort (( a, b ) => a.OpenTimeUtc.CompareTo (b.OpenTimeUtc));
			var result = new List<CandleNdjsonStore.CandleLine> ();
			if (src.Count == 0) return result;

			DateTime Anchor6h ( DateTime tUtc )
				{
				var h = (tUtc.Hour / 6) * 6;
				return new DateTime (tUtc.Year, tUtc.Month, tUtc.Day, h, 0, 0, DateTimeKind.Utc);
				}

			DateTime curAnchor = Anchor6h (src[0].OpenTimeUtc);
			double open = src[0].Open;
			double high = src[0].High;
			double low = src[0].Low;
			double close = src[0].Close;

			void FlushIfAny ()
				{
				result.Add (new CandleNdjsonStore.CandleLine (curAnchor, open, high, low, close));
				}

			foreach (var c in src)
				{
				var a = Anchor6h (c.OpenTimeUtc);

				if (a != curAnchor)
					{
					FlushIfAny ();
					curAnchor = a;
					open = c.Open;
					high = c.High;
					low = c.Low;
					close = c.Close;
					}
				else
					{
					if (c.High > high) high = c.High;
					if (c.Low < low) low = c.Low;
					close = c.Close;
					}
				}

			FlushIfAny ();
			return result;
			}
		}
	}
