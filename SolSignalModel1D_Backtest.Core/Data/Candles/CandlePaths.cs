using System.IO;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.Data.Candles
	{
	public static class CandlePaths
		{
		public static string BaseDir => PathConfig.CandlesDir;
		public static string File ( string symbol, string tf ) =>
			Path.Combine (BaseDir, $"{symbol}-{tf}.ndjson");
		}
	}
