using System;
using System.IO;

namespace SolSignalModel1D_Backtest.Core.Data
	{
	/// <summary>
	/// Все пути к вспомогательным JSON тут.
	/// LEGACY
	/// </summary>
	public static class Paths
		{
		// база — где лежит собранный exe / dll
		public static readonly string BaseDir =
			AppContext.BaseDirectory;

		// папка, где мы держим ручные индикаторы
		public static readonly string IndicatorsDir =
			Path.Combine (BaseDir, "JsonIndicators");

		public static string LiquidityJson =>
			Path.Combine (IndicatorsDir, "liquidity.json");

		public static string FiboJson =>
			Path.Combine (IndicatorsDir, "fibo.json");

		// если нужно будет extra.json тоже туда положить
		public static string ExtraJson =>
			Path.Combine (BaseDir, "extra.json");
		}
	}
