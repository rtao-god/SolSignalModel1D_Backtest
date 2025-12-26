using System.Globalization;
using System.Text.Json;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	public static class CandleNdjsonReader
		{
		// Загружает 1m свечи из NDJSON в полуинтервале [fromUtc, toUtc)
		public static List<Candle1m> LoadRange1m ( string path, DateTime fromUtc, DateTime toUtc )
			{
			var res = new List<Candle1m> (1024);
			if (!File.Exists (path)) return res;

			using var fs = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var sr = new StreamReader (fs);

			string? line;
			int lineIndex = 0;

			while ((line = sr.ReadLine ()) != null)
				{
				lineIndex++;
				if (line.Length == 0) continue;

				try
					{
					using var doc = JsonDocument.Parse (line);
					var root = doc.RootElement;

					DateTime t = DateTime.Parse (
						root.GetProperty ("t").GetString ()!,
						null,
						DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

					if (t < fromUtc) continue;
					if (t >= toUtc) break; // файл отсортирован по возрастанию времени

					double o = root.GetProperty ("o").GetDouble ();
					double h = root.GetProperty ("h").GetDouble ();
					double l = root.GetProperty ("l").GetDouble ();
					double c = root.GetProperty ("c").GetDouble ();

					res.Add (new Candle1m
						{
						OpenTimeUtc = t,
						Open = o,
						High = h,
						Low = l,
						Close = c
						});
					}
				catch (Exception ex)
					{
					// Любая проблема с JSON/датой/числами – явный фейл.
					throw new InvalidOperationException (
						$"[candles:LoadRange1m] invalid NDJSON in '{path}' at line #{lineIndex}: '{line}'",
						ex);
					}
				}
			return res;
			}
		}
	}
