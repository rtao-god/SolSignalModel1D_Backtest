namespace SolSignalModel1D_Backtest.Core.ML.Shared
	{
	/// <summary>
	/// Имена признаков для дневной / микро-модели.
	/// Индекс строго соответствует порядку добавления фич в RowBuilder.Causal.Features.
	/// </summary>
	public static class DailyFeatureSchema
		{
		public const int UsedCount = 24;

		/// <summary>
		/// Имена фич длиной ровно MlSchema.FeatureCount.
		/// Первые UsedCount — реальные признаки, остальные — заглушки "_pad_i".
		/// </summary>
		public static readonly string[] Names = Build ();

		private static string[] Build ()
			{
			var names = new string[MlSchema.FeatureCount];

			names[0] = "SolRet30";
			names[1] = "BtcRet30";
			names[2] = "SolBtcRet30";
			names[3] = "SolRet1";
			names[4] = "SolRet3";
			names[5] = "BtcRet1";
			names[6] = "BtcRet3";
			names[7] = "FngNorm";
			names[8] = "DxyChg30";
			names[9] = "GoldChg30";
			names[10] = "BtcVs200";
			names[11] = "SolRsiCenteredNorm";
			names[12] = "RsiSlope3Norm";
			names[13] = "GapBtcSol1";
			names[14] = "GapBtcSol3";
			names[15] = "RegimeDownFlag";
			names[16] = "AtrPct";
			names[17] = "DynVol";
			names[18] = "Funding";
			names[19] = "OiMln";
			names[20] = "HardRegimeFlag";
			names[21] = "SolAboveEma50";
			names[22] = "SolEma50vs200";
			names[23] = "BtcEma50vs200";

			for (int i = UsedCount; i < names.Length; i++)
				names[i] = $"_pad_{i}";

			return names;
			}
		}

	/// <summary>
	/// Имена признаков для SL-модели (SlOfflineBuilder/SlFeatureBuilder).
	/// SL-вектор фиксированно 11-мерный.
	/// </summary>
	public static class SlFeatureSchema
		{
		public const int UsedCount = 11;

		/// <summary>
		/// Имена SL-фич длиной ровно UsedCount.
		/// Индексы должны совпадать с порядком записи в SlFeatureBuilder.
		/// </summary>
		public static readonly string[] Names = Build ();

		private static string[] Build ()
			{
			var names = new string[UsedCount];

			names[0] = "GoLongFlag";
			names[1] = "StrongSignalFlag";
			names[2] = "DayMinMove";
			names[3] = "Range6hTotal";
			names[4] = "Range2hEarly";
			names[5] = "Range2hLast";
			names[6] = "DistToHigh6h";
			names[7] = "DistToLow6h";
			names[8] = "Last1hWickiness";
			names[9] = "EntryHourNorm";
			names[10] = "DayMinMoveHighFlag";

			return names;
			}
		}

	public static class MicroFeatureSchema
		{
		public static string[] Names => DailyFeatureSchema.Names;
		}
	}
