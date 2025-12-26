using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static List<Candle6h> ReadAll6h ( string symbol )
			{
			var path = CandlePaths.File (symbol, "6h");
			if (!File.Exists (path)) return new List<Candle6h> ();
			var store = new CandleNdjsonStore (path);
			var lines = store.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			return lines.Select (l => new Candle6h
				{
				OpenTimeUtc = l.OpenTimeUtc,
				Open = l.Open,
				High = l.High,
				Low = l.Low,
				Close = l.Close
				}).ToList ();
			}

		private static List<Candle1h> ReadAll1h ( string symbol )
			{
			var path = CandlePaths.File (symbol, "1h");
			if (!File.Exists (path)) return new List<Candle1h> ();
			var store = new CandleNdjsonStore (path);
			var lines = store.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			return lines.Select (l => new Candle1h
				{
				OpenTimeUtc = l.OpenTimeUtc,
				Open = l.Open,
				High = l.High,
				Low = l.Low,
				Close = l.Close
				}).ToList ();
			}

		private static List<Candle1m> ReadAll1m ( string symbol )
			{
			var path = CandlePaths.File (symbol, "1m");
			if (!File.Exists (path)) return new List<Candle1m> ();
			var store = new CandleNdjsonStore (path);
			var lines = store.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			return lines.Select (l => new Candle1m
				{
				OpenTimeUtc = l.OpenTimeUtc,
				Open = l.Open,
				High = l.High,
				Low = l.Low,
				Close = l.Close
				}).ToList ();
			}
		}
	}
