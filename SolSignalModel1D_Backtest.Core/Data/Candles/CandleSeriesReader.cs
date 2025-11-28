using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	public static class CandleSeriesReader
		{
		private static string TfToSuffix ( CandleTimeframe tf ) => tf switch
			{
				CandleTimeframe.M1 => "1m",
				CandleTimeframe.H1 => "1h",
				CandleTimeframe.H6 => "6h",
				_ => throw new NotSupportedException ($"Unsupported TF: {tf}")
				};

		// Универсальный ридер «всё за всё время»
		public static List<CandleNdjsonStore.CandleLine> ReadAll ( string symbol, CandleTimeframe tf )
			{
			var file = CandlePaths.File (symbol, TfToSuffix (tf));
			var store = new CandleNdjsonStore (file);
			// Range: Min..Max, чтение с фильтром внутри store
			return store.ReadRange (DateTime.MinValue, DateTime.MaxValue);
			}

		public static List<Candle1m> ReadAll1m ( string symbol ) =>
			ReadAll (symbol, CandleTimeframe.M1)
				.Select (l => new Candle1m
					{
					OpenTimeUtc = l.OpenTimeUtc,
					Open = l.Open,
					High = l.High,
					Low = l.Low,
					Close = l.Close
					})
				.ToList ();

		public static List<Candle1h> ReadAll1h ( string symbol ) =>
			ReadAll (symbol, CandleTimeframe.H1)
				.Select (l => new Candle1h 
					{ 
					OpenTimeUtc = l.OpenTimeUtc, 
					Open = l.Open, 
					High = l.High, 
					Low = l.Low, 
					Close = l.Close 
					})
				.ToList ();

		public static List<Candle6h> ReadAll6h ( string symbol ) =>
			ReadAll (symbol, CandleTimeframe.H6)
				.Select (l => new Candle6h 
					{ 
					OpenTimeUtc = l.OpenTimeUtc,
					Open = l.Open,
					High = l.High, 
					Low = l.Low,
					Close = l.Close 
					})
				.ToList ();
		}
	}
