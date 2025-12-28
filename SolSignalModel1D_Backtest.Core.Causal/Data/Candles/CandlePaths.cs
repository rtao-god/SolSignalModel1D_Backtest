using System.IO;
using SolSignalModel1D_Backtest.Core.Causal.Infra;

namespace SolSignalModel1D_Backtest.Core.Causal.Data.Candles
	{
	public static class CandlePaths
		{
		public static string BaseDir => PathConfig.CandlesDir;

		/// <summary>
		/// Основной NDJSON-файл по таймфрейму:
		/// SYMBOL-tf.ndjson (только будни)
		/// </summary>
		public static string File ( string symbol, string tf ) =>
			Path.Combine (BaseDir, $"{symbol}-{tf}.ndjson");

		/// <summary>
		/// NDJSON-файл только для выходных:
		/// SYMBOL-tf-weekends.ndjson.
		/// Для 1m: SOLUSDT-1m-weekends.ndjson.
		/// </summary>
		public static string WeekendFile ( string symbol, string tf ) =>
			Path.Combine (BaseDir, $"{symbol}-{tf}-weekends.ndjson");
		}
	}
