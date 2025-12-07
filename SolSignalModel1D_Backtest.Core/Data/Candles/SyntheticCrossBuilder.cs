namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	public static class SyntheticCrossBuilder
		{
		public static void EnsureCross6hFromUsdt ( string outSymbol, string solUsdt = "SOLUSDT", string btcUsdt = "BTCUSDT" )
			{
			// если уже есть - выходим
			if (CandleSeriesReader.ReadAll6h (outSymbol).Count > 0) return;

			var sol = CandleSeriesReader.ReadAll6h (solUsdt);
			var btc = CandleSeriesReader.ReadAll6h (btcUsdt);
			if (sol.Count == 0 || btc.Count == 0)
				throw new InvalidOperationException ($"[cross] need 6h SOLUSDT & BTCUSDT first (in {Infra.PathConfig.CandlesDir}).");

			var bIdx = btc.ToDictionary (x => x.OpenTimeUtc);
			var outLines = new List<CandleNdjsonStore.CandleLine> (sol.Count);

			foreach (var s in sol)
				{
				if (!bIdx.TryGetValue (s.OpenTimeUtc, out var b)) continue;
				if (b.Open <= 0 || b.High <= 0 || b.Low <= 0 || b.Close <= 0) continue;

				outLines.Add (new CandleNdjsonStore.CandleLine (
					s.OpenTimeUtc,
					s.Open / b.Open,
					s.High / b.High,
					s.Low / b.Low,
					s.Close / b.Close
				));
				}

			var file = CandlePaths.File (outSymbol, "6h");
			new CandleNdjsonStore (file).Append (outLines);
			Console.WriteLine ($"[cross] built {outSymbol}-6h from SOLUSDT/BTCUSDT 6h: {outLines.Count} rows");
			}
		}
	}
